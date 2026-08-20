using System;
using System.Collections.Generic;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>How a realtime room is laid out.</summary>
    public sealed class StarhermitRoomConfig : StarhermitModel
    {
        private StarhermitRoomConfig(JsonValue json) : base(json)
        {
            TeamCount = json["teamCount"].AsInt32OrDefault();
            SeatsPerTeam = json["seatsPerTeam"].AsInt32OrDefault();
            BackfillAfterSeconds = json["backfillAfterSeconds"].AsInt32OrDefault();
            AiPlayers = json["aiPlayers"].AsInt32OrDefault();
            Metadata = json["metadata"];
        }

        /// <summary>How many teams the room has.</summary>
        public int TeamCount { get; }

        /// <summary>How many seats each team has.</summary>
        public int SeatsPerTeam { get; }

        /// <summary>How long before empty seats are opened for backfill.</summary>
        public int BackfillAfterSeconds { get; }

        /// <summary>How many seats are filled by AI from the start.</summary>
        public int AiPlayers { get; }

        /// <summary>Room metadata, in whatever shape the game defines.</summary>
        public JsonValue Metadata { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRoomConfig Read(JsonValue json) => new StarhermitRoomConfig(json);
    }

    /// <summary>Someone seated in a realtime room.</summary>
    public sealed class StarhermitRoomParticipant : StarhermitModel
    {
        private StarhermitRoomParticipant(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            UserId = json["userId"].AsGuidOrNull();
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            IsAi = json["isAi"].AsBooleanOrDefault();
            IsHost = json["isHost"].AsBooleanOrDefault();
            Team = json["team"].AsInt32OrDefault();
            Slot = json["slot"].AsInt32OrDefault();
            JoinedAt = json["joinedAt"].AsDateTimeOffsetOrNull();
            LeftAt = json["leftAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Participant id, which is what seat assignments refer to.</summary>
        public Guid Id { get; }

        /// <summary>The account, when the seat is held by a player rather than AI.</summary>
        public Guid? UserId { get; }

        /// <summary>Display name.</summary>
        public string Username { get; }

        /// <summary>True when the seat is filled by AI.</summary>
        public bool IsAi { get; }

        /// <summary>True for the room's host.</summary>
        public bool IsHost { get; }

        /// <summary>Team index.</summary>
        public int Team { get; }

        /// <summary>Seat index within the team.</summary>
        public int Slot { get; }

        /// <summary>When they joined.</summary>
        public DateTimeOffset? JoinedAt { get; }

        /// <summary>When they left, when they have.</summary>
        public DateTimeOffset? LeftAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRoomParticipant Read(JsonValue json) => new StarhermitRoomParticipant(json);
    }

    /// <summary>A realtime room: the lobby a match is assembled in.</summary>
    public sealed class StarhermitRoom : StarhermitModel
    {
        private StarhermitRoom(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            GameSlug = json["gameSlug"].AsStringOrNull() ?? string.Empty;
            HostUserId = json["hostUserId"].AsGuidOrNull() ?? Guid.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            GameSessionId = json["gameSessionId"].AsGuidOrNull();
            Config = json["config"].IsObject ? StarhermitRoomConfig.Read(json["config"]) : null;
            Participants = json["participants"].AsList(StarhermitRoomParticipant.Read);
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            OpenedAt = json["openedAt"].AsDateTimeOffsetOrNull();
            StartedAt = json["startedAt"].AsDateTimeOffsetOrNull();
            ClosedAt = json["closedAt"].AsDateTimeOffsetOrNull();
            Result = json["result"];
        }

        /// <summary>Room id.</summary>
        public Guid Id { get; }

        /// <summary>The game the room is for.</summary>
        public string GameSlug { get; }

        /// <summary>Who hosts it. Losing the host closes the room.</summary>
        public Guid HostUserId { get; }

        /// <summary>Room phase - see <see cref="StarhermitRoomStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>The game session the room started, once it has.</summary>
        public Guid? GameSessionId { get; }

        /// <summary>Team and seat layout.</summary>
        public StarhermitRoomConfig? Config { get; }

        /// <summary>Everyone seated, including AI.</summary>
        public IReadOnlyList<StarhermitRoomParticipant> Participants { get; }

        /// <summary>When the room was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it was opened for backfill.</summary>
        public DateTimeOffset? OpenedAt { get; }

        /// <summary>When the match started.</summary>
        public DateTimeOffset? StartedAt { get; }

        /// <summary>When the room closed.</summary>
        public DateTimeOffset? ClosedAt { get; }

        /// <summary>The submitted result, in whatever shape the game defines.</summary>
        public JsonValue Result { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRoom Read(JsonValue json) => new StarhermitRoom(json);
    }

    /// <summary>An invitation to a realtime room.</summary>
    public sealed class StarhermitRoomInvite : StarhermitModel
    {
        private StarhermitRoomInvite(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            RoomId = json["roomId"].AsGuidOrNull() ?? Guid.Empty;
            GameSlug = json["gameSlug"].AsStringOrNull() ?? string.Empty;
            FromUserId = json["fromUserId"].AsGuidOrNull() ?? Guid.Empty;
            FromUsername = json["fromUsername"].AsStringOrNull();
            ToUserId = json["toUserId"].AsGuidOrNull() ?? Guid.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Invite id.</summary>
        public Guid Id { get; }

        /// <summary>The room being offered.</summary>
        public Guid RoomId { get; }

        /// <summary>The game the room is for.</summary>
        public string GameSlug { get; }

        /// <summary>Who sent the invite.</summary>
        public Guid FromUserId { get; }

        /// <summary>Their username.</summary>
        public string? FromUsername { get; }

        /// <summary>Who it was sent to.</summary>
        public Guid ToUserId { get; }

        /// <summary>Invite status.</summary>
        public string Status { get; }

        /// <summary>When it was sent.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRoomInvite Read(JsonValue json) => new StarhermitRoomInvite(json);
    }

    /// <summary>Where one participant should sit.</summary>
    public readonly struct StarhermitSeatAssignment
    {
        /// <summary>Creates an assignment.</summary>
        /// <param name="participantId">The participant to seat.</param>
        /// <param name="team">Team index.</param>
        /// <param name="slot">Seat index within the team.</param>
        public StarhermitSeatAssignment(Guid participantId, int team, int slot)
        {
            ParticipantId = participantId;
            Team = team;
            Slot = slot;
        }

        /// <summary>The participant to seat.</summary>
        public Guid ParticipantId { get; }

        /// <summary>Team index.</summary>
        public int Team { get; }

        /// <summary>Seat index within the team.</summary>
        public int Slot { get; }
    }

    /// <summary>A peer relay session.</summary>
    public sealed class StarhermitRelaySession : StarhermitModel
    {
        private StarhermitRelaySession(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            TitleId = json["titleId"].AsGuidOrNull() ?? Guid.Empty;
            CreatorUserId = json["creatorUserId"].AsGuidOrNull() ?? Guid.Empty;
            GameSessionId = json["gameSessionId"].AsGuidOrNull();
            RealtimeRoomId = json["realtimeRoomId"].AsGuidOrNull();
            MaxParticipants = json["maxParticipants"].AsInt32OrDefault();
            CurrentParticipantCount = json["currentParticipantCount"].AsInt32OrDefault();
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            ClosedAt = json["closedAt"].AsDateTimeOffsetOrNull();
            Participants = json["participants"].AsList(StarhermitRelayParticipant.Read);
        }

        /// <summary>Relay id, used when connecting the relay socket.</summary>
        public Guid Id { get; }

        /// <summary>The catalog title the relay belongs to.</summary>
        public Guid TitleId { get; }

        /// <summary>Who created it.</summary>
        public Guid CreatorUserId { get; }

        /// <summary>The game session that authorises the roster, when it is bound to one.</summary>
        public Guid? GameSessionId { get; }

        /// <summary>The realtime room that authorises the roster, when it is bound to one.</summary>
        public Guid? RealtimeRoomId { get; }

        /// <summary>Participant limit.</summary>
        public int MaxParticipants { get; }

        /// <summary>How many are connected now.</summary>
        public int CurrentParticipantCount { get; }

        /// <summary>Session status.</summary>
        public string Status { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it closed.</summary>
        public DateTimeOffset? ClosedAt { get; }

        /// <summary>The roster.</summary>
        public IReadOnlyList<StarhermitRelayParticipant> Participants { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRelaySession Read(JsonValue json) => new StarhermitRelaySession(json);
    }

    /// <summary>Someone on a relay's roster.</summary>
    public sealed class StarhermitRelayParticipant : StarhermitModel
    {
        private StarhermitRelayParticipant(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            SessionId = json["sessionId"].AsGuidOrNull() ?? Guid.Empty;
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            JoinedAt = json["joinedAt"].AsDateTimeOffsetOrNull();
            LeftAt = json["leftAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Participant row id.</summary>
        public Guid Id { get; }

        /// <summary>The relay they belong to.</summary>
        public Guid SessionId { get; }

        /// <summary>Their account id.</summary>
        public Guid UserId { get; }

        /// <summary>Participation status.</summary>
        public string Status { get; }

        /// <summary>When they joined.</summary>
        public DateTimeOffset? JoinedAt { get; }

        /// <summary>When they left.</summary>
        public DateTimeOffset? LeftAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRelayParticipant Read(JsonValue json) => new StarhermitRelayParticipant(json);
    }

    /// <summary>Phases a realtime room moves through.</summary>
    public static class StarhermitRoomStatuses
    {
        /// <summary>Assembling: seats are being filled.</summary>
        public const string Lobby = "lobby";

        /// <summary>Open for backfill.</summary>
        public const string Open = "open";

        /// <summary>The match has started.</summary>
        public const string Started = "started";

        /// <summary>The room has closed.</summary>
        public const string Closed = "closed";
    }
}
