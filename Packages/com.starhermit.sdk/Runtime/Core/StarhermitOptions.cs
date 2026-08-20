using System;

namespace Starhermit
{
    /// <summary>
    /// Everything a <see cref="StarhermitClient"/> needs: addresses, budgets, policies and the platform
    /// adapters it should use.
    /// </summary>
    /// <remarks>
    /// Defaults are the ones a shipped game should want: production addresses, HTTPS enforced,
    /// bounded retries, no telemetry, and an in-memory token store rather than a persistent one the
    /// package cannot promise to protect. Everything platform-specific is an adapter, so the same
    /// options object describes a desktop build, a WebGL build and a headless server.
    /// </remarks>
    public sealed class StarhermitOptions
    {
        /// <summary>The API address used when none is configured.</summary>
        public static readonly Uri DefaultApiBaseUri = new Uri("https://api.starhermit.com/api/v1/");

        /// <summary>Base address of the REST API, including the version segment and a trailing slash.</summary>
        public Uri ApiBaseUri { get; set; } = DefaultApiBaseUri;

        /// <summary>
        /// Base address for WebSockets. Left null it is derived from <see cref="ApiBaseUri"/> by
        /// swapping the scheme and replacing the path with <c>/ws/v1/</c>, which is where the
        /// deployment serves them.
        /// </summary>
        public Uri? WebSocketBaseUri { get; set; }

        /// <summary>
        /// Default game slug for game-scoped calls, so a title that ships one game need not repeat it.
        /// </summary>
        public string? GameSlug { get; set; }

        /// <summary>Time budget for one REST attempt, excluding retries.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Time budget for opening a socket.</summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Retry policy for REST requests.</summary>
        public StarhermitRetryPolicy RetryPolicy { get; set; } = StarhermitRetryPolicy.Default;

        /// <summary>HTTP transport. Left null the client picks the platform default.</summary>
        public IStarhermitTransport? Transport { get; set; }

        /// <summary>Socket factory. Left null the client picks the platform default.</summary>
        public IStarhermitSocketFactory? SocketFactory { get; set; }

        /// <summary>Where the account session is persisted. In-memory by default.</summary>
        public IStarhermitTokenStore TokenStore { get; set; } = new InMemoryTokenStore();

        /// <summary>Adapter that runs OAuth sign-in. Required only for OAuth flows.</summary>
        public IStarhermitOAuthBrowser? OAuthBrowser { get; set; }

        /// <summary>Adapter that signs public-key challenges. Required only for public-key flows.</summary>
        public IStarhermitSigner? Signer { get; set; }

        /// <summary>Time source.</summary>
        public IStarhermitClock Clock { get; set; } = SystemClock.Instance;

        /// <summary>Where SDK diagnostics go.</summary>
        public IStarhermitLogger Logger { get; set; } = NullStarhermitLogger.Instance;

        /// <summary>How much the SDK logs.</summary>
        public StarhermitLogLevel LogLevel { get; set; } = StarhermitLogLevel.Warning;

        /// <summary>Optional telemetry sink. Nothing is collected unless one is supplied.</summary>
        public IStarhermitTelemetrySink? Telemetry { get; set; }

        /// <summary>
        /// Where events and progress callbacks are raised. Left null the client captures the
        /// synchronization context it was created on, which in Unity is the main thread.
        /// </summary>
        public IStarhermitCallbackDispatcher? CallbackDispatcher { get; set; }

        /// <summary>File access for downloads and cloud saves. Required only for file overloads.</summary>
        public IStarhermitFileStore? FileStore { get; set; }

        /// <summary>Microphone adapter. Required only for voice capture.</summary>
        public IStarhermitAudioCapture? AudioCapture { get; set; }

        /// <summary>Playback adapter. Required only for voice playback.</summary>
        public IStarhermitAudioPlayback? AudioPlayback { get; set; }

