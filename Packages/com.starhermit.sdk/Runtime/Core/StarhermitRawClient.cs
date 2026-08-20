using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// Direct access to the request pipeline for endpoints this SDK version does not type.
    /// </summary>
    /// <remarks>
    /// This is the forward-compatibility valve. A deployment that ships an endpoint before the SDK
    /// maps it is still reachable - with the same credentials, retries, refresh handling and redaction
    /// as every typed call - so nobody has to fork the package to call one new route.
    /// </remarks>
    public sealed class StarhermitRawClient : StarhermitServiceClient
    {
        internal StarhermitRawClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Sends a hand-built request through the full pipeline.</summary>
        /// <param name="request">The request to send.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The response, which the caller disposes when it holds a stream.</returns>
        public Task<StarhermitApiResponse> SendAsync(StarhermitRequest request, CancellationToken cancellationToken = default) =>
            Rest.SendAsync(request, "raw.send", cancellationToken);

        /// <summary>Sends a request and returns its parsed JSON body.</summary>
        /// <param name="request">The request to send.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The response body.</returns>
        public Task<JsonValue> SendForJsonAsync(StarhermitRequest request, CancellationToken cancellationToken = default) =>
            SendJsonAsync(request, "raw.send", cancellationToken);

        /// <summary>Starts a GET request against the API base address.</summary>
        /// <param name="path">Path relative to the API base.</param>
        /// <returns>The request.</returns>
        public static StarhermitRequest GetRequest(string path) => new StarhermitRequest("GET", path);

        /// <summary>Starts a request with any method.</summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path relative to the API base.</param>
        /// <returns>The request.</returns>
        public static StarhermitRequest Request(string method, string path) => new StarhermitRequest(method, path);
    }
}
