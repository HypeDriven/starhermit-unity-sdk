using System;
using System.Collections.Generic;
using System.Globalization;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// A request reached the API and came back unsuccessful.
    /// </summary>
    /// <remarks>
    /// Everything a caller needs to diagnose the failure without re-reading the raw response is here:
    /// status, the server's own message, the request id to quote in a bug report, any
    /// <c>Retry-After</c>, and a size-capped copy of the body. Catch a typed subclass to handle one
    /// class of failure; catch this to handle them all.
    /// </remarks>
    public class StarhermitApiException : StarhermitException
    {
        /// <summary>Creates the exception from a parsed error.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitApiException(StarhermitErrorInfo error)
            : base(error.BuildMessage())
        {
            Status = error.Status;
            ErrorCode = error.ErrorCode;
            ServerMessage = error.ServerMessage;
            RequestId = error.RequestId;
            RetryAfter = error.RetryAfter;
            Headers = error.Headers ?? EmptyHeaders;
            RawBody = error.RawBody ?? string.Empty;
            Method = error.Method ?? string.Empty;
            Path = error.Path ?? string.Empty;
            Limit = error.Limit;
            Used = error.Used;
            LimitKey = error.LimitKey;
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
            new Dictionary<string, string>(0);

        /// <summary>HTTP status code of the response.</summary>
        public int Status { get; }

        /// <summary>
        /// Machine-readable error code when the deployment supplies one.
        /// </summary>
        /// <remarks>
        /// The v1 API answers most failures with a prose <c>error</c> member rather than a code, so
        /// this is frequently null; branch on the exception type and <see cref="Status"/> instead, and
        /// show <see cref="ServerMessage"/> to a human.
        /// </remarks>
        public string? ErrorCode { get; }

        /// <summary>The server's own description of the failure, safe to surface to a player.</summary>
        public string? ServerMessage { get; }

        /// <summary>Correlation id for the request, when the deployment returns one.</summary>
        public string? RequestId { get; }

        /// <summary>How long the server asked the caller to wait, from <c>Retry-After</c>.</summary>
        public TimeSpan? RetryAfter { get; }

        /// <summary>Response headers, compared case-insensitively.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>The response body, truncated to the configured diagnostic cap.</summary>
        public string RawBody { get; }

        /// <summary>HTTP method of the failed request.</summary>
        public string Method { get; }

        /// <summary>Path of the failed request, without query string secrets.</summary>
        public string Path { get; }

        /// <summary>
        /// The number in force for the caller when the refusal was a tunable limit - a save's maximum
        /// size, a slot count, a rate - or null when the response named none.
        /// </summary>
        /// <remarks>
        /// Limits are set per account and per game by the platform's operators, so this is the only
        /// reliable source for the value a message to the player should quote. The unit follows the
        /// refusal: bytes for a size or quota, a count for slots, requests per window for a rate.
        /// </remarks>
        public long? Limit { get; }

        /// <summary>How much of <see cref="Limit"/> was already used, when the response said.</summary>
        public long? Used { get; }

        /// <summary>The operator-facing key of the limit that refused the request, when the response named it.</summary>
        public string? LimitKey { get; }

        /// <summary>Builds the most specific exception type for a failed response.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        /// <returns>A typed exception matching the status.</returns>
        public static StarhermitApiException Create(StarhermitErrorInfo error)
        {
            switch (error.Status)
            {
                case 400:
                case 422:
                    return error.ValidationErrors != null && error.ValidationErrors.Count > 0
                        ? new StarhermitValidationException(error)
                        : (StarhermitApiException)new StarhermitBadRequestException(error);
                case 401:
                    return new StarhermitAuthenticationException(error);
                case 402:
                    return new StarhermitEntitlementException(error);
                case 403:
                    return new StarhermitAuthorizationException(error);
                case 404:
                    return new StarhermitNotFoundException(error);
                case 409:
                    return new StarhermitConflictException(error);
                case 412:
                    return new StarhermitPreconditionFailedException(error);
                case 413:
                case 507:
                    // 507 is the account's storage being full: the deployment is healthy and a retry
                    // cannot succeed, so it is a quota, not a server failure.
                    return new StarhermitQuotaExceededException(error);
                case 429:
                    return new StarhermitRateLimitException(error);
                default:
                    return error.Status >= 500
                        ? new StarhermitServerException(error)
                        : new StarhermitApiException(error);
            }
        }
    }

    /// <summary>A malformed request the server rejected without field-level detail.</summary>
    public sealed class StarhermitBadRequestException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitBadRequestException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>
    /// The request needs a valid session and did not have one. Raised after the pipeline's single
    /// coordinated refresh attempt has already failed or was not eligible.
    /// </summary>
    public sealed class StarhermitAuthenticationException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitAuthenticationException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>
    /// The caller is authenticated but not permitted. Never retried: a game-scoped launch token
    /// reaching for an account route, or a publisher action without the membership to perform it,
    /// will fail identically however many times it is sent.
    /// </summary>
    public sealed class StarhermitAuthorizationException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitAuthorizationException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>The addressed resource does not exist, or is hidden from this caller.</summary>
    public sealed class StarhermitNotFoundException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitNotFoundException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>The request conflicts with current server state, such as joining a full room.</summary>
    public sealed class StarhermitConflictException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitConflictException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>
    /// A conditional write lost to another writer: the version named in <c>If-Match</c> is no longer
    /// current, or <c>If-None-Match: *</c> found one already there. Nothing was written.
    /// </summary>
    /// <remarks>
    /// Load the version in <see cref="CurrentETag"/>, decide what the result should be, and write again
    /// naming it. Retrying the same request unchanged can never succeed.
    /// </remarks>
    public sealed class StarhermitPreconditionFailedException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitPreconditionFailedException(StarhermitErrorInfo error) : base(error)
        {
            CurrentETag = error.ETag;
            if (CurrentETag == null && Headers.TryGetValue("ETag", out var header) && !string.IsNullOrEmpty(header))
                CurrentETag = header;
        }

        /// <summary>The version that is current now, or null when nothing is stored any more.</summary>
        public string? CurrentETag { get; }
    }

    /// <summary>
    /// The request would take the caller past a size or storage limit - the API's <c>413</c> (this one
    /// payload is too large) or <c>507</c> (the account's storage is full).
    /// </summary>
    /// <remarks>
    /// <see cref="StarhermitApiException.Limit"/> and <see cref="StarhermitApiException.Used"/> carry the
    /// numbers in force for this account, and <see cref="StarhermitApiException.ErrorCode"/> says which
    /// limit it was (for cloud saves, <c>cloud_save_too_large</c> or <c>cloud_save_quota_exceeded</c>).
    /// A slot limit is a <c>409</c> and arrives as <see cref="StarhermitConflictException"/> with the same
    /// properties set. Not retried: the same request will be refused again.
    /// </remarks>
    public sealed class StarhermitQuotaExceededException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitQuotaExceededException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>
    /// The request needs an entitlement the caller does not hold - the API's <c>402</c>, returned for
    /// example when claiming a title that is not free.
    /// </summary>
    public sealed class StarhermitEntitlementException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitEntitlementException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>The caller is being throttled. <see cref="StarhermitApiException.RetryAfter"/> carries the server's wait.</summary>
    public sealed class StarhermitRateLimitException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitRateLimitException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>The deployment failed to handle the request (5xx).</summary>
    public sealed class StarhermitServerException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitServerException(StarhermitErrorInfo error) : base(error)
        {
        }
    }

    /// <summary>
    /// The request body failed validation, with per-field detail preserved under the JSON field names
    /// the API used.
    /// </summary>
    public sealed class StarhermitValidationException : StarhermitApiException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="error">Everything known about the failed response.</param>
        public StarhermitValidationException(StarhermitErrorInfo error) : base(error)
        {
            Errors = error.ValidationErrors ?? new Dictionary<string, IReadOnlyList<string>>(0);
        }

        /// <summary>Validation messages keyed by the wire field name they apply to.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; }
    }

    /// <summary>
    /// Everything the SDK could learn about a failed response, assembled once by the transport
    /// pipeline and handed to the exception factory.
    /// </summary>
    public sealed class StarhermitErrorInfo
    {
        /// <summary>HTTP status code.</summary>
        public int Status { get; set; }

        /// <summary>HTTP method of the request.</summary>
        public string? Method { get; set; }

        /// <summary>Request path, with query-string credentials already removed.</summary>
        public string? Path { get; set; }

        /// <summary>Machine-readable error code, when the payload carried one.</summary>
        public string? ErrorCode { get; set; }

        /// <summary>The server's description of the failure.</summary>
        public string? ServerMessage { get; set; }

        /// <summary>Correlation id from the response.</summary>
        public string? RequestId { get; set; }

        /// <summary>Parsed <c>Retry-After</c>, in either seconds or HTTP-date form.</summary>
        public TimeSpan? RetryAfter { get; set; }

        /// <summary>Response headers.</summary>
        public IReadOnlyDictionary<string, string>? Headers { get; set; }

        /// <summary>Response body, already truncated to the diagnostic cap.</summary>
        public string? RawBody { get; set; }

        /// <summary>Field-level validation messages, when the payload carried them.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>>? ValidationErrors { get; set; }

        /// <summary>The <c>limit</c> member of a limit refusal: the number in force for the caller.</summary>
        public long? Limit { get; set; }

        /// <summary>The <c>used</c> member of a quota refusal.</summary>
        public long? Used { get; set; }

        /// <summary>The <c>limitKey</c> member of a limit refusal.</summary>
        public string? LimitKey { get; set; }

        /// <summary>The <c>etag</c> member of a precondition failure: the version current now.</summary>
        public string? ETag { get; set; }

        /// <summary>
        /// Reads the deployment's error shapes: <c>{ "error": "..." }</c> from the controllers,
        /// <c>ProblemDetails</c> from the exception handler, and <c>ValidationProblemDetails</c> from
        /// model binding. An unparseable body is not an error in itself - status still decides.
        /// </summary>
        /// <param name="body">The raw response body.</param>
        public void ReadBody(string? body)
        {
            RawBody = body;
            if (!JsonParser.TryParse(body, out var json) || !json.IsObject) return;

            var error = json["error"];
            if (error.Kind == JsonKind.String)
            {
                ServerMessage = error.AsString();
                // The v1 API writes prose here; treat a single token as a code, prose as a message.
                if (LooksLikeCode(ServerMessage)) ErrorCode = ServerMessage;
            }

            var code = json["code"];
            // The body handed in here is already redacted, and "code" is redacted by name because an
            // OAuth authorization code travels under it. A placeholder is not an error code;
            // ReadErrorCode recovers the real one from the raw body.
            if (code.Kind == JsonKind.String && code.AsString() != StarhermitRedactor.Placeholder) ErrorCode = code.AsString();

            if (ServerMessage == null)
            {
                var detail = json["detail"];
                var title = json["title"];
                if (detail.Kind == JsonKind.String) ServerMessage = detail.AsString();
                else if (title.Kind == JsonKind.String) ServerMessage = title.AsString();
                else if (json["message"].Kind == JsonKind.String) ServerMessage = json["message"].AsString();
            }

            Limit = ReadWholeNumber(json["limit"]);
            Used = ReadWholeNumber(json["used"]);
            if (json["limitKey"].Kind == JsonKind.String) LimitKey = json["limitKey"].AsString();
            if (json["etag"].Kind == JsonKind.String) ETag = json["etag"].AsString();

            var traceId = json["traceId"];
            if (RequestId == null && traceId.Kind == JsonKind.String) RequestId = traceId.AsString();

            var errors = json["errors"];
            if (errors.IsObject)
            {
                var map = new Dictionary<string, IReadOnlyList<string>>(errors.Count, StringComparer.Ordinal);
                foreach (var member in errors.Members)
                {
                    if (member.Value.IsArray)
                    {
                        var messages = new List<string>(member.Value.Count);
                        foreach (var item in member.Value.Items)
                            if (item.Kind == JsonKind.String)
                                messages.Add(item.AsString());
                        map[member.Key] = messages;
                    }
                    else if (member.Value.Kind == JsonKind.String)
                    {
                        map[member.Key] = new[] { member.Value.AsString() };
                    }
                }

                if (map.Count > 0) ValidationErrors = map;
            }
        }

        /// <summary>
        /// Reads the top-level <c>code</c> member from the unredacted body - <c>rate_limited</c>,
        /// <c>cloud_save_too_large</c> - which redaction by name would otherwise hide.
        /// </summary>
        /// <remarks>
        /// Only a snake_case identifier is taken. That is the shape of every error code the API
        /// writes, and not the shape of an OAuth authorization code, which is the secret the
        /// redactor hides under the same name.
        /// </remarks>
        /// <param name="rawBody">The response body before redaction.</param>
        public void ReadErrorCode(string? rawBody)
        {
            if (!JsonParser.TryParse(rawBody, out var json) || !json.IsObject) return;
            var code = json["code"];
            if (code.Kind == JsonKind.String && IsErrorCodeIdentifier(code.AsString())) ErrorCode = code.AsString();
        }

        /// <summary>Composes the exception message shown in logs and stack traces.</summary>
        /// <returns>A message that never contains credentials.</returns>
        public string BuildMessage()
        {
            var reason = string.IsNullOrEmpty(ServerMessage)
                ? ReasonPhrase(Status)
                : ServerMessage!;
            var request = string.IsNullOrEmpty(Method) && string.IsNullOrEmpty(Path)
                ? string.Empty
                : $" ({Method} {Path})";
            var correlation = string.IsNullOrEmpty(RequestId) ? string.Empty : $" [request {RequestId}]";
            return string.Format(
                CultureInfo.InvariantCulture,
                "Starhermit API returned {0}: {1}{2}{3}",
                Status,
                reason,
                request,
                correlation);
        }

        private static long? ReadWholeNumber(JsonValue value)
        {
            if (value.Kind != JsonKind.Number) return null;
            return long.TryParse(value.AsNumberText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (long?)null;
        }

        private static bool IsErrorCodeIdentifier(string value)
        {
            // [a-z]+(_[a-z0-9]+)* - letters first, so a hex or base64 token cannot pass for one.
            if (value.Length == 0 || value.Length > 64 || value[0] < 'a' || value[0] > 'z') return false;
            var digitsAllowed = false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '_')
                {
                    if (i == value.Length - 1 || value[i - 1] == '_') return false;
                    digitsAllowed = true;
                }
                else if (c >= '0' && c <= '9')
                {
                    if (!digitsAllowed) return false;
                }
                else if (c < 'a' || c > 'z')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LooksLikeCode(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (var c in value!)
                if (c == ' ' || c == '.') return false;
            return value!.Length <= 64;
        }

        private static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 400: return "bad request";
                case 401: return "authentication required";
                case 402: return "payment or entitlement required";
                case 403: return "forbidden";
                case 404: return "not found";
                case 409: return "conflict";
                case 412: return "precondition failed";
                case 413: return "payload too large";
                case 422: return "validation failed";
                case 429: return "rate limited";
                case 500: return "server error";
                case 502: return "bad gateway";
                case 503: return "service unavailable";
                case 504: return "gateway timeout";
                case 507: return "insufficient storage";
                default: return "request failed";
            }
        }
    }
}