        /// <summary>How long before expiry a token is treated as spent, absorbing clock skew.</summary>
        public TimeSpan TokenRefreshLeeway { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Messages that may wait in a socket's outbound queue before sends are refused.</summary>
        public int MaxOutboundQueuedMessages { get; set; } = 256;

        /// <summary>
        /// Largest inbound socket message accepted. Enforced locally even when the deployment allows
        /// more, so a hostile or faulty peer cannot grow the heap without bound.
        /// </summary>
        public int MaxIncomingMessageBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>Largest outbound socket message the SDK will send.</summary>
        public int MaxOutgoingMessageBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>Characters of a response body kept on an exception for diagnostics.</summary>
        public int MaxDiagnosticBodyCharacters { get; set; } = 4096;

        /// <summary>
        /// Allows plain <c>http</c>/<c>ws</c> addresses. Development only: it must never be set in a
        /// shipped build, and it does not disable certificate validation for anything else.
        /// </summary>
        public bool AllowInsecureTransport { get; set; }

        /// <summary>Extra token appended to the SDK's <c>User-Agent</c>, such as a game name.</summary>
        public string? UserAgentSuffix { get; set; }

        /// <summary>Copies the options, so a client is unaffected by later edits to the original.</summary>
        /// <returns>An independent copy.</returns>
        public StarhermitOptions Clone() => (StarhermitOptions)MemberwiseClone();

        /// <summary>Resolves the WebSocket base address, deriving it from the API address if needed.</summary>
        /// <returns>The base address sockets are opened under.</returns>
        public Uri ResolveWebSocketBaseUri()
        {
            if (WebSocketBaseUri != null) return WebSocketBaseUri;
            var api = ApiBaseUri ?? DefaultApiBaseUri;
            var builder = new UriBuilder(api)
            {
                Scheme = string.Equals(api.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
                Path = "/ws/v1/",
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri;
        }

        /// <summary>Checks the options are usable and refuses combinations that are not.</summary>
        /// <exception cref="ArgumentException">An option is missing or inconsistent.</exception>
        public void Validate()
        {
            if (ApiBaseUri == null) throw new ArgumentException("ApiBaseUri is required.", nameof(ApiBaseUri));
            if (!ApiBaseUri.IsAbsoluteUri) throw new ArgumentException("ApiBaseUri must be absolute.", nameof(ApiBaseUri));
            if (TokenStore == null) throw new ArgumentException("TokenStore is required.", nameof(TokenStore));
            if (Clock == null) throw new ArgumentException("Clock is required.", nameof(Clock));
            if (RequestTimeout <= TimeSpan.Zero) throw new ArgumentException("RequestTimeout must be positive.", nameof(RequestTimeout));
            if (ConnectTimeout <= TimeSpan.Zero) throw new ArgumentException("ConnectTimeout must be positive.", nameof(ConnectTimeout));
            if (MaxIncomingMessageBytes <= 0) throw new ArgumentException("MaxIncomingMessageBytes must be positive.", nameof(MaxIncomingMessageBytes));
            if (MaxOutboundQueuedMessages <= 0) throw new ArgumentException("MaxOutboundQueuedMessages must be positive.", nameof(MaxOutboundQueuedMessages));

            var insecureApi = !string.Equals(ApiBaseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
            if (insecureApi && !AllowInsecureTransport)
            {
                throw new ArgumentException(
                    $"ApiBaseUri '{ApiBaseUri.Scheme}://{ApiBaseUri.Host}' is not HTTPS. Set AllowInsecureTransport only for a development endpoint.",
                    nameof(ApiBaseUri));
            }

            var socketUri = ResolveWebSocketBaseUri();
            var insecureSocket = !string.Equals(socketUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
            if (insecureSocket && !AllowInsecureTransport)
            {
                throw new ArgumentException(
                    $"WebSocketBaseUri '{socketUri.Scheme}://{socketUri.Host}' is not WSS. Set AllowInsecureTransport only for a development endpoint.",
                    nameof(WebSocketBaseUri));
            }
        }
    }

    /// <summary>Constants describing this build of the SDK.</summary>
    public static class StarhermitSdk
    {
        /// <summary>The package version, sent as <c>X-Starhermit-SDK-Version</c>.</summary>
        public const string Version = "0.1.0";

        /// <summary>The API contract version this build targets.</summary>
        public const string ApiVersion = "v1";

        /// <summary>Header carrying the SDK version on every request.</summary>
        public const string VersionHeader = "X-Starhermit-SDK-Version";

        /// <summary>Builds the <c>User-Agent</c> value.</summary>
        /// <param name="suffix">Optional application token to append.</param>
        /// <returns>The user agent string.</returns>
        public static string UserAgent(string? suffix) =>
            string.IsNullOrWhiteSpace(suffix)
                ? $"StarhermitUnitySDK/{Version}"
                : $"StarhermitUnitySDK/{Version} {suffix}";
    }
}
