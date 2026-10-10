using System;
using System.Collections.Generic;
using System.Text;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>A public-key registration that is waiting on an emailed confirmation link.</summary>
    public sealed class StarhermitRegistrationReceipt : StarhermitModel
    {
        private StarhermitRegistrationReceipt(JsonValue json) : base(json)
        {
            RegistrationId = json["registrationId"].AsGuidOrNull() ?? Guid.Empty;
            Email = json["email"].AsStringOrNull() ?? string.Empty;
            EmailSent = json["emailSent"].AsBooleanOrDefault();
            DeferralReason = json["deferralReason"].AsStringOrNull();
            Message = json["message"].AsStringOrNull();
        }

        /// <summary>Identifier of the pending registration.</summary>
        public Guid RegistrationId { get; }

        /// <summary>Address the confirmation link was sent to.</summary>
        public string Email { get; }

        /// <summary>
        /// False when the mail was queued rather than sent immediately. The registration is accepted
        /// either way; only the delivery timing differs.
        /// </summary>
        public bool EmailSent { get; }

        /// <summary>Why delivery was deferred, when it was.</summary>
        public string? DeferralReason { get; }

        /// <summary>The server's message for the person who asked.</summary>
        public string? Message { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRegistrationReceipt Read(JsonValue json) => new StarhermitRegistrationReceipt(json);
    }

    /// <summary>The result of opening a public-key verification link: a key, and a session for it.</summary>
    public sealed class StarhermitRegistrationVerification : StarhermitModel
    {
        private StarhermitRegistrationVerification(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            KeyId = json["keyId"].AsGuidOrNull() ?? Guid.Empty;
            Session = new StarhermitSession(
                json["accessToken"].AsStringOrNull() ?? string.Empty,
                json["refreshToken"].AsStringOrNull() ?? string.Empty,
                json["userId"].AsGuidOrNull());
        }

        /// <summary>The account the key is now attached to.</summary>
        public Guid UserId { get; }

        /// <summary>The key that was attached.</summary>
        public Guid KeyId { get; }

        /// <summary>The session issued by completing the registration.</summary>
        public StarhermitSession Session { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRegistrationVerification Read(JsonValue json) => new StarhermitRegistrationVerification(json);
    }

    /// <summary>How many keys a confirmed revocation removed, and how many sessions it ended.</summary>
    public sealed class StarhermitRevocationResult : StarhermitModel
    {
        private StarhermitRevocationResult(JsonValue json) : base(json)
        {
            RevokedKeys = json["revoked"].AsInt32OrDefault();
            SessionsEnded = json["sessionsEnded"].AsInt32OrDefault();
        }

        /// <summary>Number of keys revoked.</summary>
        public int RevokedKeys { get; }

        /// <summary>Number of sessions those keys had authenticated.</summary>
        public int SessionsEnded { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRevocationResult Read(JsonValue json) => new StarhermitRevocationResult(json);
    }

    /// <summary>
    /// A public-key authentication challenge, together with the exact bytes that must be signed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The signature is verified against the server's <em>own</em> serialisation of the challenge
    /// payload, which uses .NET property names rather than the camel-case the payload arrives in.
    /// Re-serialising the response as received therefore produces bytes that will never verify.
    /// </para>
    /// <para>
    /// <see cref="CanonicalPayload"/> rebuilds the server's form: the same members, in declaration
    /// order, under their .NET names, reusing the timestamp text exactly as it was received so no
    /// formatting difference can creep in. This is a coupling to the deployment's serializer, and the
    /// API should hand clients the bytes to sign instead - noted in the SDK's contract report.
    /// </para>
    /// </remarks>
    public sealed class StarhermitChallenge : StarhermitModel
    {
        private StarhermitChallenge(JsonValue json) : base(json)
        {
            ChallengeId = json["challengeId"].AsGuidOrNull() ?? Guid.Empty;
            ExpiresIn = TimeSpan.FromSeconds(json["expiresIn"].AsInt32OrDefault());

            var payload = json["payload"];
            Fingerprint = payload["fingerprint"].AsStringOrNull() ?? string.Empty;
            Issuer = payload["issuer"].AsStringOrNull() ?? string.Empty;
            Audience = payload["audience"].AsStringOrNull() ?? string.Empty;
            Nonce = payload["nonce"].AsStringOrNull() ?? string.Empty;
            ExpiresAt = payload["expiry"].AsDateTimeOffsetOrNull();
            IssuedAt = payload["clientTimestamp"].AsDateTimeOffsetOrNull();

            CanonicalPayload = BuildCanonicalPayload(payload);
        }

        /// <summary>Identifier to send back with the signature.</summary>
        public Guid ChallengeId { get; }

        /// <summary>How long the challenge remains valid.</summary>
        public TimeSpan ExpiresIn { get; }

        /// <summary>Fingerprint of the key the challenge was issued for.</summary>
        public string Fingerprint { get; }

        /// <summary>Issuer named in the payload.</summary>
        public string Issuer { get; }

        /// <summary>Audience named in the payload.</summary>
        public string Audience { get; }

        /// <summary>Single-use nonce.</summary>
        public string Nonce { get; }

        /// <summary>When the challenge expires.</summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary>When the server issued the challenge.</summary>
        public DateTimeOffset? IssuedAt { get; }

        /// <summary>The exact bytes to sign.</summary>
        public byte[] CanonicalPayload { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitChallenge Read(JsonValue json) => new StarhermitChallenge(json);

        private static byte[] BuildCanonicalPayload(JsonValue payload)
        {
            // Property order and names mirror Platform.Application.Services.ChallengePayload, because
            // that class is what the server serialises when it verifies the signature. Timestamps are
            // copied through as their received text rather than reformatted: System.Text.Json writes
            // a DateTimeOffset the same way into the response and into the bytes it verifies.
            //
            // The string members are escaped the way System.Text.Json's default encoder escapes them,
            // not the way JSON requires. The nonce is base64, so about three challenges in ten carry
            // a '+', which the server's bytes hold as \u002B; signing a literal '+' failed exactly those
            // sign-ins, at random.
            var builder = new StringBuilder(256);
            builder.Append('{');
            AppendRaw(builder, "ChallengeId", payload["challengeId"].AsStringOrNull());
            builder.Append(',');
            AppendEscaped(builder, "Fingerprint", payload["fingerprint"].AsStringOrNull());
            builder.Append(',');
            AppendEscaped(builder, "Issuer", payload["issuer"].AsStringOrNull());
            builder.Append(',');
            AppendEscaped(builder, "Audience", payload["audience"].AsStringOrNull());
            builder.Append(',');
            AppendRaw(builder, "Expiry", payload["expiry"].AsStringOrNull());
            builder.Append(',');
            AppendEscaped(builder, "Nonce", payload["nonce"].AsStringOrNull());
            builder.Append(',');
            AppendRaw(builder, "ClientTimestamp", payload["clientTimestamp"].AsStringOrNull());
            builder.Append('}');
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        /// <summary>A Guid or DateTimeOffset member: System.Text.Json formats these without escaping.</summary>
        private static void AppendRaw(StringBuilder builder, string name, string? value) =>
            builder.Append('"').Append(name).Append("\":\"").Append(value ?? string.Empty).Append('"');

        /// <summary>A string member, escaped as <c>JavaScriptEncoder.Default</c> escapes it.</summary>
        private static void AppendEscaped(StringBuilder builder, string name, string? value)
        {
            builder.Append('"').Append(name).Append("\":\"");
            foreach (var c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\r': builder.Append("\\r"); break;
                    default:
                        // HTML-sensitive characters, controls and everything outside Basic Latin are
                        // written as \uXXXX with upper-case hex - surrogate pairs one half at a time.
                        if (c < 0x20 || c > 0x7E || c == '"' || c == '&' || c == '\'' || c == '+' ||
                            c == '<' || c == '>' || c == '`')
                            builder.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }

            builder.Append('"');
        }
    }

    /// <summary>A public key registered on the account.</summary>
    public sealed class StarhermitPublicKey : StarhermitModel
    {
        private StarhermitPublicKey(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            KeyType = json["keyType"].AsStringOrNull() ?? string.Empty;
            KeyData = json["keyData"].AsStringOrNull() ?? string.Empty;
            Label = json["label"].AsStringOrNull();
            Fingerprint = json["fingerprint"].AsStringOrNull();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            IsRevoked = json["isRevoked"].AsBooleanOrDefault();
            RevokedAt = json["revokedAt"].AsDateTimeOffsetOrNull();
            LastUsedAt = json["lastUsedAt"].AsDateTimeOffsetOrNull();
            Metadata = json["metadata"].AsStringOrNull();
        }

        /// <summary>Key identifier.</summary>
        public Guid Id { get; }

        /// <summary>Algorithm, as <see cref="StarhermitKeyTypes"/> names it.</summary>
        public string KeyType { get; }

        /// <summary>The public key material, base64 encoded.</summary>
        public string KeyData { get; }

        /// <summary>Label the owner gave the key.</summary>
        public string? Label { get; }

        /// <summary>Server-computed fingerprint.</summary>
        public string? Fingerprint { get; }

        /// <summary>When the key was registered.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>True when the key has been revoked.</summary>
        public bool IsRevoked { get; }

        /// <summary>When it was revoked.</summary>
        public DateTimeOffset? RevokedAt { get; }

        /// <summary>When it last authenticated a session.</summary>
        public DateTimeOffset? LastUsedAt { get; }

        /// <summary>Free-form metadata stored with the key.</summary>
        public string? Metadata { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitPublicKey Read(JsonValue json) => new StarhermitPublicKey(json);
    }

    /// <summary>Keys removed by a revoke-all, and how many sessions ended with them.</summary>
    public sealed class StarhermitKeyRevocation : StarhermitModel
    {
        private StarhermitKeyRevocation(JsonValue json) : base(json)
        {
            // The key-management routes answer with the revoked rows themselves under "revoked",
            // while the emailed confirmation route answers with a count under the same name.
            RevokedKeys = json["revoked"].AsList(StarhermitPublicKey.Read);
            SessionsEnded = json["sessionsEnded"].AsInt32OrDefault();
        }

        /// <summary>The keys that were revoked.</summary>
        public IReadOnlyList<StarhermitPublicKey> RevokedKeys { get; }

        /// <summary>Sessions those keys had authenticated.</summary>
        public int SessionsEnded { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitKeyRevocation Read(JsonValue json) => new StarhermitKeyRevocation(json);
    }

    /// <summary>Authentication methods the API distinguishes.</summary>
    public static class StarhermitAuthMethods
    {
        /// <summary>Signed in through an identity provider.</summary>
        public const string OAuth = "oauth";

        /// <summary>Signed in by proving control of a registered public key.</summary>
        public const string PublicKey = "public_key";
    }
    /// <summary>An OAuth provider the deployment offers.</summary>
    public sealed class StarhermitOAuthProvider : StarhermitModel
    {
        private StarhermitOAuthProvider(JsonValue json) : base(json)
        {
            Provider = json["provider"].AsStringOrNull() ?? string.Empty;
            DisplayName = json["displayName"].AsStringOrNull() ?? string.Empty;
            AuthorizeUrl = json["authorizeUrl"].AsStringOrNull() ?? string.Empty;
            LinksExistingAccountsByEmail = json["linksExistingAccountsByEmail"].AsBooleanOrDefault();
        }

        /// <summary>The provider key to pass to <see cref="StarhermitAuthClient.BuildAuthorizeUri"/>.</summary>
        public string Provider { get; }

        /// <summary>The name to put on the button.</summary>
        public string DisplayName { get; }

        /// <summary>The deployment's own authorize URL for it.</summary>
        public string AuthorizeUrl { get; }

        /// <summary>
        /// True when signing in through this provider finds an existing account by its email address.
        /// When false, a player who first signed up another way gets a new account unless they link
        /// this provider from their existing one.
        /// </summary>
        public bool LinksExistingAccountsByEmail { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitOAuthProvider Read(JsonValue json) => new StarhermitOAuthProvider(json);
    }

    /// <summary>A single-use credential for one socket handshake.</summary>
    /// <remarks>
    /// Pass it as <c>?ticket=</c>. It is a credential: the SDK redacts it from logs, and an
    /// application must not record it either.
    /// </remarks>
    public sealed class StarhermitConnectionTicket : StarhermitModel
    {
        private StarhermitConnectionTicket(JsonValue json) : base(json)
        {
            Ticket = json["ticket"].AsStringOrNull()
                     ?? throw new StarhermitSerializationException("The response carried no ticket.");
            ExpiresInSeconds = json["expiresIn"].AsInt32OrDefault();
        }

        /// <summary>The ticket.</summary>
        public string Ticket { get; }

        /// <summary>Seconds until it can no longer be presented.</summary>
        public int ExpiresInSeconds { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitConnectionTicket Read(JsonValue json) => new StarhermitConnectionTicket(json);

        /// <inheritdoc />
        public override string ToString() => "StarhermitConnectionTicket(***)";
    }
}
