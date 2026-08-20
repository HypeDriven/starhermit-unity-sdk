using System;
using System.Collections.Generic;
using System.Text;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// An authenticated account session: the token pair plus what the SDK can tell about it locally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refresh token is deliberately hard to leak. It is never included in <see cref="ToString"/>,
    /// never logged, and never reaches telemetry; only <see cref="RefreshToken"/> exposes it, for the
    /// token store and the refresh call.
    /// </para>
    /// <para>
    /// Claims are read from the access token without verifying its signature. That is a local
    /// convenience for expiry and scope checks only - the server remains the sole authority on what a
    /// token may do, and the SDK never grants itself a permission by reading one.
    /// </para>
    /// </remarks>
    public sealed class StarhermitSession
    {
        /// <summary>Creates a session.</summary>
        /// <param name="accessToken">Bearer access token.</param>
        /// <param name="refreshToken">Rotating refresh token.</param>
        /// <param name="userId">Account id, when known independently of the token.</param>
        public StarhermitSession(string accessToken, string refreshToken, Guid? userId = null)
        {
            AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            RefreshToken = refreshToken ?? throw new ArgumentNullException(nameof(refreshToken));

            var claims = StarhermitJwt.ReadClaims(accessToken);
            UserId = userId ?? claims.Subject;
            AccessTokenExpiresAt = claims.ExpiresAt;
            IssuedAt = claims.IssuedAt;
            AuthenticationMethod = claims.AuthenticationMethod;
            Permissions = claims.Permissions;
            Roles = claims.Roles;
        }

        /// <summary>The bearer token sent with account-scoped requests.</summary>
        public string AccessToken { get; }

        /// <summary>The rotating refresh token. Treat as a credential: never log or display it.</summary>
        public string RefreshToken { get; }

        /// <summary>The signed-in account, when the token carried a subject.</summary>
        public Guid? UserId { get; }

        /// <summary>When the access token expires, from its <c>exp</c> claim.</summary>
        public DateTimeOffset? AccessTokenExpiresAt { get; }

        /// <summary>When the access token was issued, from its <c>iat</c> claim.</summary>
        public DateTimeOffset? IssuedAt { get; }

        /// <summary>
        /// How the account authenticated - <c>oauth</c> or <c>public_key</c>. Some account operations
        /// are restricted to an OAuth session by the server.
        /// </summary>
        public string? AuthenticationMethod { get; }

        /// <summary>Permissions the access token grants, as the server wrote them.</summary>
        public IReadOnlyList<string> Permissions { get; }

        /// <summary>Roles the access token carries.</summary>
        public IReadOnlyList<string> Roles { get; }

        /// <summary>
        /// True when the access token is at or past expiry, allowing <paramref name="leeway"/> of clock
        /// skew so the SDK refreshes just before the server would reject it.
        /// </summary>
        /// <param name="now">Current time.</param>
        /// <param name="leeway">How far ahead of expiry to consider the token spent.</param>
        /// <returns>True when the token should be refreshed before use.</returns>
        public bool IsExpired(DateTimeOffset now, TimeSpan leeway) =>
            AccessTokenExpiresAt.HasValue && AccessTokenExpiresAt.Value - leeway <= now;

        /// <summary>Returns a description that contains no credential material.</summary>
        public override string ToString() =>
            $"StarhermitSession(user={UserId?.ToString() ?? "unknown"}, method={AuthenticationMethod ?? "unknown"}, expires={AccessTokenExpiresAt?.ToString("u") ?? "unknown"})";
    }

    /// <summary>Claims the SDK reads out of an access token for local decisions.</summary>
    public readonly struct StarhermitTokenClaims
    {
        /// <summary>Creates a claim set.</summary>
        /// <param name="subject">The <c>sub</c> claim.</param>
        /// <param name="expiresAt">The <c>exp</c> claim.</param>
        /// <param name="issuedAt">The <c>iat</c> claim.</param>
        /// <param name="authenticationMethod">The <c>auth_method</c> claim.</param>
        /// <param name="permissions">The <c>permission</c> claims.</param>
        /// <param name="roles">The role claims.</param>
        public StarhermitTokenClaims(
            Guid? subject,
            DateTimeOffset? expiresAt,
            DateTimeOffset? issuedAt,
            string? authenticationMethod,
            IReadOnlyList<string> permissions,
            IReadOnlyList<string> roles)
        {
            Subject = subject;
            ExpiresAt = expiresAt;
            IssuedAt = issuedAt;
            AuthenticationMethod = authenticationMethod;
            Permissions = permissions;
            Roles = roles;
        }

        /// <summary>Account id from <c>sub</c>.</summary>
        public Guid? Subject { get; }

        /// <summary>Expiry from <c>exp</c>.</summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary>Issue time from <c>iat</c>.</summary>
        public DateTimeOffset? IssuedAt { get; }

        /// <summary>Authentication method from <c>auth_method</c>.</summary>
        public string? AuthenticationMethod { get; }

        /// <summary>Permissions carried by the token.</summary>
        public IReadOnlyList<string> Permissions { get; }

        /// <summary>Roles carried by the token.</summary>
        public IReadOnlyList<string> Roles { get; }
    }

    /// <summary>
    /// Reads the claim set out of a JWT access token without validating it.
    /// </summary>
    /// <remarks>
    /// Deliberately not a JWT library: no signature verification, no algorithm negotiation, no key
    /// handling. Verification belongs to the server. This exists so the client can answer "is my token
    /// about to expire" without a round trip, and it treats an unreadable token as simply carrying no
    /// claims rather than as an error, because the server's answer is the one that counts.
    /// </remarks>
    public static class StarhermitJwt
    {
        /// <summary>The role claim name emitted by ASP.NET's JWT handler.</summary>
        private const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

        /// <summary>Reads local claims from an access token.</summary>
        /// <param name="accessToken">The encoded JWT.</param>
        /// <returns>The claims, or an empty set when the token cannot be read.</returns>
        public static StarhermitTokenClaims ReadClaims(string? accessToken)
        {
            var empty = new StarhermitTokenClaims(null, null, null, null, Array.Empty<string>(), Array.Empty<string>());
            if (string.IsNullOrEmpty(accessToken)) return empty;

            var parts = accessToken!.Split('.');
            if (parts.Length < 2) return empty;

            JsonValue payload;
            try
            {
                var json = Encoding.UTF8.GetString(DecodeBase64Url(parts[1]));
                if (!JsonParser.TryParse(json, out payload) || !payload.IsObject) return empty;
            }
            catch (FormatException)
            {
                return empty;
            }

            Guid? subject = null;
            var sub = payload["sub"];
            if (sub.Kind == JsonKind.String && Guid.TryParse(sub.AsString(), out var parsed)) subject = parsed;

            return new StarhermitTokenClaims(
                subject,
                ReadUnixTime(payload["exp"]),
                ReadUnixTime(payload["iat"]),
                payload["auth_method"].Kind == JsonKind.String ? payload["auth_method"].AsString() : null,
                ReadStrings(payload["permission"]),
                ReadStrings(payload[RoleClaim].IsNullOrMissing ? payload["role"] : payload[RoleClaim]));
        }

        private static DateTimeOffset? ReadUnixTime(JsonValue value)
        {
            if (value.Kind == JsonKind.Number) return DateTimeOffset.FromUnixTimeSeconds(value.AsInt64());
            if (value.Kind == JsonKind.String && long.TryParse(value.AsString(), out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            return null;
        }

        private static IReadOnlyList<string> ReadStrings(JsonValue value)
        {
            // A JWT collapses a single repeated claim to a bare string and keeps an array otherwise.
            if (value.Kind == JsonKind.String) return new[] { value.AsString() };
            if (!value.IsArray) return Array.Empty<string>();

            var result = new List<string>(value.Count);
            foreach (var item in value.Items)
                if (item.Kind == JsonKind.String)
                    result.Add(item.AsString());
            return result;
        }

        private static byte[] DecodeBase64Url(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
                case 1: throw new FormatException("Malformed base64url segment.");
            }

            return Convert.FromBase64String(padded);
        }
    }
}
