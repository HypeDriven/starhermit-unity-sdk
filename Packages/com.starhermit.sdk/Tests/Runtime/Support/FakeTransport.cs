using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit.Tests
{
    /// <summary>
    /// A transport that answers from a script instead of a network, and records what it was asked.
    /// </summary>
    /// <remarks>
    /// Every test in this suite runs the real pipeline - credentials, retries, refresh, error mapping,
    /// redaction - and swaps only the bottom-most layer. A test therefore exercises what ships.
    /// </remarks>
    public sealed class FakeTransport : IStarhermitTransport
    {
        private readonly Queue<Func<RecordedRequest, FakeResponse>> _scripted =
            new Queue<Func<RecordedRequest, FakeResponse>>();

        private Func<RecordedRequest, FakeResponse>? _fallback;
        private Func<RecordedRequest, Task<FakeResponse>>? _asyncFallback;
        private readonly object _gate = new object();

        /// <summary>Every request the pipeline sent, in order.</summary>
        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        /// <summary>The most recent request.</summary>
        public RecordedRequest Last => Requests[Requests.Count - 1];

        /// <summary>True once disposed.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Queues one scripted answer, consumed by the next request.</summary>
        /// <param name="respond">Builds the response from the request.</param>
        /// <returns>This transport, for chaining.</returns>
        public FakeTransport Enqueue(Func<RecordedRequest, FakeResponse> respond)
        {
            _scripted.Enqueue(respond);
            return this;
        }

        /// <summary>Queues a JSON response.</summary>
        /// <param name="status">HTTP status.</param>
        /// <param name="json">Body text.</param>
        /// <param name="headers">Optional response headers.</param>
        /// <returns>This transport, for chaining.</returns>
        public FakeTransport EnqueueJson(int status, string json, IDictionary<string, string>? headers = null) =>
            Enqueue(_ => new FakeResponse(status, json, headers));

        /// <summary>Answers every unscripted request the same way.</summary>
        /// <param name="respond">Builds the response from the request.</param>
        /// <returns>This transport, for chaining.</returns>
        public FakeTransport Always(Func<RecordedRequest, FakeResponse> respond)
        {
            _fallback = respond;
            return this;
        }

        /// <summary>
        /// Answers every unscripted request asynchronously, so a test can hold a request open without
        /// blocking a thread pool thread.
        /// </summary>
        /// <param name="respond">Builds the response from the request.</param>
        /// <returns>This transport, for chaining.</returns>
        public FakeTransport AlwaysAsync(Func<RecordedRequest, Task<FakeResponse>> respond)
        {
            _asyncFallback = respond;
            return this;
        }

        /// <inheritdoc />
        public async Task<StarhermitTransportResponse> SendAsync(
            StarhermitTransportRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var recorded = RecordedRequest.From(request);
            Func<RecordedRequest, FakeResponse>? responder;
            Func<RecordedRequest, Task<FakeResponse>>? asyncResponder;
            lock (_gate)
            {
                // Requests arrive from several threads in the concurrency tests; recording them has to
                // be safe or the test fails for reasons that have nothing to do with the SDK.
                Requests.Add(recorded);
                responder = _scripted.Count > 0 ? _scripted.Dequeue() : _fallback;
                asyncResponder = responder == null ? _asyncFallback : null;
            }

            if (responder == null && asyncResponder == null)
                throw new InvalidOperationException($"No scripted response for {recorded.Method} {recorded.Uri}.");

            var response = responder != null
                ? responder(recorded)
                : await asyncResponder!(recorded).ConfigureAwait(false);

            if (response.Throw != null) throw response.Throw;

            var body = response.Body == null ? null : Encoding.UTF8.GetBytes(response.Body);

            // Streaming callers - downloads - get a live stream, exactly as a real transport gives them.
            if (!request.BufferResponse)
            {
                return new StarhermitTransportResponse(
                    response.Status,
                    response.Headers,
                    new System.IO.MemoryStream(body ?? new byte[0], writable: false));
            }

            return new StarhermitTransportResponse(response.Status, response.Headers, body);
        }

        /// <inheritdoc />
        public void Dispose() => IsDisposed = true;
    }

    /// <summary>One request as the fake transport saw it.</summary>
    public sealed class RecordedRequest
    {
        private RecordedRequest(string method, Uri uri, IReadOnlyDictionary<string, string> headers, string? body, string? contentType)
        {
            Method = method;
            Uri = uri;
            Headers = headers;
            Body = body;
            ContentType = contentType;
        }

        /// <summary>HTTP method.</summary>
        public string Method { get; }

        /// <summary>Absolute address the pipeline built.</summary>
        public Uri Uri { get; }

        /// <summary>Path without the query string.</summary>
        public string Path => Uri.AbsolutePath;

        /// <summary>Query string, including the leading '?' when present.</summary>
        public string Query => Uri.Query;

        /// <summary>Headers, compared case-insensitively.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Request body decoded as UTF-8, or null when there was none.</summary>
        public string? Body { get; }

        /// <summary>Media type of the request body, or null when there was no body.</summary>
        public string? ContentType { get; }

        /// <summary>Reads one header, or null when absent.</summary>
        /// <param name="name">Header name.</param>
        /// <returns>The value or null.</returns>
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

        /// <summary>The bearer token on this request, without the scheme.</summary>
        public string? BearerToken
        {
            get
            {
                var authorization = Header("Authorization");
                return authorization != null && authorization.StartsWith("Bearer ", StringComparison.Ordinal)
                    ? authorization.Substring("Bearer ".Length)
                    : null;
            }
        }

        internal static RecordedRequest From(StarhermitTransportRequest request)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers) headers[header.Key] = header.Value;

            string? body = null;
            if (request.Content != null) body = Encoding.UTF8.GetString(request.Content.ReadBytes());

            return new RecordedRequest(request.Method, request.Uri, headers, body, request.Content?.ContentType);
        }
    }

    /// <summary>A scripted response.</summary>
    public sealed class FakeResponse
    {
        /// <summary>Creates a response.</summary>
        /// <param name="status">HTTP status.</param>
        /// <param name="body">Body text.</param>
        /// <param name="headers">Response headers.</param>
        public FakeResponse(int status, string? body = null, IDictionary<string, string>? headers = null)
        {
            Status = status;
            Body = body;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers != null)
                foreach (var header in headers)
                    map[header.Key] = header.Value;
            Headers = map;
        }

        private FakeResponse(Exception failure)
        {
            Status = 0;
            Throw = failure;
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>HTTP status.</summary>
        public int Status { get; }

        /// <summary>Body text.</summary>
        public string? Body { get; }

        /// <summary>Response headers.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Exception to throw instead of answering.</summary>
        public Exception? Throw { get; }

        /// <summary>Creates a response that fails at the transport, as an offline device would.</summary>
        /// <param name="message">Failure description.</param>
        /// <returns>The failing response.</returns>
        public static FakeResponse TransportFailure(string message = "The network is unreachable.") =>
            new FakeResponse(new StarhermitTransportException(message));

        /// <summary>Creates a response that times out.</summary>
        /// <returns>The failing response.</returns>
        public static FakeResponse Timeout() =>
            new FakeResponse(new StarhermitTimeoutException("The request timed out.", TimeSpan.FromSeconds(1)));
    }
}
