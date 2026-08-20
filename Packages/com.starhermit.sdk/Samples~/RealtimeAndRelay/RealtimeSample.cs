#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Starhermit.Samples
{
    /// <summary>
    /// A lobby with teams and seats, then binary traffic between the players in it.
    /// </summary>
    /// <remarks>
    /// Two things this sample makes concrete: the host is the only participant allowed to send
    /// <c>event</c> control frames, and a reconnect refetches the room rather than assuming the seat
    /// survived. Rooms close when the host is lost, so a client that assumed otherwise would sit in a
    /// lobby that no longer exists.
    /// </remarks>
    public sealed class RealtimeSample : MonoBehaviour
    {
        [SerializeField]
        private string gameSlug = "chess";

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private StarhermitRealtimeConnection? _room;
        private StarhermitRelayConnection? _relay;

        /// <summary>The signed-in client to use.</summary>
        public StarhermitClient? Client { get; set; }

        private void Start()
        {
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            var client = Client;
            if (client == null) return;

            var lobby = await client.RealtimeRooms.QuickJoinAsync(gameSlug, cancellationToken: _lifetime.Token)
                        ?? throw new InvalidOperationException("Quick join returned nothing.");

            Debug.Log($"[Sample] In room {lobby.Id} ({lobby.Status}) with {lobby.Participants.Count} seats filled.");

            _room = client.CreateRealtimeConnection(lobby.Id, gameSlug);
            _room.BinaryReceived += (participant, payload) =>
                Debug.Log($"[Sample] {payload.Length} bytes from participant {participant}.");
            _room.ControlReceived += (type, frame) => Debug.Log($"[Sample] control '{type}': {frame}");
            _room.PresenceChanged += (user, online) => Debug.Log($"[Sample] {user} is {(online ? "here" : "away")}.");
            _room.Closed += (code, reason) => Debug.Log($"[Sample] room socket closed ({code}): {reason}");

            await _room.ConnectAsync(_lifetime.Token);
            await _room.SendReadyAsync(true, _lifetime.Token);

            var isHost = lobby.HostUserId == client.Session?.UserId;
            if (isHost)
            {
                var seats = new List<StarhermitSeatAssignment>();
                var index = 0;
                foreach (var participant in lobby.Participants)
                    seats.Add(new StarhermitSeatAssignment(participant.Id, index++ % 2, 0));

                await client.RealtimeRooms.SetSeatsAsync(lobby.Id, seats, _lifetime.Token);
                var started = await client.RealtimeRooms.StartRoomAsync(lobby.Id, _lifetime.Token);
                Debug.Log($"[Sample] Started; session {started.GameSessionId}.");

                // A relay is bound to the match that authorises it - here, the room.
                var relay = await client.Relay.CreateSessionAsync(
                    titleId: Guid.Empty,
                    realtimeRoomId: lobby.Id,
                    cancellationToken: _lifetime.Token);

                _relay = client.CreateRelayConnection(relay.Id, relay.TitleId);
                _relay.PayloadReceived += payload => Debug.Log($"[Sample] relay payload of {payload.Length} bytes.");
                await _relay.ConnectAsync(_lifetime.Token);
                await _relay.SendAsync(new byte[] { 0x01, 0x02 }, _lifetime.Token);
            }
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _relay?.Dispose();
            _room?.Dispose();
        }
    }
}
#endif
