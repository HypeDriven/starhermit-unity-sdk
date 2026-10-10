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
            BackfillAiPlayers = json["backfillAiPlayers"].AsInt32OrNull();
            JoinInProgress = json["joinInProgress"].AsBooleanOrDefault();
            Metadata = json["metadata"];
        }

        /// <summary>
        /// Most empty seats the start-time backfill may give to AI players; null fills every empty
        /// seat, 0 starts the match with empty seats left empty.
        /// </summary>
        public int? BackfillAiPlayers { get; }

        /// <summary>
        /// True when the room keeps taking players after its match starts: a seat given up is vacated
        /// rather than handed to an AI, and a newcomer is admitted to the running session.
        /// </summary>
        public bool JoinInProgress { get; }

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
            Name = json["name"].AsStringOrNull();
            JoinCode = json["joinCode"].AsStringOrNull();
            IsVisible = json["isVisible"].AsBooleanOrDefault();
            Revision = json["revision"].AsInt32OrDefault();
        }

        /// <summary>The name the host gave the room, if any.</summary>
        public string? Name { get; }

        /// <summary>
        /// The code that takes a seat in this room - shown only to people already in it. It is a
        /// capability: share it with the players you mean to invite, and never log it.
        /// </summary>
        public string? JoinCode { get; }

        /// <summary>True when the room is listed in the room browser.</summary>
        public bool IsVisible { get; }

        /// <summary>
        /// The configuration's version. Pass it to <see cref="StarhermitRoomUpdate.ExpectedRevision"/>
        /// so a concurrent change is refused rather than overwritten. The roster does not change it.
        /// </summary>
        public int Revision { get; }

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

    /// <summary>
    /// One row of the room browser. Carries no join code and no roster: a listing is readable by
    /// anyone who can see the game.
    /// </summary>
    public sealed class StarhermitRoomSummary : StarhermitModel
    {
        private StarhermitRoomSummary(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            GameSlug = json["gameSlug"].AsStringOrNull() ?? string.Empty;
            Name = json["name"].AsStringOrNull();
            HostUsername = json["hostUsername"].AsStringOrNull() ?? string.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            Players = json["players"].AsInt32OrDefault();
            Capacity = json["capacity"].AsInt32OrDefault();
            FreeSeats = json["freeSeats"].AsInt32OrDefault();
            Metadata = json["metadata"];
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            OpenedAt = json["openedAt"].AsDateTimeOffsetOrNull();
            StartedAt = json["startedAt"].AsDateTimeOffsetOrNull();
            JoinInProgress = json["joinInProgress"].AsBooleanOrDefault();
        }

        /// <summary>Room id - join it with <see cref="StarhermitRealtimeRoomsClient.QuickJoinAsync"/> or an invite.</summary>
        public Guid Id { get; }

        /// <summary>The game the room is for.</summary>
        public string GameSlug { get; }

        /// <summary>The room's name, if the host gave it one.</summary>
        public string? Name { get; }

        /// <summary>Who hosts it.</summary>
        public string HostUsername { get; }

        /// <summary>Room phase - see <see cref="StarhermitRoomStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>Seats taken.</summary>
        public int Players { get; }

        /// <summary>Seats in all.</summary>
        public int Capacity { get; }

        /// <summary>Seats a newcomer could take.</summary>
        public int FreeSeats { get; }

        /// <summary>Room metadata, in whatever shape the game defines.</summary>
        public JsonValue Metadata { get; }

        /// <summary>When the room was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it was opened.</summary>
        public DateTimeOffset? OpenedAt { get; }

        /// <summary>When its match started, for a running match that still takes players.</summary>
        public DateTimeOffset? StartedAt { get; }

        /// <summary>True when the room takes players mid-match.</summary>
        public bool JoinInProgress { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRoomSummary Read(JsonValue json) => new StarhermitRoomSummary(json);
    }

    /// <summary>Changes a host makes to a room's configuration. Unset members are left alone.</summary>
    public sealed class StarhermitRoomUpdate
    {
        /// <summary>New name; an empty string clears it.</summary>
        public Optional<string> Name { get; set; }

        /// <summary>Whether the room is listed in the room browser.</summary>
        public Optional<bool> IsVisible { get; set; }

        /// <summary>Replacement metadata, in whatever shape the game defines.</summary>
        public Optional<JsonValue> Metadata { get; set; }

        /// <summary>Most empty seats the start-time backfill may give to AI players.</summary>
        public Optional<int> BackfillAiPlayers { get; set; }

        /// <summary>True to go back to filling every empty seat with AI at start.</summary>
        public bool BackfillAllEmptySeats { get; set; }

        /// <summary>Whether the room keeps taking players after its match starts.</summary>
        public Optional<bool> JoinInProgress { get; set; }

        /// <summary>
        /// The <see cref="StarhermitRoom.Revision"/> the change was made against. When set, a change
        /// someone else made in between answers <see cref="StarhermitConflictException"/> instead of
        /// one of the two edits vanishing.
        /// </summary>
        public int? ExpectedRevision { get; set; }

        /// <summary>Writes the update as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (Name.IsSet) writer.Write("name", Name.Value);
            if (IsVisible.IsSet) writer.Write("isVisible", IsVisible.Value);
            if (Metadata.IsSet) writer.Write("metadata", Metadata.Value ?? JsonValue.Null);
            if (BackfillAiPlayers.IsSet) writer.Write("backfillAiPlayers", BackfillAiPlayers.Value);
            if (BackfillAllEmptySeats) writer.Write("backfillAllEmptySeats", true);
            if (JoinInProgress.IsSet) writer.Write("joinInProgress", JoinInProgress.Value);
            writer.WriteIfPresent("expectedRevision", ExpectedRevision);
        }
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
