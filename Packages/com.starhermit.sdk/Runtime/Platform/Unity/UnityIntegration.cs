#if UNITY_2021_3_OR_NEWER
using System;
using UnityEngine;

namespace Starhermit.Platform
{
    /// <summary>Writes SDK diagnostics to the Unity console.</summary>
    /// <remarks>Messages have already been redacted, so nothing here can leak a credential.</remarks>
    public sealed class UnityStarhermitLogger : IStarhermitLogger
    {
        /// <inheritdoc />
        public void Log(StarhermitLogLevel level, string message, Exception? exception = null)
        {
            var text = exception == null ? "[Starhermit] " + message : "[Starhermit] " + message + "\n" + exception;
            switch (level)
            {
                case StarhermitLogLevel.Error:
                    Debug.LogError(text);
                    break;
                case StarhermitLogLevel.Warning:
                    Debug.LogWarning(text);
                    break;
                default:
                    Debug.Log(text);
                    break;
            }
        }
    }

    /// <summary>
    /// Non-secret defaults for a project, stored as an asset.
    /// </summary>
    /// <remarks>
    /// Addresses and log levels only. Tokens, keys, client secrets and invoke keys must never be
    /// serialised into an asset: it ships inside the build, where anyone can read it.
    /// </remarks>
    [CreateAssetMenu(fileName = "StarhermitSettings", menuName = "Starhermit/Settings", order = 0)]
    public sealed class StarhermitSettings : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Base address of the REST API, including the version segment and a trailing slash.")]
        private string apiBaseUri = "https://api.starhermit.com/api/v1/";

        [SerializeField]
        [Tooltip("WebSocket base address. Leave empty to derive it from the API address.")]
        private string webSocketBaseUri = string.Empty;

        [SerializeField]
        [Tooltip("Default game slug for game-scoped calls.")]
        private string gameSlug = string.Empty;

        [SerializeField]
        [Tooltip("How much the SDK writes to the Unity console.")]
        private StarhermitLogLevel logLevel = StarhermitLogLevel.Warning;

        [SerializeField]
        [Tooltip("Allow plain http/ws. Development only - never enable this in a shipped build.")]
        private bool allowInsecureTransport = false;

        [SerializeField]
        [Tooltip("Seconds allowed for one REST attempt.")]
        private int requestTimeoutSeconds = 30;

        /// <summary>Base address of the REST API.</summary>
        public string ApiBaseUri => apiBaseUri;

        /// <summary>WebSocket base address, or empty to derive it.</summary>
        public string WebSocketBaseUri => webSocketBaseUri;

        /// <summary>Default game slug.</summary>
        public string GameSlug => gameSlug;

        /// <summary>Console log level.</summary>
        public StarhermitLogLevel LogLevel => logLevel;

        /// <summary>Whether plain http/ws is permitted.</summary>
        public bool AllowInsecureTransport => allowInsecureTransport;

        /// <summary>Seconds allowed for one REST attempt.</summary>
        public int RequestTimeoutSeconds => requestTimeoutSeconds;

        /// <summary>Builds client options from these settings and the Unity platform adapters.</summary>
        /// <param name="tokenStore">
        /// Where the session is persisted. Left null the session lives in memory only, because the
        /// package will not pretend that any store it could pick for you is secure.
        /// </param>
        /// <returns>Options ready to create a client from.</returns>
        public StarhermitOptions ToOptions(IStarhermitTokenStore? tokenStore = null)
        {
            var options = new StarhermitOptions
            {
                ApiBaseUri = new Uri(string.IsNullOrWhiteSpace(apiBaseUri)
                    ? StarhermitOptions.DefaultApiBaseUri.ToString()
                    : apiBaseUri),
                WebSocketBaseUri = string.IsNullOrWhiteSpace(webSocketBaseUri) ? null : new Uri(webSocketBaseUri),
                GameSlug = string.IsNullOrWhiteSpace(gameSlug) ? null : gameSlug,
                Logger = new UnityStarhermitLogger(),
                LogLevel = logLevel,
                AllowInsecureTransport = allowInsecureTransport,
                RequestTimeout = TimeSpan.FromSeconds(Math.Max(1, requestTimeoutSeconds)),
                UserAgentSuffix = Application.productName + "/" + Application.version
            };

            if (tokenStore != null) options.TokenStore = tokenStore;
            return options;
        }
    }

    /// <summary>
    /// Bridges Unity's application lifecycle to the SDK: pausing presence and audio on suspension,
    /// and closing connections cleanly on quit.
    /// </summary>
    /// <remarks>
    /// Mobile suspension stops heartbeats and audio rather than leaving them to fail; resuming
    /// refreshes the session and lets each connection refetch what it was attached to. Quitting does
    /// no synchronous network work - a socket close is best-effort by then.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StarhermitLifecycle : MonoBehaviour
    {
        private StarhermitClient? _client;
        private StarhermitPresenceHeartbeat? _heartbeat;

        /// <summary>Attaches a lifecycle bridge for a client to a new, persistent object.</summary>
        /// <param name="client">The client to manage.</param>
        /// <param name="heartbeat">Optional presence heartbeat to pause and resume.</param>
        /// <returns>The component, which lives until the application quits.</returns>
        public static StarhermitLifecycle Attach(StarhermitClient client, StarhermitPresenceHeartbeat? heartbeat = null)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            var host = new GameObject("Starhermit Lifecycle");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideInHierarchy;

            var lifecycle = host.AddComponent<StarhermitLifecycle>();
            lifecycle._client = client;
            lifecycle._heartbeat = heartbeat;
            return lifecycle;
        }

        private void OnApplicationPause(bool paused)
        {
            if (_heartbeat == null) return;
            if (paused) _heartbeat.Pause();
            else _heartbeat.Resume();
        }

        private void OnApplicationQuit()
        {
            // Disposal cancels requests and closes sockets. No blocking network work happens here:
            // the platform is already tearing the process down.
            _heartbeat?.Dispose();
            _client?.Dispose();
        }
    }
}
#endif
