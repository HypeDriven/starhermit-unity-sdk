#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Starhermit.Platform
{
    /// <summary>
    /// The default transport inside Unity, built on <see cref="UnityWebRequest"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the transport the platform supports everywhere Unity runs, including WebGL - where a
    /// managed <c>HttpClient</c> has no sockets to use and the browser owns the request.
    /// </para>
    /// <para>
    /// <c>UnityWebRequest</c> must be created and driven on the main thread, so the whole request is
    /// marshalled there and the awaiting caller is resumed from the completion callback.
    /// </para>
    /// </remarks>
    public sealed class UnityWebRequestTransport : IStarhermitTransport
    {
        private readonly int _maxBufferedResponseBytes;
        private bool _disposed;

        /// <summary>Creates the transport.</summary>
        /// <param name="maxBufferedResponseBytes">
        /// Largest response the transport will buffer in memory. Larger downloads must be streamed by
        /// a caller that writes them to a file as they arrive.
        /// </param>
        public UnityWebRequestTransport(int maxBufferedResponseBytes = 64 * 1024 * 1024)
        {
            _maxBufferedResponseBytes = maxBufferedResponseBytes;
        }

        /// <inheritdoc />
        public async Task<StarhermitTransportResponse> SendAsync(
            StarhermitTransportRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_disposed) throw new ObjectDisposedException(nameof(UnityWebRequestTransport));

            cancellationToken.ThrowIfCancellationRequested();

            using var webRequest = new UnityWebRequest(request.Uri, request.Method);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.timeout = (int)Math.Ceiling(request.Timeout.TotalSeconds);

            if (request.Content != null)
            {
                var bytes = request.Content.ReadBytes();
                webRequest.uploadHandler = new UploadHandlerRaw(bytes) { contentType = request.Content.ContentType };
            }

            foreach (var header in request.Headers)
            {
                // Content-Length and Content-Type are owned by the upload handler; setting them here
                // is rejected by some platforms and ignored by others.
                if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                if (request.Content != null && string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                webRequest.SetRequestHeader(header.Key, header.Value);
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = webRequest.SendWebRequest();
            operation.completed += _ => completion.TrySetResult(true);

            using (cancellationToken.Register(() =>
                   {
                       // Aborting is how a UnityWebRequest is cancelled; the completion callback still
                       // runs, and the result reads as an abort.
                       try
                       {
                           webRequest.Abort();
                       }
                       catch (InvalidOperationException)
                       {
                       }
                   }))
            {
                await completion.Task.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (webRequest.result == UnityWebRequest.Result.ConnectionError)
            {
                throw new StarhermitTransportException(
                    $"The request to {request.Uri.AbsolutePath} could not be completed: {webRequest.error}");
            }

            if (webRequest.result == UnityWebRequest.Result.DataProcessingError)
            {
                throw new StarhermitTransportException(
                    $"The response from {request.Uri.AbsolutePath} could not be read: {webRequest.error}");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var responseHeaders = webRequest.GetResponseHeaders();
            if (responseHeaders != null)
                foreach (var header in responseHeaders)
                    headers[header.Key] = header.Value;

            var body = webRequest.downloadHandler?.data;
            if (body != null && body.Length > _maxBufferedResponseBytes)
            {
                throw new StarhermitProtocolException(
                    $"The response body is {body.Length} bytes, beyond this transport's {_maxBufferedResponseBytes}-byte buffer.");
            }

            var status = (int)webRequest.responseCode;

            if (!request.BufferResponse)
            {
                // UnityWebRequest has already buffered the body; hand it back as a stream so callers
                // that stream to disk work identically on every platform.
                Stream stream = new MemoryStream(body ?? Array.Empty<byte>(), writable: false);
                if (request.Progress != null)
                    stream = new ProgressStream(stream, request.Progress, body?.Length, isUpload: false);
                return new StarhermitTransportResponse(status, headers, stream);
            }

            return new StarhermitTransportResponse(status, headers, body);
        }

        /// <inheritdoc />
        public void Dispose() => _disposed = true;
    }
}
#endif
