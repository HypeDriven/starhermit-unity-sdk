using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// Base class for the typed API clients.
    /// </summary>
    /// <remarks>
    /// Subclasses describe endpoints - path, verb, query, body, and how to read the result - and
    /// nothing else. Credentials, retries, refresh, error mapping and redaction all belong to
    /// <see cref="StarhermitRestClient"/>, so no individual operation can quietly disagree with the
    /// others about how the API is called.
    /// </remarks>
    public abstract class StarhermitServiceClient
    {
        /// <summary>Creates the client.</summary>
        /// <param name="rest">The pipeline to send through.</param>
        protected StarhermitServiceClient(StarhermitRestClient rest)
        {
            Rest = rest ?? throw new ArgumentNullException(nameof(rest));
        }

        /// <summary>The pipeline this client sends through.</summary>
        protected StarhermitRestClient Rest { get; }

        /// <summary>Options the client was built with.</summary>
        protected StarhermitOptions Options => Rest.Options;

        /// <summary>Starts a GET request.</summary>
        /// <param name="path">Path relative to the API base.</param>
        /// <returns>The request.</returns>
        protected static StarhermitRequest Get(string path) => new StarhermitRequest("GET", path);

        /// <summary>Starts a POST request.</summary>
        /// <param name="path">Path relative to the API base.</param>
        /// <returns>The request.</returns>
        protected static StarhermitRequest Post(string path) => new StarhermitRequest("POST", path);

        /// <summary>Starts a PUT request.</summary>
        /// <param name="path">Path relative to the API base.</param>
        /// <returns>The request.</returns>
        protected static StarhermitRequest Put(string path) => new StarhermitRequest("PUT", path);

        /// <summary>Starts a PATCH request.</summary>
        /// <param name="path">Path relative to the API base.</param>
        /// <returns>The request.</returns>
        protected static StarhermitRequest Patch(string path) => new StarhermitRequest("PATCH", path);

        /// <summary>Starts a DELETE request.</summary>
        /// <param name="path">Path relative to the API base.</param>
        /// <returns>The request.</returns>
        protected static StarhermitRequest Delete(string path) => new StarhermitRequest("DELETE", path);

        /// <summary>Escapes a value for use as a path segment.</summary>
        /// <param name="value">Raw value.</param>
        /// <returns>The escaped segment.</returns>
        protected static string Escape(string value) => StarhermitRestClient.Segment(value);

        /// <summary>Formats a GUID for use as a path segment.</summary>
        /// <param name="value">The identifier.</param>
        /// <returns>The canonical GUID text.</returns>
        protected static string Escape(Guid value) => StarhermitRestClient.Segment(value);

        /// <summary>Serialises a JSON body and attaches it to a request.</summary>
        /// <param name="request">The request to extend.</param>
        /// <param name="writeMembers">Writes the body's members.</param>
        /// <returns>The same request, for chaining.</returns>
        protected static StarhermitRequest WithBody(StarhermitRequest request, Action<JsonWriter> writeMembers) =>
            request.WithJson(JsonWriter.SerializeObject(writeMembers));

        /// <summary>Sends a request and returns its parsed JSON body.</summary>
        /// <param name="request">The request to send.</param>
        /// <param name="operationId">SDK operation name.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The response body.</returns>
        protected async Task<JsonValue> SendJsonAsync(
            StarhermitRequest request,
            string operationId,
            CancellationToken cancellationToken)
        {
            using var response = await Rest.SendAsync(request, operationId, cancellationToken).ConfigureAwait(false);
            return response.Json;
        }

        /// <summary>Sends a request, reads its JSON body, and maps it to a model.</summary>
        /// <typeparam name="T">Model type.</typeparam>
        /// <param name="request">The request to send.</param>
        /// <param name="operationId">SDK operation name.</param>
        /// <param name="read">Maps the body to the model.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The mapped model.</returns>
        protected async Task<T> SendAsync<T>(
            StarhermitRequest request,
            string operationId,
            Func<JsonValue, T> read,
            CancellationToken cancellationToken)
        {
            var json = await SendJsonAsync(request, operationId, cancellationToken).ConfigureAwait(false);
            return read(json);
        }

        /// <summary>Sends a request that returns no body.</summary>
        /// <param name="request">The request to send.</param>
        /// <param name="operationId">SDK operation name.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes when the API has accepted the request.</returns>
        protected async Task SendAsync(
            StarhermitRequest request,
            string operationId,
            CancellationToken cancellationToken)
        {
            using var response = await Rest
                .SendAsync(request.Expecting(StarhermitResponseKind.None), operationId, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Sends a request and returns its body as bytes.</summary>
        /// <param name="request">The request to send.</param>
        /// <param name="operationId">SDK operation name.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The body bytes and its media type.</returns>
        protected async Task<StarhermitBinary> SendBytesAsync(
            StarhermitRequest request,
            string operationId,
            CancellationToken cancellationToken)
        {
            using var response = await Rest
                .SendAsync(request.Expecting(StarhermitResponseKind.Bytes), operationId, cancellationToken)
                .ConfigureAwait(false);
            return new StarhermitBinary(
                response.Body ?? Array.Empty<byte>(),
                response.Header("Content-Type") ?? "application/octet-stream");
        }

        /// <summary>Sends a request and hands back the live response for streaming.</summary>
        /// <param name="request">The request to send.</param>
        /// <param name="operationId">SDK operation name.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The response, which the caller must dispose.</returns>
        protected Task<StarhermitApiResponse> SendStreamAsync(
            StarhermitRequest request,
            string operationId,
            CancellationToken cancellationToken) =>
            Rest.SendAsync(request.Expecting(StarhermitResponseKind.Stream), operationId, cancellationToken);

        /// <summary>
        /// Pages through a list endpoint lazily, requesting the next page only when the caller asks
        /// for an item beyond the current one.
        /// </summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="fetchPage">Requests one page by 1-based page number.</param>
        /// <param name="cancellationToken">Cancels enumeration.</param>
        /// <returns>An asynchronous sequence over every matching item.</returns>
        protected static async IAsyncEnumerable<T> EnumeratePagesAsync<T>(
            Func<int, CancellationToken, Task<StarhermitPage<T>>> fetchPage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (fetchPage == null) throw new ArgumentNullException(nameof(fetchPage));

            var page = 1;
            while (true)
            {
                var result = await fetchPage(page, cancellationToken).ConfigureAwait(false);
                foreach (var item in result.Items) yield return item;

                // Stop on an empty page as well as on the server's own count: a deployment that
                // reports a stale total must not spin this loop forever.
                if (!result.HasMore || result.Items.Count == 0) yield break;
                page++;
            }
        }
    }

    /// <summary>Bytes returned by an API operation, with the media type the server labelled them.</summary>
    public readonly struct StarhermitBinary
    {
        /// <summary>Creates a binary payload.</summary>
        /// <param name="bytes">The payload.</param>
        /// <param name="contentType">Media type from the response.</param>
        public StarhermitBinary(byte[] bytes, string contentType)
        {
            Bytes = bytes ?? Array.Empty<byte>();
            ContentType = contentType ?? "application/octet-stream";
        }

        /// <summary>The payload bytes.</summary>
        public byte[] Bytes { get; }

        /// <summary>Media type the server reported.</summary>
        public string ContentType { get; }

        /// <summary>Number of bytes.</summary>
        public int Length => Bytes.Length;
    }
}
