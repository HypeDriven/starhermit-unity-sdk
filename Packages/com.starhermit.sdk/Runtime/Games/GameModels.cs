using System;
using System.Collections.Generic;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>A player as a game session refers to them.</summary>
    public sealed class StarhermitGamePlayer : StarhermitModel
    {
        private StarhermitGamePlayer(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
        }

        /// <summary>Their account id.</summary>
        public Guid UserId { get; }

        /// <summary>Their username.</summary>
        public string Username { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGamePlayer Read(JsonValue json) => new StarhermitGamePlayer(json);
    }

    /// <summary>
    /// The caller's standing in one game: rating and record, as the platform maintains them.
    /// </summary>
    /// <remarks>
    /// This is game player state, written by the game's server-side logic. A client reads it; it never
    /// writes it, and never recomputes a rating locally.
    /// </remarks>
    public sealed class StarhermitGameStanding : StarhermitModel
    {
        private StarhermitGameStanding(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Elo = json["elo"].AsDecimalOrNull() ?? 0m;
            Wins = json["wins"].AsInt64OrDefault();
            Losses = json["losses"].AsInt64OrDefault();
            Draws = json["draws"].AsInt64OrDefault();
            ActiveSessionCount = json["activeSessionCount"].AsInt32OrDefault();
        }

        /// <summary>The account the standing belongs to.</summary>
        public Guid UserId { get; }

        /// <summary>Current rating.</summary>
        public decimal Elo { get; }

        /// <summary>Wins recorded.</summary>
        public long Wins { get; }

        /// <summary>Losses recorded.</summary>
        public long Losses { get; }

        /// <summary>Draws recorded.</summary>
        public long Draws { get; }

        /// <summary>How many sessions the player currently has open.</summary>
        public int ActiveSessionCount { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameStanding Read(JsonValue json) => new StarhermitGameStanding(json);
    }

    /// <summary>A game's metadata and effective capabilities.</summary>
    public sealed class StarhermitGameInfo : StarhermitModel
    {
        private StarhermitGameInfo(JsonValue json) : base(json)
        {
            Slug = json["slug"].AsStringOrNull() ?? string.Empty;
            Name = json["name"].AsStringOrNull() ?? string.Empty;
            IsEnabled = json["enabled"].AsBooleanOrDefault();
            LeaderboardId = json["leaderboardId"].AsGuidOrNull();
            MaxConcurrentSessionsPerPlayer = json["maxConcurrentSessionsPerPlayer"].AsInt32OrDefault();
            ReplaysEnabled = json["replaysEnabled"].AsBooleanOrDefault();
            Me = json["me"].IsObject ? StarhermitGameStanding.Read(json["me"]) : null;
        }

        /// <summary>The game's slug.</summary>
        public string Slug { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Whether the game accepts new sessions.</summary>
        public bool IsEnabled { get; }

        /// <summary>The leaderboard the game feeds, when it has one.</summary>
        public Guid? LeaderboardId { get; }

        /// <summary>How many sessions one player may have open at once.</summary>
        public int MaxConcurrentSessionsPerPlayer { get; }

        /// <summary>Whether replays are recorded and readable.</summary>
        public bool ReplaysEnabled { get; }

        /// <summary>The caller's standing in this game.</summary>
        public StarhermitGameStanding? Me { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameInfo Read(JsonValue json) => new StarhermitGameInfo(json);
    }

    /// <summary>A session in the caller's list.</summary>
    public sealed class StarhermitGameSessionSummary : StarhermitModel
    {
        private StarhermitGameSessionSummary(JsonValue json) : base(json)
        {
            SessionId = json["sessionId"].AsGuidOrNull() ?? Guid.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            Players = json["players"].AsList(StarhermitGamePlayer.Read);
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            FinishedAt = json["finishedAt"].AsDateTimeOffsetOrNull();
            IsMyTurn = json["myTurn"].AsBooleanOrNull();
            DeadlineUnixMilliseconds = json["deadline"].AsInt64OrNull();
        }

        /// <summary>Session id.</summary>
        public Guid SessionId { get; }

        /// <summary>Session status - see <see cref="StarhermitSessionStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>Everyone in the session.</summary>
        public IReadOnlyList<StarhermitGamePlayer> Players { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it finished.</summary>
        public DateTimeOffset? FinishedAt { get; }

        /// <summary>Whether the caller is to move, for a turn-based game.</summary>
        public bool? IsMyTurn { get; }

        /// <summary>Move deadline as Unix milliseconds, when the game sets one.</summary>
        public long? DeadlineUnixMilliseconds { get; }

        /// <summary>The deadline as a timestamp.</summary>
        public DateTimeOffset? Deadline =>
            DeadlineUnixMilliseconds.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(DeadlineUnixMilliseconds.Value)
                : (DateTimeOffset?)null;

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameSessionSummary Read(JsonValue json) => new StarhermitGameSessionSummary(json);
    }

    /// <summary>One session in detail.</summary>
    public sealed class StarhermitGameSession : StarhermitModel
    {
        private StarhermitGameSession(JsonValue json) : base(json)
        {
            SessionId = json["sessionId"].AsGuidOrNull() ?? Guid.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            Players = json["players"].AsList(StarhermitGamePlayer.Read);
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            FinishedAt = json["finishedAt"].AsDateTimeOffsetOrNull();
            ChatConversationId = json["chatConversationId"].AsGuidOrNull();
            Result = json["result"];
        }

        /// <summary>Session id.</summary>
        public Guid SessionId { get; }

        /// <summary>Session status.</summary>
        public string Status { get; }

        /// <summary>Everyone in the session.</summary>
        public IReadOnlyList<StarhermitGamePlayer> Players { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it finished.</summary>
        public DateTimeOffset? FinishedAt { get; }

        /// <summary>The chat conversation attached to the session, when there is one.</summary>
        public Guid? ChatConversationId { get; }

        /// <summary>
        /// The game's own result document, whose shape the game defines. The SDK passes it through
        /// untouched rather than guessing at a schema it does not own.
        /// </summary>
        public JsonValue Result { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameSession Read(JsonValue json) => new StarhermitGameSession(json);
    }

    /// <summary>A matchmaking ticket.</summary>
    public sealed class StarhermitMatchmakingTicket : StarhermitModel
    {
        private StarhermitMatchmakingTicket(JsonValue json) : base(json)
        {
            TicketId = json["ticketId"].AsGuidOrNull() ?? Guid.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            SessionId = json["sessionId"].AsGuidOrNull();
        }

        /// <summary>Ticket id.</summary>
        public Guid TicketId { get; }

        /// <summary>Ticket status - see <see cref="StarhermitMatchmakingStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>The session the ticket matched into, once it has.</summary>
        public Guid? SessionId { get; }

        /// <summary>True once a session exists for this ticket.</summary>
        public bool IsMatched => SessionId.HasValue;

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitMatchmakingTicket Read(JsonValue json) => new StarhermitMatchmakingTicket(json);
    }

    /// <summary>An invitation to play a game.</summary>
    public sealed class StarhermitGameInvite : StarhermitModel
    {
        private StarhermitGameInvite(JsonValue json) : base(json)
        {
            InviteId = json["inviteId"].AsGuidOrNull() ?? Guid.Empty;
            From = json["from"].IsObject ? StarhermitGamePlayer.Read(json["from"]) : null;
            To = json["to"].IsObject ? StarhermitGamePlayer.Read(json["to"]) : null;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            SessionId = json["sessionId"].AsGuidOrNull();
        }

        /// <summary>Invite id.</summary>
        public Guid InviteId { get; }

        /// <summary>Who sent it.</summary>
        public StarhermitGamePlayer? From { get; }

        /// <summary>Who it was sent to.</summary>
        public StarhermitGamePlayer? To { get; }

        /// <summary>Invite status.</summary>
        public string Status { get; }

        /// <summary>When it was sent.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>The session created by accepting it.</summary>
        public Guid? SessionId { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameInvite Read(JsonValue json) => new StarhermitGameInvite(json);
    }

    /// <summary>The invites addressed to and sent by the caller for one game.</summary>
    public sealed class StarhermitGameInviteLists : StarhermitModel
    {
        private StarhermitGameInviteLists(JsonValue json) : base(json)
        {
            Incoming = json["incoming"].AsList(StarhermitGameInvite.Read);
            Outgoing = json["outgoing"].AsList(StarhermitGameInvite.Read);
        }

        /// <summary>Invites waiting for the caller's answer.</summary>
        public IReadOnlyList<StarhermitGameInvite> Incoming { get; }

        /// <summary>Invites the caller has sent.</summary>
        public IReadOnlyList<StarhermitGameInvite> Outgoing { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameInviteLists Read(JsonValue json) => new StarhermitGameInviteLists(json);
    }

    /// <summary>
    /// A pending invitation from any game or room, with the routes that answer it.
    /// </summary>
    /// <remarks>
    /// <see cref="AcceptPath"/> and <see cref="DeclinePath"/> come from the server so a notification
    /// UI can answer an invite without knowing which subsystem raised it.
    /// </remarks>
    public sealed class StarhermitInviteNotification : StarhermitModel
    {
        private StarhermitInviteNotification(JsonValue json) : base(json)
        {
            InviteId = json["inviteId"].AsGuidOrNull() ?? Guid.Empty;
            Kind = json["kind"].AsStringOrNull() ?? string.Empty;
            GameSlug = json["gameSlug"].AsStringOrNull() ?? string.Empty;
            GameName = json["gameName"].AsStringOrNull() ?? string.Empty;
            RoomId = json["roomId"].AsGuidOrNull();
            From = json["from"].IsObject ? StarhermitGamePlayer.Read(json["from"]) : null;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            AcceptPath = json["acceptPath"].AsStringOrNull() ?? string.Empty;
            DeclinePath = json["declinePath"].AsStringOrNull() ?? string.Empty;
        }

        /// <summary>Invite id.</summary>
        public Guid InviteId { get; }

        /// <summary>Which subsystem the invite came from.</summary>
        public string Kind { get; }

        /// <summary>The game it concerns.</summary>
        public string GameSlug { get; }

        /// <summary>That game's display name.</summary>
        public string GameName { get; }

        /// <summary>The room, for a realtime-room invite.</summary>
        public Guid? RoomId { get; }

        /// <summary>Who sent it.</summary>
        public StarhermitGamePlayer? From { get; }

        /// <summary>When it was sent.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>API path that accepts this invite.</summary>
        public string AcceptPath { get; }

        /// <summary>API path that declines it.</summary>
        public string DeclinePath { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitInviteNotification Read(JsonValue json) => new StarhermitInviteNotification(json);
    }

    /// <summary>A replay in the caller's list.</summary>
    public sealed class StarhermitReplaySummary : StarhermitModel
    {
        private StarhermitReplaySummary(JsonValue json) : base(json)
        {
            SessionId = json["sessionId"].AsGuidOrNull() ?? Guid.Empty;
            Players = json["players"].AsList(StarhermitGamePlayer.Read);
            FinishedAt = json["finishedAt"].AsDateTimeOffsetOrNull();
            Result = json["result"];
            MoveCount = json["moveCount"].AsInt32OrDefault();
        }

        /// <summary>The session the replay records.</summary>
        public Guid SessionId { get; }

        /// <summary>Who played.</summary>
        public IReadOnlyList<StarhermitGamePlayer> Players { get; }

        /// <summary>When the session finished.</summary>
        public DateTimeOffset? FinishedAt { get; }

        /// <summary>The game's result document.</summary>
        public JsonValue Result { get; }

        /// <summary>How many moves the replay holds.</summary>
        public int MoveCount { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitReplaySummary Read(JsonValue json) => new StarhermitReplaySummary(json);
    }

    /// <summary>A full replay, including the recorded state the game interprets.</summary>
    public sealed class StarhermitReplay : StarhermitModel
    {
        private StarhermitReplay(JsonValue json) : base(json)
        {
            SessionId = json["sessionId"].AsGuidOrNull() ?? Guid.Empty;
            Players = json["players"].AsList(StarhermitGamePlayer.Read);
            FinishedAt = json["finishedAt"].AsDateTimeOffsetOrNull();
            Result = json["result"];
            State = json["state"];
        }

        /// <summary>The session the replay records.</summary>
        public Guid SessionId { get; }

        /// <summary>Who played.</summary>
        public IReadOnlyList<StarhermitGamePlayer> Players { get; }

        /// <summary>When the session finished.</summary>
        public DateTimeOffset? FinishedAt { get; }

        /// <summary>The game's result document.</summary>
        public JsonValue Result { get; }

        /// <summary>The recorded state, in whatever shape the game writes.</summary>
        public JsonValue State { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitReplay Read(JsonValue json) => new StarhermitReplay(json);
    }

    /// <summary>An achievement as one game reports it, including whether the caller holds it.</summary>
    public sealed class StarhermitGameAchievement : StarhermitModel
    {
        private StarhermitGameAchievement(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Key = json["key"].AsStringOrNull() ?? string.Empty;
            Name = json["name"].AsStringOrNull() ?? string.Empty;
            Description = json["description"].AsStringOrNull() ?? string.Empty;
            Icon = json["icon"].AsStringOrNull();
            IsSecret = json["secret"].AsBooleanOrDefault();
            Points = json["points"].AsInt32OrDefault();
            IsUnlocked = json["unlocked"].AsBooleanOrDefault();
            UnlockedAt = json["unlockedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Definition id.</summary>
        public Guid Id { get; }

        /// <summary>Stable key.</summary>
        public string Key { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Description.</summary>
        public string Description { get; }

        /// <summary>Icon reference.</summary>
        public string? Icon { get; }

        /// <summary>True when hidden until unlocked.</summary>
        public bool IsSecret { get; }

        /// <summary>Point value.</summary>
        public int Points { get; }

        /// <summary>True when the caller has unlocked it.</summary>
        public bool IsUnlocked { get; }

        /// <summary>When they unlocked it.</summary>
        public DateTimeOffset? UnlockedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameAchievement Read(JsonValue json) => new StarhermitGameAchievement(json);
    }

    /// <summary>One declared control action and the codes bound to it.</summary>
    public sealed class StarhermitControlAction : StarhermitModel
    {
        private StarhermitControlAction(JsonValue json) : base(json)
        {
            Action = json["action"].AsStringOrNull() ?? string.Empty;
            Label = json["label"].AsStringOrNull() ?? string.Empty;
            DefaultCodes = json["defaultCodes"].AsList(value => value.AsStringOrNull() ?? string.Empty);
            Codes = json["codes"].AsList(value => value.AsStringOrNull() ?? string.Empty);
        }

        /// <summary>The action's stable name.</summary>
        public string Action { get; }

        /// <summary>Label to show a player.</summary>
        public string Label { get; }

        /// <summary>Codes the game declared as defaults.</summary>
        public IReadOnlyList<string> DefaultCodes { get; }

        /// <summary>Codes currently bound, after the player's overrides.</summary>
        public IReadOnlyList<string> Codes { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitControlAction Read(JsonValue json) => new StarhermitControlAction(json);
    }

    /// <summary>A game's control bindings for the caller.</summary>
    public sealed class StarhermitGameControls : StarhermitModel
    {
        private StarhermitGameControls(JsonValue json) : base(json)
        {
            Actions = json["actions"].AsList(StarhermitControlAction.Read);
        }

        /// <summary>Every declared action with its bindings.</summary>
        public IReadOnlyList<StarhermitControlAction> Actions { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameControls Read(JsonValue json) => new StarhermitGameControls(json);
    }

    /// <summary>The server's budget for a player's settings document.</summary>
    /// <remarks>
    /// Read from the deployment rather than hard-coded, so a game shows the limit that is actually in
    /// force rather than the one that was in force when the SDK shipped.
    /// </remarks>
    public sealed class StarhermitSettingsLimits : StarhermitModel
    {
        private StarhermitSettingsLimits(JsonValue json) : base(json)
        {
            MaxKeys = json["maxKeys"].AsInt32OrDefault();
            MaxKeyLength = json["maxKeyLength"].AsInt32OrDefault();
            MaxTotalBytes = json["maxTotalBytes"].AsInt64OrDefault();
        }

        /// <summary>Most keys the document may hold.</summary>
        public int MaxKeys { get; }

        /// <summary>Longest key name accepted.</summary>
        public int MaxKeyLength { get; }

        /// <summary>Largest total size accepted.</summary>
        public long MaxTotalBytes { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitSettingsLimits Read(JsonValue json) => new StarhermitSettingsLimits(json);
    }

    /// <summary>
    /// A player's settings document for one game: schema-free JSON the platform stores and never
    /// interprets.
    /// </summary>
    public sealed class StarhermitGameSettings : StarhermitModel
    {
        private StarhermitGameSettings(JsonValue json) : base(json)
        {
            Slug = json["slug"].AsStringOrNull() ?? string.Empty;
            Settings = json["settings"].AsDictionary(value => value);
            Count = json["count"].AsInt32OrDefault();
            Bytes = json["bytes"].AsInt64OrDefault();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
            Limits = json["limits"].IsObject ? StarhermitSettingsLimits.Read(json["limits"]) : null;
        }

        /// <summary>The game the document belongs to.</summary>
        public string Slug { get; }

        /// <summary>The stored values, keyed as the game wrote them.</summary>
        public IReadOnlyDictionary<string, JsonValue> Settings { get; }

        /// <summary>How many keys are stored.</summary>
        public int Count { get; }

        /// <summary>How many bytes the document occupies.</summary>
        public long Bytes { get; }

        /// <summary>When it was last written.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>The server's current budget for this document.</summary>
        public StarhermitSettingsLimits? Limits { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameSettings Read(JsonValue json) => new StarhermitGameSettings(json);
    }

    /// <summary>One key of a player's settings document.</summary>
    public sealed class StarhermitGameSetting : StarhermitModel
    {
        private StarhermitGameSetting(JsonValue json) : base(json)
        {
            Key = json["key"].AsStringOrNull() ?? string.Empty;
            Value = json["value"];
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>The key.</summary>
        public string Key { get; }

        /// <summary>The stored value, in whatever shape the game wrote.</summary>
        public JsonValue Value { get; }

        /// <summary>When it was last written.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameSetting Read(JsonValue json) => new StarhermitGameSetting(json);
    }

    /// <summary>Session statuses the games API reports.</summary>
    public static class StarhermitSessionStatuses
    {
        /// <summary>The session is in progress.</summary>
        public const string Active = "active";

        /// <summary>The session has ended.</summary>
        public const string Finished = "finished";

        /// <summary>The session was abandoned.</summary>
        public const string Abandoned = "abandoned";
    }

    /// <summary>Matchmaking ticket statuses.</summary>
    public static class StarhermitMatchmakingStatuses
    {
        /// <summary>Waiting for an opponent.</summary>
        public const string Waiting = "waiting";

        /// <summary>Matched into a session.</summary>
        public const string Matched = "matched";

        /// <summary>Cancelled by the player.</summary>
        public const string Cancelled = "cancelled";
    }
}
