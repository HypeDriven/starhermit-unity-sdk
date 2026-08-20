using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// The HTTP primitive the SDK is built on.
    /// </summary>
    /// <remarks>
    /// Everything above this interface - retries, refresh coordination, redaction, error mapping - is
    /// platform-neutral. Swapping the implementation is how the package supports a console with its
    /// own certified networking stack, or how a test drives the whole SDK with no sockets at all.
    /// </remarks>
    public interface IStarhermitTransport : IDisposable
    {
        /// <summary>Sends one request and returns its response.</summary>
        /// <param name="request">The fully resolved request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The response, whose body the caller disposes.</returns>
        /// <exception cref="StarhermitTransportException">No response could be obtained.</exception>
        Task<StarhermitTransportResponse> SendAsync(StarhermitTransportRequest request, CancellationToken cancellationToken);
    }

    /// <summary>A request with everything resolved: absolute address, headers and body.</summary>
    public sealed class StarhermitTransportRequest
    {
        /// <summary>Creates a transport request.</summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="uri">Absolute request address.</param>
        public StarhermitTransportRequest(string method, Uri uri)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        }

        /// <summary>HTTP method.</summary>
        public string Method { get; }

        /// <summary>Absolute request address.</summary>
        public Uri Uri { get; }

        /// <summary>Headers to send.</summary>
        public List<KeyValuePair<string, string>> Headers { get; } = new List<KeyValuePair<string, string>>(8);

        /// <summary>Request body, when there is one.</summary>
        public StarhermitContent? Content { get; set; }

        /// <summary>
        /// True when the response body should be buffered into memory. False asks the transport to
        /// hand back a live stream so a multi-gigabyte download never sits in the heap.
        /// </summary>
        public bool BufferResponse { get; set; } = true;

        /// <summary>Total time budget for this attempt.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Optional progress reporting for large transfers.</summary>
        public IProgress<StarhermitTransferProgress>? Progress { get; set; }
    }

    /// <summary>A response from the transport.</summary>
    public sealed class StarhermitTransportResponse : IDisposable
    {
        private readonly IDisposable? _bodyOwner;
        private bool _disposed;

        /// <summary>Creates a buffered response.</summary>
        /// <param name="status">HTTP status code.</param>
        /// <param name="headers">Response headers.</param>
        /// <param name="body">Body bytes, or null when there was no body.</param>
        public StarhermitTransportResponse(int status, IReadOnlyDictionary<string, string> headers, byte[]? body)
        {
            Status = status;
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
            Body = body;
        }

        /// <summary>Creates a streaming response.</summary>
        /// <param name="status">HTTP status code.</param>
        /// <param name="headers">Response headers.</param>
        /// <param name="bodyStream">Live body stream.</param>
        /// <param name="bodyOwner">Object whose disposal releases the underlying connection.</param>
        public StarhermitTransportResponse(
            int status,
            IReadOnlyDictionary<string, string> headers,
            Stream bodyStream,
            IDisposable? bodyOwner = null)
        {
            Status = status;
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
            BodyStream = bodyStream ?? throw new ArgumentNullException(nameof(bodyStream));
            _bodyOwner = bodyOwner;
        }

        /// <summary>HTTP status code.</summary>
        public int Status { get; }

        /// <summary>Response headers, compared case-insensitively.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Buffered body, when the request asked for buffering.</summary>
        public byte[]? Body { get; }

        /// <summary>Live body stream, when the request asked to stream.</summary>
        public Stream? BodyStream { get; }

        /// <summary>True for 2xx.</summary>
        public bool IsSuccess => Status >= 200 && Status < 300;

        /// <summary>Reads a response header, returning null when absent.</summary>
        /// <param name="name">Header name.</param>
        /// <returns>The header value or null.</returns>
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BodyStream?.Dispose();
            _bodyOwner?.Dispose();
        }
    }
}
