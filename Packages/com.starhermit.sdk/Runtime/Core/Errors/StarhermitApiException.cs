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
            if (code.Kind == JsonKind.String) ErrorCode = code.AsString();

            if (ServerMessage == null)
            {
                var detail = json["detail"];
                var title = json["title"];
                if (detail.Kind == JsonKind.String) ServerMessage = detail.AsString();
                else if (title.Kind == JsonKind.String) ServerMessage = title.AsString();
                else if (json["message"].Kind == JsonKind.String) ServerMessage = json["message"].AsString();
            }

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
                case 413: return "payload too large";
                case 422: return "validation failed";
                case 429: return "rate limited";
                case 500: return "server error";
                case 502: return "bad gateway";
                case 503: return "service unavailable";
                case 504: return "gateway timeout";
                default: return "request failed";
            }
        }
    }
}
