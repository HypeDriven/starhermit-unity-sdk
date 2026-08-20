using System;
using System.Collections.Generic;
using System.Globalization;

namespace Starhermit
{
    /// <summary>Which credential a request must carry.</summary>
    public enum StarhermitCredential
    {
        /// <summary>No authorization header at all - public routes and the auth handshake itself.</summary>
        None = 0,

        /// <summary>The player's account session.</summary>
        Account = 1,

        /// <summary>A game-scoped launch token, which the backend fences to that game's routes.</summary>
        Launch = 2,

        /// <summary>A dedicated-server token exchanged from a deployment invoke key.</summary>
        Server = 3,

        /// <summary>Send the account session when there is one; proceed anonymously otherwise.</summary>
        AccountOptional = 4
    }

    /// <summary>How the SDK should handle the response body.</summary>
    public enum StarhermitResponseKind
    {
        /// <summary>Parse the body as JSON.</summary>
        Json = 0,

        /// <summary>Buffer the body as bytes - a small image or archive.</summary>
        Bytes = 1,

        /// <summary>Leave the body on the wire so the caller can stream it.</summary>
        Stream = 2,

        /// <summary>Discard the body.</summary>
        None = 3
    }

    /// <summary>
    /// A request to the Starhermit API, expressed against the versioned base address.
    /// </summary>
    /// <remarks>
    /// This is also the public escape hatch: an endpoint that shipped after this SDK version can be
    /// called through <c>client.Raw</c> with a hand-built request instead of forking the package.
    /// </remarks>
    public sealed class StarhermitRequest
    {
        private List<KeyValuePair<string, string>>? _query;
        private Dictionary<string, string>? _headers;

        /// <summary>Creates a request.</summary>
        /// <param name="method">HTTP method, uppercase.</param>
        /// <param name="path">Path relative to the API base address, without a leading slash.</param>
        public StarhermitRequest(string method, string path)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            IsIdempotent = method == "GET" || method == "HEAD" || method == "PUT" || method == "DELETE";
        }

        /// <summary>HTTP method.</summary>
        public string Method { get; }

        /// <summary>Path relative to the API base address.</summary>
        public string Path { get; }

        /// <summary>Query-string parameters, in the order they were added.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Query =>
            _query ?? (IReadOnlyList<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>();

        /// <summary>Extra request headers.</summary>
        public IReadOnlyDictionary<string, string> Headers =>
            _headers ?? (IReadOnlyDictionary<string, string>)EmptyHeaders;

        private static readonly Dictionary<string, string> EmptyHeaders = new Dictionary<string, string>(0);

        /// <summary>Request body, when there is one.</summary>
        public StarhermitContent? Content { get; set; }

        /// <summary>Credential this request requires. Defaults to the account session.</summary>
        public StarhermitCredential Credential { get; set; } = StarhermitCredential.Account;

        /// <summary>How the response body should be handled.</summary>
        public StarhermitResponseKind Expect { get; set; } = StarhermitResponseKind.Json;

        /// <summary>
        /// Whether repeating this request is safe. GET, HEAD, PUT and DELETE start true; a POST must
        /// opt in, and only when the endpoint documents an idempotency guarantee or an idempotency key
        /// is supplied.
        /// </summary>
        public bool IsIdempotent { get; set; }

        /// <summary>Optional idempotency key that makes a POST safe to repeat.</summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>Per-request timeout override.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>Reports upload and download progress for large transfers.</summary>
        public IProgress<StarhermitTransferProgress>? Progress { get; set; }

        /// <summary>Adds a query parameter. Null values are skipped, which keeps call sites terse.</summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value; null omits the parameter.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithQuery(string name, string? value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (value == null) return this;
            (_query ??= new List<KeyValuePair<string, string>>(4))
                .Add(new KeyValuePair<string, string>(name, value));
            return this;
        }

        /// <summary>Adds an integer query parameter.</summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value; null omits the parameter.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithQuery(string name, int? value) =>
            WithQuery(name, value?.ToString(CultureInfo.InvariantCulture));

        /// <summary>Adds a boolean query parameter, lowercase as the API expects.</summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value; null omits the parameter.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithQuery(string name, bool? value) =>
            WithQuery(name, value.HasValue ? (value.Value ? "true" : "false") : null);

        /// <summary>Adds a GUID query parameter.</summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value; null omits the parameter.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithQuery(string name, Guid? value) =>
            WithQuery(name, value?.ToString("D", CultureInfo.InvariantCulture));

        /// <summary>Adds a timestamp query parameter in ISO-8601 UTC.</summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value; null omits the parameter.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithQuery(string name, DateTimeOffset? value) =>
            WithQuery(name, value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        /// <summary>Sets a request header.</summary>
        /// <param name="name">Header name.</param>
        /// <param name="value">Header value; null removes it.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithHeader(string name, string? value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            _headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (value == null) _headers.Remove(name);
            else _headers[name] = value;
            return this;
        }

        /// <summary>Sets the request body.</summary>
        /// <param name="content">The body to send.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithContent(StarhermitContent? content)
        {
            Content = content;
            return this;
        }

        /// <summary>Sets a JSON body from already-serialised text.</summary>
        /// <param name="json">JSON text.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithJson(string json) => WithContent(StarhermitContent.Json(json));

        /// <summary>Sets the credential this request needs.</summary>
        /// <param name="credential">Credential kind.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest WithCredential(StarhermitCredential credential)
        {
            Credential = credential;
            return this;
        }

        /// <summary>Sets how the response body is handled.</summary>
        /// <param name="kind">Response handling mode.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest Expecting(StarhermitResponseKind kind)
        {
            Expect = kind;
            return this;
        }

        /// <summary>
        /// Marks a POST as safe to repeat. Supplying a key also sends it as <c>Idempotency-Key</c>.
        /// </summary>
        /// <param name="key">Optional idempotency key.</param>
        /// <returns>This request, for chaining.</returns>
        public StarhermitRequest AsIdempotent(string? key = null)
        {
            IsIdempotent = true;
            IdempotencyKey = key;
            if (key != null) WithHeader("Idempotency-Key", key);
            return this;
        }
    }

    /// <summary>Progress of a streaming upload or download.</summary>
    public readonly struct StarhermitTransferProgress
    {
        /// <summary>Creates a progress report.</summary>
        /// <param name="bytesTransferred">Bytes moved so far.</param>
        /// <param name="totalBytes">Total bytes when the length is known.</param>
        /// <param name="isUpload">True for an upload, false for a download.</param>
        public StarhermitTransferProgress(long bytesTransferred, long? totalBytes, bool isUpload)
        {
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            IsUpload = isUpload;
        }

        /// <summary>Bytes transferred so far.</summary>
        public long BytesTransferred { get; }

        /// <summary>Total bytes, when the length is known in advance.</summary>
        public long? TotalBytes { get; }

        /// <summary>True when this describes an upload.</summary>
        public bool IsUpload { get; }

        /// <summary>Completion between 0 and 1, or null when the total is unknown.</summary>
        public double? Fraction =>
            TotalBytes.HasValue && TotalBytes.Value > 0
                ? Math.Min(1d, BytesTransferred / (double)TotalBytes.Value)
                : (double?)null;
    }
}
