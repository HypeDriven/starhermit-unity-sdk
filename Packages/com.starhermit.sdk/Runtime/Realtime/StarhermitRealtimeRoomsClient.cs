using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>Realtime rooms: lobbies with teams and seats that start a match.</summary>
    public sealed class StarhermitRealtimeRoomsClient : StarhermitServiceClient
    {
        internal StarhermitRealtimeRoomsClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Creates a room.</summary>
        /// <param name="gameSlug">Game the room is for; defaults to the configured slug.</param>
        /// <param name="teamCount">How many teams.</param>
        /// <param name="seatsPerTeam">How many seats per team.</param>
        /// <param name="backfillAfterSeconds">Seconds before empty seats open for backfill.</param>
        /// <param name="aiPlayers">Seats to fill with AI immediately.</param>
        /// <param name="metadata">Room metadata in whatever shape the game defines.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new room.</returns>
        public Task<StarhermitRoom> CreateRoomAsync(
            string? gameSlug = null,
            int teamCount = 2,
            int seatsPerTeam = 1,
            int? backfillAfterSeconds = null,
            int aiPlayers = 0,
            JsonValue? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var slug = gameSlug ?? Options.GameSlug;
            var request = WithBody(Post("realtime/rooms"), writer =>
            {
                writer.WriteIfPresent("gameSlug", slug);
                writer.Write("teamCount", teamCount);
                writer.Write("seatsPerTeam", seatsPerTeam);
                writer.WriteIfPresent("backfillAfterSeconds", backfillAfterSeconds);
                writer.Write("aiPlayers", aiPlayers);
                if (metadata != null) writer.Write("metadata", metadata);
            });

            return SendAsync(request, "realtime.createRoom", StarhermitRoom.Read, cancellationToken);
        }

        /// <summary>Reads the caller's active room, if they are in one.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room, or null when the caller is not in one.</returns>
        public async Task<StarhermitRoom?> GetMyRoomAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await SendAsync(Get("realtime/rooms/mine"), "realtime.getMyRoom", StarhermitRoom.Read, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (StarhermitNotFoundException)
            {
                return null;
            }
        }

        /// <summary>Lists room invitations addressed to the caller.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The invitations.</returns>
        public async Task<IReadOnlyList<StarhermitRoomInvite>> GetInvitesAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("realtime/rooms/invites"), "realtime.getInvites", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitRoomInvite.Read);
        }

        /// <summary>Accepts a room invitation and takes a seat.</summary>
        /// <param name="inviteId">The invitation to accept.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room that was joined.</returns>
        public Task<StarhermitRoom> AcceptInviteAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"realtime/rooms/invites/{Escape(inviteId)}/accept"),
                "realtime.acceptInvite",
                StarhermitRoom.Read,
                cancellationToken);

        /// <summary>Declines a room invitation.</summary>
        /// <param name="inviteId">The invitation to decline.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The closed invitation.</returns>
        public Task<StarhermitRoomInvite> DeclineInviteAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"realtime/rooms/invites/{Escape(inviteId)}/decline"),
                "realtime.declineInvite",
                StarhermitRoomInvite.Read,
                cancellationToken);

        /// <summary>
        /// Joins any room with space, creating one when none is available.
        /// </summary>
        /// <param name="gameSlug">Game to quick-join; defaults to the configured slug.</param>
        /// <param name="seats">How many adjacent seats to claim. The deployment currently accepts one.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room the caller landed in.</returns>
        public Task<StarhermitRoom> QuickJoinAsync(
            string? gameSlug = null,
            int seats = 1,
            CancellationToken cancellationToken = default)
        {
            var slug = gameSlug ?? Options.GameSlug;
            var request = WithBody(Post("realtime/rooms/quick-join"), writer =>
            {
                writer.WriteIfPresent("gameSlug", slug);
                writer.Write("seats", seats);
            });

            return SendAsync(request, "realtime.quickJoin", StarhermitRoom.Read, cancellationToken);
        }

        /// <summary>Reads one room.</summary>
        /// <param name="roomId">The room to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room.</returns>
        public Task<StarhermitRoom> GetRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
            SendAsync(Get($"realtime/rooms/{Escape(roomId)}"), "realtime.getRoom", StarhermitRoom.Read, cancellationToken);

        /// <summary>Invites a player to a room.</summary>
        /// <param name="roomId">The room.</param>
        /// <param name="toUserId">Who to invite.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The invitation.</returns>
        public Task<StarhermitRoomInvite> CreateInviteAsync(
            Guid roomId,
            Guid toUserId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post($"realtime/rooms/{Escape(roomId)}/invites"), writer => writer.Write("toUserId", toUserId)),
                "realtime.createInvite",
                StarhermitRoomInvite.Read,
                cancellationToken);

        /// <summary>Opens a room so strangers can backfill its empty seats.</summary>
        /// <param name="roomId">The room to open.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room as it stands.</returns>
        public Task<StarhermitRoom> OpenRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"realtime/rooms/{Escape(roomId)}/open"), "realtime.openRoom", StarhermitRoom.Read, cancellationToken);

        /// <summary>Starts the match. Host only.</summary>
        /// <param name="roomId">The room to start.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room, now carrying its game session id.</returns>
        public Task<StarhermitRoom> StartRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"realtime/rooms/{Escape(roomId)}/start"), "realtime.startRoom", StarhermitRoom.Read, cancellationToken);

        /// <summary>Leaves a room.</summary>
        /// <param name="roomId">The room to leave.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the caller has left.</returns>
        public Task LeaveRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"realtime/rooms/{Escape(roomId)}/leave"), "realtime.leaveRoom", cancellationToken);

        /// <summary>Assigns seats. Host only.</summary>
        /// <param name="roomId">The room.</param>
        /// <param name="seats">Where each participant should sit.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room with its new seating.</returns>
        public Task<StarhermitRoom> SetSeatsAsync(
            Guid roomId,
            IEnumerable<StarhermitSeatAssignment> seats,
            CancellationToken cancellationToken = default)
        {
            if (seats == null) throw new ArgumentNullException(nameof(seats));
            var request = WithBody(Post($"realtime/rooms/{Escape(roomId)}/seats"), writer =>
                writer.WriteArray("seats", seats, (w, seat) =>
                {
                    w.WriteStartObject();
                    w.Write("participantId", seat.ParticipantId);
                    w.Write("team", seat.Team);
                    w.Write("slot", seat.Slot);
                    w.WriteEndObject();
                }));

            return SendAsync(request, "realtime.setSeats", StarhermitRoom.Read, cancellationToken);
        }

        /// <summary>Submits the match result.</summary>
        /// <param name="roomId">The room.</param>
        /// <param name="teamScores">Score per team, in team order.</param>
        /// <param name="metadata">Extra result detail the game defines.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The room with its recorded result.</returns>
        public Task<StarhermitRoom> SubmitResultAsync(
            Guid roomId,
            IEnumerable<int> teamScores,
            JsonValue? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (teamScores == null) throw new ArgumentNullException(nameof(teamScores));
            var request = WithBody(Post($"realtime/rooms/{Escape(roomId)}/result"), writer =>
            {
                writer.WriteArray("teamScores", teamScores, (w, score) => w.WriteNumber(score));
                if (metadata != null) writer.Write("metadata", metadata);
            });

            return SendAsync(request, "realtime.submitResult", StarhermitRoom.Read, cancellationToken);
        }
    }

    /// <summary>Peer relays: server-brokered binary traffic between the members of a match.</summary>
    /// <remarks>
    /// A relay is always bound to a game session or a realtime room, and that roster is what
    /// authorises every join and every socket. The SDK never assumes a send rate: pacing comes from
    /// the game's declaration, and the server closes a connection that exceeds it.
    /// </remarks>
    public sealed class StarhermitRelayClient : StarhermitServiceClient
    {
        internal StarhermitRelayClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Lists relays the caller can see for a title.</summary>
        /// <param name="titleId">The catalog title.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The relays.</returns>
        public async Task<IReadOnlyList<StarhermitRelaySession>> ListSessionsAsync(
            Guid titleId,
            CancellationToken cancellationToken = default)
        {
            var request = Get("relay").WithQuery("titleId", titleId);
            var json = await SendJsonAsync(request, "relay.listSessions", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitRelaySession.Read);
        }

        /// <summary>
        /// Creates a relay bound to a game session or a realtime room. Exactly one must be supplied.
        /// </summary>
        /// <param name="titleId">The catalog title.</param>
        /// <param name="gameSessionId">The game session that authorises the roster.</param>
        /// <param name="realtimeRoomId">The realtime room that authorises the roster.</param>
        /// <param name="maxParticipants">Participant limit to request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new relay.</returns>
        public Task<StarhermitRelaySession> CreateSessionAsync(
            Guid titleId,
            Guid? gameSessionId = null,
            Guid? realtimeRoomId = null,
            int? maxParticipants = null,
            CancellationToken cancellationToken = default)
        {
            if (gameSessionId.HasValue == realtimeRoomId.HasValue)
            {
                throw new ArgumentException(
                    "A relay must be bound to exactly one of a game session or a realtime room - that roster is what authorises it.",
                    nameof(gameSessionId));
            }

            var request = WithBody(Post("relay"), writer =>
            {
                writer.Write("titleId", titleId);
                writer.WriteIfPresent("gameSessionId", gameSessionId);
                writer.WriteIfPresent("realtimeRoomId", realtimeRoomId);
                writer.WriteIfPresent("maxParticipants", maxParticipants);
            });

            return SendAsync(request, "relay.createSession", StarhermitRelaySession.Read, cancellationToken);
        }

        /// <summary>Reads one relay.</summary>
        /// <param name="sessionId">The relay to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The relay.</returns>
        public Task<StarhermitRelaySession> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            SendAsync(Get($"relay/{Escape(sessionId)}"), "relay.getSession", StarhermitRelaySession.Read, cancellationToken);

        /// <summary>Joins a relay's roster.</summary>
        /// <param name="sessionId">The relay to join.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The relay as it stands after joining.</returns>
        public Task<StarhermitRelaySession> JoinSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"relay/{Escape(sessionId)}/join"), "relay.joinSession", StarhermitRelaySession.Read, cancellationToken);

        /// <summary>Closes a relay.</summary>
        /// <param name="sessionId">The relay to close.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the relay is closed.</returns>
        public Task CloseSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"relay/{Escape(sessionId)}/close"), "relay.closeSession", cancellationToken);
    }
}
