using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// The REST pipeline every typed client sends through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One place decides the things that must be decided identically for all 190-odd operations:
    /// which credential goes on the request, when a <c>401</c> earns a refresh and a replay, which
    /// failures are worth repeating, what an error looks like once typed, and what may be logged.
    /// Individual clients describe endpoints; they never make those decisions themselves.
    /// </para>
    /// <para>
    /// Nothing here is Unity-specific. The transport underneath it is.
    /// </para>
    /// </remarks>
    public sealed class StarhermitRestClient : IDisposable
    {
        private static readonly string[] RequestIdHeaders =
        {
            "X-Request-Id", "x-request-id", "X-Correlation-Id", "Request-Id", "X-Trace-Id"
        };

        private readonly StarhermitOptions _options;
        private readonly IStarhermitTransport _transport;
        private readonly bool _ownsTransport;
        private readonly StarhermitSessionManager _sessions;
        private readonly StarhermitScopedCredentials _scoped;
        private readonly LevelFilteredLogger _log;
        private readonly IStarhermitTelemetrySink? _telemetry;
        private readonly IStarhermitClock _clock;
        private readonly string _userAgent;

        private int _inFlight;
        private int _retriesSpent;
        private string? _lastError;
        private bool _disposed;

        /// <summary>Creates the pipeline.</summary>
        /// <param name="options">Client options.</param>
        /// <param name="transport">Transport to send through.</param>
        /// <param name="ownsTransport">True when disposing this client should dispose the transport.</param>
        /// <param name="sessions">Session manager supplying account credentials.</param>
        /// <param name="scoped">Store holding launch and server credentials.</param>
        public StarhermitRestClient(
            StarhermitOptions options,
            IStarhermitTransport transport,
            bool ownsTransport,
            StarhermitSessionManager sessions,
            StarhermitScopedCredentials scoped)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _ownsTransport = ownsTransport;
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _scoped = scoped ?? throw new ArgumentNullException(nameof(scoped));
            _log = new LevelFilteredLogger(options.Logger, options.LogLevel);
            _telemetry = options.Telemetry;
            _clock = options.Clock;
            _userAgent = StarhermitSdk.UserAgent(options.UserAgentSuffix);
        }

        /// <summary>The options this pipeline was built with.</summary>
        public StarhermitOptions Options => _options;

        /// <summary>Requests currently in flight.</summary>
        public int InFlightRequests => Volatile.Read(ref _inFlight);

        /// <summary>Retries spent since the client was created.</summary>
        public int RetriesSpent => Volatile.Read(ref _retriesSpent);

        /// <summary>The last failure, already redacted and safe to display.</summary>
        public string? LastError => Volatile.Read(ref _lastError);

        /// <summary>Sends a request through the full pipeline.</summary>
        /// <param name="request">The request to send.</param>
        /// <param name="operationId">SDK operation name, used for telemetry and logs.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The successful response, which the caller disposes when it holds a stream.</returns>
        /// <exception cref="StarhermitApiException">The API refused the request.</exception>
        /// <exception cref="StarhermitTransportException">No response could be obtained.</exception>
        public async Task<StarhermitApiResponse> SendAsync(
            StarhermitRequest request,
            string operationId,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_disposed) throw new ObjectDisposedException(nameof(StarhermitRestClient));

            var uri = BuildUri(request);
            var safeUri = StarhermitRedactor.RedactUri(uri);
            var started = Stopwatch.StartNew();
            var attempt = 0;
            var retries = 0;
            var refreshed = false;
            var replayable = request.Content == null || request.Content.CanReplay;

            Interlocked.Increment(ref _inFlight);
            try
            {
                while (true)
                {
                    attempt++;
                    cancellationToken.ThrowIfCancellationRequested();

                    var transportRequest = new StarhermitTransportRequest(request.Method, uri)
                    {
                        Content = request.Content,
                        BufferResponse = request.Expect != StarhermitResponseKind.Stream,
                        Timeout = request.Timeout ?? _options.RequestTimeout,
                        Progress = request.Progress
                    };

                    await ApplyHeadersAsync(request, transportRequest, cancellationToken).ConfigureAwait(false);

                    StarhermitTransportResponse response;
                    try
                    {
                        response = await _transport.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        RecordTelemetry(operationId, started.Elapsed, 0, retries, null, StarhermitOperationOutcome.Cancelled);
                        throw;
                    }
                    catch (StarhermitTransportException exception)
                    {
                        var outcome = new StarhermitAttemptOutcome(
                            0,
                            transportFailed: true,
                            timedOut: exception is StarhermitTimeoutException,
                            isReplayable: replayable && request.IsIdempotent,
                            retryAfter: null);

                        if (_options.RetryPolicy.ShouldRetry(attempt, outcome, out var backoff))
                        {
                            retries++;
                            Interlocked.Increment(ref _retriesSpent);
                            _log.Log(StarhermitLogLevel.Warning, $"{request.Method} {safeUri} failed ({exception.GetType().Name}); retrying in {backoff.TotalMilliseconds:0}ms.");
                            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        Volatile.Write(ref _lastError, $"{operationId}: {exception.Message}");
                        RecordTelemetry(operationId, started.Elapsed, 0, retries, null, StarhermitOperationOutcome.TransportError);
                        throw;
                    }

                    var requestId = ReadRequestId(response);

                    if (response.IsSuccess)
                    {
                        _log.Log(StarhermitLogLevel.Debug, $"{request.Method} {safeUri} -> {response.Status}");
                        RecordTelemetry(operationId, started.Elapsed, response.Status / 100, retries, requestId, StarhermitOperationOutcome.Success);
                        return ToApiResponse(request, response, requestId);
                    }

                    // A 401 buys exactly one coordinated refresh and one replay. A second would mean
                    // the refreshed token is being rejected too, and hammering the endpoint with it
                    // only burns the refresh-token family.
                    if (response.Status == 401 && !refreshed && replayable && UsesAccountSession(request.Credential))
                    {
                        // Keep the server's own account of the refusal before disposing it: if the
                        // refresh then fails, the caller should see what the API actually said rather
                        // than a message the SDK made up.
                        var unauthorized = new StarhermitErrorInfo
                        {
                            Status = 401,
                            Method = request.Method,
                            Path = safeUri,
                            RequestId = requestId,
                            Headers = RedactHeaders(response.Headers)
                        };
                        unauthorized.ReadBody(StarhermitRedactor.RedactBody(
                            response.Body == null ? null : Encoding.UTF8.GetString(response.Body),
                            _options.MaxDiagnosticBodyCharacters));
                        response.Dispose();

                        refreshed = true;
                        var spent = _sessions.Current?.AccessToken;
                        if (await _sessions.TryRefreshAsync(spent, cancellationToken).ConfigureAwait(false))
                        {
                            _log.Log(StarhermitLogLevel.Info, $"{request.Method} {safeUri} -> 401; session refreshed, replaying once.");
                            continue;
                        }

                        _log.Log(StarhermitLogLevel.Warning, $"{request.Method} {safeUri} -> 401 and the session could not be refreshed.");
                        if (unauthorized.ServerMessage == null)
                            unauthorized.ServerMessage = "The session is no longer valid.";
                        var unauthorizedException = StarhermitApiException.Create(unauthorized);
                        Volatile.Write(ref _lastError, $"{operationId}: {unauthorizedException.Message}");
                        RecordTelemetry(operationId, started.Elapsed, 4, retries, requestId, StarhermitOperationOutcome.ApiError);
                        throw unauthorizedException;
                    }

                    var body = response.Body == null ? null : Encoding.UTF8.GetString(response.Body);
                    var retryAfter = StarhermitRetryPolicy.ParseRetryAfter(response.Header("Retry-After"), _clock.UtcNow);
                    var status = response.Status;
                    var headers = response.Headers;
                    response.Dispose();

                    var failureOutcome = new StarhermitAttemptOutcome(
                        status,
                        transportFailed: false,
                        timedOut: false,
                        isReplayable: replayable && request.IsIdempotent,
                        retryAfter: retryAfter);

                    if (_options.RetryPolicy.ShouldRetry(attempt, failureOutcome, out var delay))
                    {
                        retries++;
                        Interlocked.Increment(ref _retriesSpent);
                        _log.Log(StarhermitLogLevel.Warning, $"{request.Method} {safeUri} -> {status}; retrying in {delay.TotalMilliseconds:0}ms.");
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var error = new StarhermitErrorInfo
                    {
                        Status = status,
                        Method = request.Method,
                        Path = safeUri,
                        RequestId = requestId,
                        RetryAfter = retryAfter,
                        Headers = RedactHeaders(headers)
                    };
                    error.ReadBody(StarhermitRedactor.RedactBody(body, _options.MaxDiagnosticBodyCharacters));

                    var exceptionToThrow = StarhermitApiException.Create(error);
                    Volatile.Write(ref _lastError, $"{operationId}: {exceptionToThrow.Message}");
                    _log.Log(StarhermitLogLevel.Error, exceptionToThrow.Message);
                    RecordTelemetry(operationId, started.Elapsed, status / 100, retries, requestId, StarhermitOperationOutcome.ApiError);
                    throw exceptionToThrow;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        /// <summary>
        /// Fetches an absolute URL that authenticates itself - a signed asset or avatar origin - and
        /// streams the response.
        /// </summary>
        /// <remarks>
        /// Deliberately sends no <c>Authorization</c> header. A signed URL already carries its own
        /// credential, and forwarding the player's bearer token to whatever host the platform happens
        /// to have signed for would hand that host a session it has no business holding.
        /// </remarks>
        /// <param name="uri">Absolute address to fetch.</param>
        /// <param name="progress">Optional download progress.</param>
        /// <param name="timeout">Time budget; defaults to the client's request timeout.</param>
        /// <param name="rangeStart">
        /// Byte offset to resume from. The origin decides whether to honour it: a <c>206</c> continues
        /// the transfer, and a <c>200</c> means it is sending the whole file again.
        /// </param>
        /// <param name="cancellationToken">Cancels the download.</param>
        /// <returns>The streaming response, which the caller disposes.</returns>
        public async Task<StarhermitApiResponse> FetchSignedAsync(
            Uri uri,
            IProgress<StarhermitTransferProgress>? progress = null,
            TimeSpan? timeout = null,
            long rangeStart = 0,
            CancellationToken cancellationToken = default)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (rangeStart < 0) throw new ArgumentOutOfRangeException(nameof(rangeStart));
            if (_disposed) throw new ObjectDisposedException(nameof(StarhermitRestClient));

            var transportRequest = new StarhermitTransportRequest("GET", uri)
            {
                BufferResponse = false,
                Timeout = timeout ?? _options.RequestTimeout,
                Progress = progress
            };
            transportRequest.Headers.Add(new KeyValuePair<string, string>("User-Agent", _userAgent));
            if (rangeStart > 0)
            {
                transportRequest.Headers.Add(new KeyValuePair<string, string>(
                    "Range",
                    "bytes=" + rangeStart.ToString(CultureInfo.InvariantCulture) + "-"));
            }

            var response = await _transport.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccess)
            {
                return new StarhermitApiResponse(
                    response.Status,
                    response.Headers,
                    null,
                    response.BodyStream ?? System.IO.Stream.Null,
                    ReadRequestId(response),
                    response);
            }

            var status = response.Status;
            response.Dispose();
            var error = new StarhermitErrorInfo
            {
                Status = status,
                Method = "GET",
                Path = StarhermitRedactor.RedactUri(uri),
                ServerMessage = "The signed download URL was refused. It may have expired.",
                Headers = new Dictionary<string, string>(0)
            };
            throw StarhermitApiException.Create(error);
        }

        /// <summary>
        /// Uploads to an absolute URL that authenticates itself - a signed storage target.
        /// </summary>
        /// <remarks>
        /// Like <see cref="FetchSignedAsync"/>, this sends no <c>Authorization</c> header: the URL
        /// already carries its own credential, and the storage host has no business holding a player's
        /// session. The body streams, so a multi-gigabyte asset never sits in the heap.
        /// </remarks>
        /// <param name="uri">Absolute address to upload to.</param>
        /// <param name="content">The body to send.</param>
        /// <param name="method">HTTP method the signed target expects. Defaults to PUT.</param>
        /// <param name="progress">Optional upload progress.</param>
        /// <param name="timeout">Time budget; defaults to the client's request timeout.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>A task that completes once the target accepts the body.</returns>
        public async Task UploadSignedAsync(
            Uri uri,
            StarhermitContent content,
            string method = "PUT",
            IProgress<StarhermitTransferProgress>? progress = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (_disposed) throw new ObjectDisposedException(nameof(StarhermitRestClient));

            var transportRequest = new StarhermitTransportRequest(method, uri)
            {
                Content = content,
                BufferResponse = true,
                Timeout = timeout ?? _options.RequestTimeout,
                Progress = progress
            };
            transportRequest.Headers.Add(new KeyValuePair<string, string>("User-Agent", _userAgent));

            using var response = await _transport.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccess) return;

            var error = new StarhermitErrorInfo
            {
                Status = response.Status,
                Method = method,
                Path = StarhermitRedactor.RedactUri(uri),
                ServerMessage = "The signed upload target refused the body. It may have expired.",
                Headers = RedactHeaders(response.Headers)
            };
            error.ReadBody(StarhermitRedactor.RedactBody(
                response.Body == null ? null : Encoding.UTF8.GetString(response.Body),
                _options.MaxDiagnosticBodyCharacters));

            throw StarhermitApiException.Create(error);
        }

        /// <summary>Builds the absolute address for a request.</summary>
        /// <param name="request">The request to address.</param>
        /// <returns>The absolute URI.</returns>
        public Uri BuildUri(StarhermitRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var path = request.Path.StartsWith("/", StringComparison.Ordinal) ? request.Path.Substring(1) : request.Path;
            var builder = new StringBuilder(path);

            var first = true;
            foreach (var parameter in request.Query)
            {
                builder.Append(first ? '?' : '&');
                first = false;
                builder.Append(Uri.EscapeDataString(parameter.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(parameter.Value));
            }

            return new Uri(_options.ApiBaseUri, builder.ToString());
        }

        private async Task ApplyHeadersAsync(
            StarhermitRequest request,
            StarhermitTransportRequest transportRequest,
            CancellationToken cancellationToken)
        {
            transportRequest.Headers.Add(new KeyValuePair<string, string>("Accept", "application/json"));
            transportRequest.Headers.Add(new KeyValuePair<string, string>(StarhermitSdk.VersionHeader, StarhermitSdk.Version));
            transportRequest.Headers.Add(new KeyValuePair<string, string>("User-Agent", _userAgent));

            foreach (var header in request.Headers)
            {
                // The slug header steers credential selection inside the pipeline; it is not part of
                // the wire contract and must not leave the process.
                if (string.Equals(header.Key, StarhermitHeaders.GameSlug, StringComparison.OrdinalIgnoreCase)) continue;
                transportRequest.Headers.Add(new KeyValuePair<string, string>(header.Key, header.Value));
            }

            var token = await ResolveCredentialAsync(request, cancellationToken).ConfigureAwait(false);
            if (token != null)
                transportRequest.Headers.Add(new KeyValuePair<string, string>("Authorization", "Bearer " + token));
        }

        private async Task<string?> ResolveCredentialAsync(StarhermitRequest request, CancellationToken cancellationToken)
        {
            switch (request.Credential)
            {
                case StarhermitCredential.None:
                    return null;

                case StarhermitCredential.Account:
                {
                    var token = await _sessions.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                    if (token == null)
                    {
                        throw StarhermitApiException.Create(new StarhermitErrorInfo
                        {
                            Status = 401,
                            Method = request.Method,
                            Path = request.Path,
                            ServerMessage = "This operation needs a signed-in account; no session is loaded.",
                            Headers = new Dictionary<string, string>(0)
                        });
                    }

                    return token;
                }

                case StarhermitCredential.AccountOptional:
                    return await _sessions.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

                case StarhermitCredential.Launch:
                {
                    var slug = request.Headers.TryGetValue(StarhermitHeaders.GameSlug, out var value) ? value : _options.GameSlug;
                    var launch = slug == null ? null : _scoped.GetLaunchToken(slug);
                    if (launch == null)
                    {
                        throw new StarhermitFeatureUnavailableException(
                            "games.launchToken",
                            StarhermitFeatureReasons.AdapterNotConfigured,
                            slug == null
                                ? "This operation needs a game-scoped launch token, and no game slug is configured."
                                : $"This operation needs a launch token for '{slug}'. Mint one with Games.ForSlug(slug).AcquireLaunchTokenAsync() first.");
                    }

                    return launch.Value.Token;
                }

                case StarhermitCredential.Server:
                {
                    var server = _scoped.ServerToken;
                    if (server == null)
                    {
                        throw new StarhermitFeatureUnavailableException(
                            "games.serverToken",
                            StarhermitFeatureReasons.AdapterNotConfigured,
                            "This operation needs a dedicated-server token. Exchange an invoke key with GameServer.AuthenticateAsync() first.");
                    }

                    return server.Value.Token;
                }

                default:
                    return null;
            }
        }

        private static bool UsesAccountSession(StarhermitCredential credential) =>
            credential == StarhermitCredential.Account || credential == StarhermitCredential.AccountOptional;

        private StarhermitApiResponse ToApiResponse(
            StarhermitRequest request,
            StarhermitTransportResponse response,
            string? requestId)
        {
            if (request.Expect == StarhermitResponseKind.Stream && response.BodyStream != null)
                return new StarhermitApiResponse(response.Status, response.Headers, null, response.BodyStream, requestId, response);

            var body = request.Expect == StarhermitResponseKind.None ? null : response.Body;
            var result = new StarhermitApiResponse(response.Status, response.Headers, body, null, requestId, null);
            response.Dispose();
            return result;
        }

        private static string? ReadRequestId(StarhermitTransportResponse response)
        {
            foreach (var name in RequestIdHeaders)
            {
                var value = response.Header(name);
                if (!string.IsNullOrEmpty(value)) return value;
            }

            return null;
        }

        private static IReadOnlyDictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers)
        {
            var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers) result[header.Key] = StarhermitRedactor.RedactHeader(header.Key, header.Value);
            return result;
        }

        private void RecordTelemetry(
            string operationId,
            TimeSpan duration,
            int statusFamily,
            int retries,
            string? requestId,
            StarhermitOperationOutcome outcome)
        {
            if (_telemetry == null) return;
            try
            {
                _telemetry.Record(new StarhermitTelemetryEvent(
                    "rest.request",
                    operationId,
                    duration,
                    statusFamily,
                    retries,
                    requestId,
                    outcome));
            }
            catch (Exception exception)
            {
                _log.Log(StarhermitLogLevel.Warning, "The telemetry sink threw; the event was dropped.", exception);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsTransport) _transport.Dispose();
        }

        /// <summary>Formats a value for a path segment, escaping it for the URL.</summary>
        /// <param name="value">The value to place in the path.</param>
        /// <returns>The escaped segment.</returns>
        public static string Segment(string value) => Uri.EscapeDataString(value ?? string.Empty);

        /// <summary>Formats a GUID for a path segment.</summary>
        /// <param name="value">The value to place in the path.</param>
        /// <returns>The canonical GUID text.</returns>
        public static string Segment(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
    }

    /// <summary>Header names the SDK uses internally to steer the pipeline.</summary>
    public static class StarhermitHeaders
    {
        /// <summary>
        /// Names the game slug whose launch token should authorise a request. Consumed by the pipeline
        /// and never sent to the server.
        /// </summary>
        public const string GameSlug = "X-Starhermit-Sdk-Game-Slug";
    }
}
