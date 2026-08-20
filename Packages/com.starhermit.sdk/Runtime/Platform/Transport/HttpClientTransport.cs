using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit.Platform
{
    /// <summary>
    /// Transport built on <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// This is the default outside Unity - headless servers, tools and the package's own test suite -
    /// and the fallback inside Unity on platforms where <c>UnityWebRequest</c> is not the better fit.
    /// It streams both directions, so an upload or download never has to be materialised in memory.
    /// </remarks>
    public sealed class HttpClientTransport : IStarhermitTransport
    {
        private readonly HttpClient _client;
        private readonly bool _ownsClient;
        private bool _disposed;

        /// <summary>Creates a transport with its own <see cref="HttpClient"/>.</summary>
        public HttpClientTransport()
            : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }), ownsClient: true)
        {
        }

        /// <summary>Creates a transport over an existing client.</summary>
        /// <param name="client">The client to send through.</param>
        /// <param name="ownsClient">True when disposing this transport should dispose the client.</param>
        public HttpClientTransport(HttpClient client, bool ownsClient = false)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            // Per-attempt timeouts are enforced by the caller's cancellation token, which is what lets
            // one client serve requests with very different budgets.
            _client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            _ownsClient = ownsClient;
        }

        /// <inheritdoc />
        public async Task<StarhermitTransportResponse> SendAsync(
            StarhermitTransportRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_disposed) throw new ObjectDisposedException(nameof(HttpClientTransport));

            using var timeoutSource = new CancellationTokenSource(request.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Uri);
            if (request.Content != null)
            {
                var content = request.Content;
                Stream body = content.OpenStream();
                if (request.Progress != null)
                    body = new ProgressStream(body, request.Progress, content.Length, isUpload: true);

                var httpContent = new StreamContent(body);
                httpContent.Headers.ContentType = ParseContentType(content.ContentType);
                if (content.Length.HasValue) httpContent.Headers.ContentLength = content.Length;
                message.Content = httpContent;
            }

            foreach (var header in request.Headers)
            {
                if (message.Headers.TryAddWithoutValidation(header.Key, header.Value)) continue;
                message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            HttpResponseMessage response;
            try
            {
                response = await _client
                    .SendAsync(
                        message,
                        request.BufferResponse ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                        linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new StarhermitTimeoutException(
                    $"The request to {request.Uri.AbsolutePath} timed out after {request.Timeout.TotalSeconds:0.##}s.",
                    request.Timeout);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                throw new StarhermitTransportException(
                    $"The request to {request.Uri.AbsolutePath} could not be completed: {exception.Message}",
                    exception);
            }
            catch (IOException exception)
            {
                throw new StarhermitTransportException(
                    $"The connection to {request.Uri.Host} failed: {exception.Message}",
                    exception);
            }

            var headers = CollectHeaders(response);

            try
            {
                if (request.BufferResponse)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    response.Dispose();
                    return new StarhermitTransportResponse(status, headers, bytes);
                }

                var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                if (request.Progress != null)
                    stream = new ProgressStream(stream, request.Progress, response.Content.Headers.ContentLength, isUpload: false);
                return new StarhermitTransportResponse((int)response.StatusCode, headers, stream, response);
            }
            catch (Exception exception) when (exception is IOException || exception is HttpRequestException)
            {
                response.Dispose();
                throw new StarhermitTransportException(
                    $"The response body from {request.Uri.AbsolutePath} could not be read: {exception.Message}",
                    exception);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsClient) _client.Dispose();
        }

        private static MediaTypeHeaderValue? ParseContentType(string contentType)
        {
            try
            {
                return MediaTypeHeaderValue.Parse(contentType);
            }
            catch (FormatException)
            {
                return new MediaTypeHeaderValue("application/octet-stream");
            }
        }

        private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers) headers[header.Key] = string.Join(", ", header.Value);
            foreach (var header in response.Content.Headers) headers[header.Key] = string.Join(", ", header.Value);
            return headers;
        }
    }
}
