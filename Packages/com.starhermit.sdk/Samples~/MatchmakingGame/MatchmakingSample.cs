#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;
using UnityEngine;

namespace Starhermit.Samples
{
    /// <summary>
    /// A whole match: mint a launch token, queue for an opponent, attach the game socket, exchange
    /// commands, and read the replay afterwards.
    /// </summary>
    /// <remarks>
    /// The launch token is what a game build should use. It is game-scoped, the backend fences it to
    /// this game's routes, and minting it does not replace or expose the account session.
    /// </remarks>
    public sealed class MatchmakingSample : MonoBehaviour
    {
        [SerializeField]
        private string gameSlug = "chess";

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private StarhermitGameConnection? _connection;

        /// <summary>The signed-in client to play with.</summary>
        public StarhermitClient? Client { get; set; }

        private void Start()
        {
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            var client = Client;
            if (client == null) return;

            var game = client.Games.ForSlug(gameSlug);
            var info = await game.GetInfoAsync(_lifetime.Token);
            Debug.Log($"[Sample] {info.Name}: rating {info.Me?.Elo}, replays {(info.ReplaysEnabled ? "on" : "off")}.");

            await game.AcquireLaunchTokenAsync(_lifetime.Token);
            var scoped = game.WithLaunchToken();

            // Player preferences live in the settings document, never in the cloud save.
            await scoped.PutSettingAsync("audio.music", JsonParser.Parse("0.4"), _lifetime.Token);

            var ticket = await scoped.EnqueueMatchmakingAsync(_lifetime.Token);
            Debug.Log($"[Sample] Queued: ticket {ticket.TicketId}.");

            var session = await WaitForMatchAsync(scoped, _lifetime.Token);
            if (session == null)
            {
                Debug.Log("[Sample] No opponent found; cancelling.");
                await scoped.CancelMatchmakingAsync(_lifetime.Token);
                return;
            }

            _connection = client.CreateGameConnection(session.Value, gameSlug, useLaunchToken: true);
            _connection.FrameReceived += frame => Debug.Log($"[Sample] game frame: {frame}");
            _connection.ErrorReceived += error => Debug.LogWarning($"[Sample] the game refused a command: {error}");
            _connection.PresenceChanged += (player, online) => Debug.Log($"[Sample] {player} is {(online ? "here" : "away")}.");

            await _connection.ConnectAsync(_lifetime.Token);
            await _connection.SendCommandAsync(
                writer =>
                {
                    writer.Write("type", "move");
                    writer.Write("from", "e2");
                    writer.Write("to", "e4");
                },
                _lifetime.Token);
        }

        private static async Task<Guid?> WaitForMatchAsync(StarhermitGameClient game, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var ticket = await game.GetMatchmakingAsync(cancellationToken);
                if (ticket?.SessionId != null) return ticket.SessionId;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            return null;
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _connection?.Dispose();
        }
    }
}
#endif
