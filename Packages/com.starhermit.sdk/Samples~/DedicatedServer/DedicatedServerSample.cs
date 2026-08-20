#if UNITY_2021_3_OR_NEWER
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Starhermit.Samples
{
    /// <summary>
    /// A headless server build: exchange a deployment key for a server token, read sessions, and shut
    /// down cleanly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This belongs in a server build only. A deployment key inside a player build hands every player
    /// the game's own credentials, and no amount of obfuscation changes that.
    /// </para>
    /// <para>
    /// Note what a server build does not configure: no microphone, no OAuth browser, no secure store.
    /// Those adapters are absent, and every other module still works - which is the whole point of the
    /// adapter seam.
    /// </para>
    /// </remarks>
    public sealed class DedicatedServerSample : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private StarhermitClient? _client;

        /// <summary>The game this deployment serves.</summary>
        public string GameSlug { get; set; } = "chess";

        private void Start()
        {
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            // The host supplies the key through the environment; it is never compiled in.
            var refreshKey = Environment.GetEnvironmentVariable("STARHERMIT_REFRESH_KEY");
            if (string.IsNullOrEmpty(refreshKey))
            {
                Debug.LogError("[Sample] STARHERMIT_REFRESH_KEY is not set; this build cannot authenticate.");
                return;
            }

            var options = new StarhermitOptions
            {
                // A server has no main thread to marshal to and nothing waiting on a frame boundary.
                CallbackDispatcher = ImmediateCallbackDispatcher.Instance,
                LogLevel = StarhermitLogLevel.Info
            };

            _client = StarhermitClient.Create(options);

            var token = await _client.GameServer.AuthenticateAsync(GameSlug, refreshKey!, _lifetime.Token);
            Debug.Log($"[Sample] Server token acquired; expires {token.ExpiresAt:u}.");

            var sessionId = Guid.Empty;
            if (sessionId != Guid.Empty)
            {
                var session = await _client.GameServer.GetSessionAsync(GameSlug, sessionId, _lifetime.Token);
                Debug.Log($"[Sample] Session {session.SessionId} is {session.Status} with {session.Players.Count} players.");
            }
        }

        private void OnDestroy()
        {
            // Disposal cancels in-flight requests and closes sockets; the server token is dropped with
            // the client rather than being written anywhere.
            _lifetime.Cancel();
            _client?.GameServer.ClearServerToken();
            _client?.Dispose();
        }
    }
}
#endif
