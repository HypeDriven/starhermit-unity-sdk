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
            PausedAt = json["pausedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>
        /// When a persistent session was paused because everyone left; null while it runs. Joining
        /// resumes it.
        /// </summary>
        public DateTimeOffset? PausedAt { get; }

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
            WaitedSeconds = json["waitedSeconds"].AsInt32OrDefault();
            SearchEloBand = json["searchEloBand"].AsDecimalOrNull() ?? 0;
            MaxWaitSeconds = json["maxWaitSeconds"].AsInt32OrDefault();
        }

        /// <summary>How long the ticket has been searching.</summary>
        public int WaitedSeconds { get; }

        /// <summary>How far either side of the player's rating the search has widened - what a client shows as "expanding".</summary>
        public decimal SearchEloBand { get; }

        /// <summary>How long a search may wait before it expires.</summary>
        public int MaxWaitSeconds { get; }

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

    /// <summary>One shape of match a game accepts, from <c>game.queues</c> or the implicit 1v1.</summary>
    public sealed class StarhermitGameQueue : StarhermitModel
    {
        private StarhermitGameQueue(JsonValue json) : base(json)
        {
            Key = json["key"].AsStringOrNull() ?? string.Empty;
            Teams = json["teams"].AsInt32OrDefault();
            TeamSize = json["teamSize"].AsInt32OrDefault();
            Players = json["players"].AsInt32OrDefault();
        }

        /// <summary>The key to name when entering matchmaking for this shape.</summary>
        public string Key { get; }

        /// <summary>How many teams a match of this shape has.</summary>
        public int Teams { get; }

        /// <summary>Players per team. A party fills exactly one team, so a party larger than this cannot queue for it.</summary>
        public int TeamSize { get; }

        /// <summary>Players in the whole match.</summary>
        public int Players { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameQueue Read(JsonValue json) => new StarhermitGameQueue(json);
    }

    /// <summary>What the caller has unlocked in another game.</summary>
    /// <remarks>
    /// For opening features earned elsewhere - a sequel honouring the original. A client can lie about
    /// what it read, so anything the platform must enforce belongs in the game's server logic, which
    /// receives the same data as <c>ctx.linkedAchievements</c>.
    /// </remarks>
    public sealed class StarhermitLinkedAchievements : StarhermitModel
    {
        private StarhermitLinkedAchievements(JsonValue json) : base(json)
        {
            Game = json["game"].AsStringOrNull() ?? string.Empty;
            IsHidden = json["hidden"].AsBooleanOrDefault();
            Unlocked = json["unlocked"].AsList(StarhermitLinkedAchievement.Read);
        }

        /// <summary>The other game's slug.</summary>
        public string Game { get; }

        /// <summary>
        /// True when the player keeps their achievements private. <see cref="Unlocked"/> is then empty
        /// and says nothing about what they have - treat it as unknown, not as none.
        /// </summary>
        public bool IsHidden { get; }

        /// <summary>The other game's achievements the player holds.</summary>
        public IReadOnlyList<StarhermitLinkedAchievement> Unlocked { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitLinkedAchievements Read(JsonValue json) => new StarhermitLinkedAchievements(json);
    }

    /// <summary>One achievement unlocked in another game.</summary>
    public sealed class StarhermitLinkedAchievement : StarhermitModel
    {
        private StarhermitLinkedAchievement(JsonValue json) : base(json)
        {
            Key = json["key"].AsStringOrNull() ?? string.Empty;
            UnlockedAt = json["unlockedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>The achievement's key in the other game.</summary>
        public string Key { get; }

        /// <summary>When it was unlocked.</summary>
        public DateTimeOffset? UnlockedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitLinkedAchievement Read(JsonValue json) => new StarhermitLinkedAchievement(json);
    }

    /// <summary>
    /// What it would take to bring a session back, measured: the checkpoint the platform holds against
    /// its budget, how fresh it is, the limits a restore is judged by, and the transport budgets the
    /// session plays under. Container-only members are null for a script game, whose state is written
    /// on every change rather than checkpointed.
    /// </summary>
    public sealed class StarhermitSessionRecovery : StarhermitModel
    {
        private StarhermitSessionRecovery(JsonValue json) : base(json)
        {
            SessionId = json["sessionId"].AsGuidOrNull() ?? Guid.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            Runtime = json["runtime"].AsStringOrNull() ?? string.Empty;
            IsPersistent = json["persistent"].AsBooleanOrDefault();
            PausedAt = json["pausedAt"].AsDateTimeOffsetOrNull();
            PausedMilliseconds = json["pausedMillis"].AsInt64OrDefault();
            SnapshotBytes = json["snapshotBytes"].AsInt64OrDefault();
            StateBudgetBytes = json["stateBudgetBytes"].AsInt64OrDefault();
            LastSnapshotAt = json["lastSnapshotAt"].AsDateTimeOffsetOrNull();
            SnapshotIntervalSeconds = json["snapshotIntervalSeconds"].AsDoubleOrNull();
            MaxSnapshotPushHz = json["maxSnapshotPushHz"].AsDoubleOrNull();
            MaxRestoreStalenessSeconds = json["maxRestoreStalenessSeconds"].AsInt32OrNull();
            MaxRestores = json["maxRestores"].AsInt32OrDefault();
            RestoreWindowMinutes = json["restoreWindowMinutes"].AsInt32OrDefault();
            RestoreCount = json["restoreCount"].AsInt32OrDefault();
            RetentionDays = json["retentionDays"].AsInt32OrDefault();
            RecoveryDataExpiresAt = json["recoveryDataExpiresAt"].AsDateTimeOffsetOrNull();
            Transport = json["transport"].IsObject ? StarhermitTransportBudget.Read(json["transport"]) : null;
        }

        /// <summary>Session id.</summary>
        public Guid SessionId { get; }

        /// <summary>Session status - see <see cref="StarhermitSessionStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>The runtime hosting it: <c>script</c> or <c>container</c>.</summary>
        public string Runtime { get; }

        /// <summary>True for a persistent world, which is paused rather than ended when it empties.</summary>
        public bool IsPersistent { get; }

        /// <summary>When the session was paused, while it is.</summary>
        public DateTimeOffset? PausedAt { get; }

        /// <summary>Total time the session has spent paused.</summary>
        public long PausedMilliseconds { get; }

        /// <summary>Size of the checkpoint the platform holds.</summary>
        public long SnapshotBytes { get; }

        /// <summary>The budget that checkpoint must fit.</summary>
        public long StateBudgetBytes { get; }

        /// <summary>When the checkpoint was taken.</summary>
        public DateTimeOffset? LastSnapshotAt { get; }

        /// <summary>How often the platform checkpoints a container session; null for a script game.</summary>
        public double? SnapshotIntervalSeconds { get; }

        /// <summary>Most snapshots per second a container may push; null for a script game.</summary>
        public double? MaxSnapshotPushHz { get; }

        /// <summary>Oldest checkpoint a crash may be restored from; null for a script game.</summary>
        public int? MaxRestoreStalenessSeconds { get; }

        /// <summary>Most restores allowed within <see cref="RestoreWindowMinutes"/>.</summary>
        public int MaxRestores { get; }

        /// <summary>The window restores are counted over.</summary>
        public int RestoreWindowMinutes { get; }

        /// <summary>Restores so far within the window.</summary>
        public int RestoreCount { get; }

        /// <summary>How long a finished session's recovery data is kept.</summary>
        public int RetentionDays { get; }

        /// <summary>When this session's recovery data will be deleted, once it has finished.</summary>
        public DateTimeOffset? RecoveryDataExpiresAt { get; }

        /// <summary>Per-connection limits on the game socket and the rates the session runs at.</summary>
        public StarhermitTransportBudget? Transport { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitSessionRecovery Read(JsonValue json) => new StarhermitSessionRecovery(json);
    }

    /// <summary>The game socket's per-connection limits, and the rates a session runs at.</summary>
    public sealed class StarhermitTransportBudget : StarhermitModel
    {
        private StarhermitTransportBudget(JsonValue json) : base(json)
        {
            MaxMessageBytes = json["maxMessageBytes"].AsInt32OrDefault();
            MaxRealtimeInputsPerSecond = json["maxRealtimeInputsPerSecond"].AsInt32OrDefault();
            TickRateHz = json["tickRateHz"].AsDoubleOrNull() ?? 0;
            PacingRateHz = json["pacingRateHz"].AsDoubleOrNull() ?? 0;
        }

        /// <summary>Largest frame the socket accepts.</summary>
        public int MaxMessageBytes { get; }

        /// <summary>Most realtime inputs a player may send per second.</summary>
        public int MaxRealtimeInputsPerSecond { get; }

        /// <summary>The rate the platform ticks the session at; 0 means never.</summary>
        public double TickRateHz { get; }

        /// <summary>The rate per-sender message budgets are sized from.</summary>
        public double PacingRateHz { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitTransportBudget Read(JsonValue json) => new StarhermitTransportBudget(json);
    }

    /// <summary>A crash or bug report a player files for a game.</summary>
    /// <remarks>
    /// Every axis is bounded by limits the platform's operators set per game - reports per player per
    /// day (<c>429</c> with <c>Retry-After</c>), description length, attachment count and total bytes
    /// (<c>413</c>). Attachments are for logs and screenshots the player chose to send; never attach
    /// tokens, keys or anything the player did not see.
    /// </remarks>
    public sealed class StarhermitPlayerReport
    {
        private readonly List<StarhermitReportAttachment> _attachments = new List<StarhermitReportAttachment>();

        /// <summary>Creates a report.</summary>
        /// <param name="kind">See <see cref="StarhermitReportKinds"/>.</param>
        /// <param name="title">One line saying what went wrong, at most 200 characters.</param>
        public StarhermitPlayerReport(string kind, string title)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Title = title ?? throw new ArgumentNullException(nameof(title));
        }

        /// <summary>See <see cref="StarhermitReportKinds"/>.</summary>
        public string Kind { get; }

        /// <summary>One line saying what went wrong.</summary>
        public string Title { get; }

        /// <summary>What the player was doing and what happened.</summary>
        public string? Description { get; set; }

        /// <summary>The game client's version.</summary>
        public string? ClientVersion { get; set; }

        /// <summary>The platform the game was running on.</summary>
        public string? Platform { get; set; }

        /// <summary>The browser's user agent, for a browser game.</summary>
        public string? UserAgent { get; set; }

        /// <summary>The build the player was running, as the game info's build id reported it.</summary>
        public string? BuildId { get; set; }

        /// <summary>The session the problem happened in, when there was one.</summary>
        public Guid? SessionId { get; set; }

        /// <summary>Files attached so far.</summary>
        public IReadOnlyList<StarhermitReportAttachment> Attachments => _attachments;

        /// <summary>Attaches a file.</summary>
        /// <param name="fileName">Name shown to the game's owner.</param>
        /// <param name="contentType">Media type, e.g. <c>text/plain</c> or <c>image/png</c>.</param>
        /// <param name="data">The file's bytes.</param>
        /// <returns>This report, for chaining.</returns>
        public StarhermitPlayerReport Attach(string fileName, string contentType, byte[] data)
        {
            _attachments.Add(new StarhermitReportAttachment(fileName, contentType, data));
            return this;
        }

        /// <summary>Writes the report as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.Write("kind", Kind);
            writer.Write("title", Title);
            writer.WriteIfPresent("description", Description);
            writer.WriteIfPresent("clientVersion", ClientVersion);
            writer.WriteIfPresent("platform", Platform);
            writer.WriteIfPresent("userAgent", UserAgent);
            writer.WriteIfPresent("buildId", BuildId);
            writer.WriteIfPresent("sessionId", SessionId);
            if (_attachments.Count == 0) return;
            writer.WriteArray("attachments", _attachments, (w, attachment) =>
            {
                w.WriteStartObject();
                w.Write("fileName", attachment.FileName);
                w.Write("contentType", attachment.ContentType);
                w.Write("dataBase64", Convert.ToBase64String(attachment.Data));
                w.WriteEndObject();
            });
        }
    }

    /// <summary>A file attached to a player report.</summary>
    public sealed class StarhermitReportAttachment
    {
        /// <summary>Creates an attachment.</summary>
        /// <param name="fileName">Name shown to the game's owner.</param>
        /// <param name="contentType">Media type.</param>
        /// <param name="data">The file's bytes.</param>
        public StarhermitReportAttachment(string fileName, string contentType, byte[] data)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>Name shown to the game's owner.</summary>
        public string FileName { get; }

        /// <summary>Media type.</summary>
        public string ContentType { get; }

        /// <summary>The file's bytes.</summary>
        public byte[] Data { get; }
    }

    /// <summary>The platform's receipt for a filed report.</summary>
    public sealed class StarhermitReportReceipt : StarhermitModel
    {
        private StarhermitReportReceipt(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Kind = json["kind"].AsStringOrNull() ?? string.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Report id - quote it to the game's developer.</summary>
        public Guid Id { get; }

        /// <summary>See <see cref="StarhermitReportKinds"/>.</summary>
        public string Kind { get; }

        /// <summary>See <see cref="StarhermitReportStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>When it was filed.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitReportReceipt Read(JsonValue json) => new StarhermitReportReceipt(json);
    }

    /// <summary>One of the caller's own reports, and how far the developer has got with it.</summary>
    public sealed class StarhermitMyReport : StarhermitModel
    {
        private StarhermitMyReport(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Kind = json["kind"].AsStringOrNull() ?? string.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            Title = json["title"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Report id.</summary>
        public Guid Id { get; }

        /// <summary>See <see cref="StarhermitReportKinds"/>.</summary>
        public string Kind { get; }

        /// <summary>See <see cref="StarhermitReportStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>The title it was filed with.</summary>
        public string Title { get; }

        /// <summary>When it was filed.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When its status last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitMyReport Read(JsonValue json) => new StarhermitMyReport(json);
    }

    /// <summary>Kinds of player report.</summary>
    public static class StarhermitReportKinds
    {
        /// <summary>The game crashed.</summary>
        public const string Crash = "crash";

        /// <summary>The game misbehaved.</summary>
        public const string Bug = "bug";
    }

    /// <summary>Where a player report stands, as the game's owner sets it.</summary>
    public static class StarhermitReportStatuses
    {
        /// <summary>Not yet looked at.</summary>
        public const string Open = "open";

        /// <summary>Seen by the developer.</summary>
        public const string Acknowledged = "acknowledged";

        /// <summary>Dealt with.</summary>
        public const string Resolved = "resolved";
    }

    /// <summary>What a game's owner can see about the game while it runs.</summary>
    public sealed class StarhermitGameDiagnostics : StarhermitModel
    {
        private StarhermitGameDiagnostics(JsonValue json) : base(json)
        {
            Slug = json["slug"].AsStringOrNull() ?? string.Empty;
            Runtime = json["runtime"].AsStringOrNull() ?? string.Empty;
            IsEnabled = json["enabled"].AsBooleanOrDefault();
            TickRateHz = json["tickRateHz"].AsDoubleOrNull() ?? 0;
            var sessions = json["sessions"];
            ActiveSessions = sessions["active"].AsInt32OrDefault();
            FinishedSessions = sessions["finished"].AsInt32OrDefault();
            LiveConnections = sessions["liveConnections"].AsInt32OrDefault();
            SessionsWithLiveConnection = sessions["withLiveConnection"].AsInt32OrDefault();
            OldestActiveSince = sessions["oldestActiveSince"].AsDateTimeOffsetOrNull();
            var script = json["script"];
            ScriptInvocations = script["invocations"].AsInt64OrDefault();
            ScriptAverageMilliseconds = script["averageMillis"].AsDoubleOrNull() ?? 0;
            ScriptPeakMilliseconds = script["peakMillis"].AsInt32OrDefault();
            ScriptLastInvokedAt = script["lastInvokedAt"].AsDateTimeOffsetOrNull();
            ScriptCpuMillisecondsBudget = script["cpuMillisBudget"].AsInt32OrDefault();
            ScriptMemoryBudgetBytes = script["memoryBudgetBytes"].AsInt64OrDefault();
            ScriptMaxStatements = script["maxStatements"].AsInt64OrDefault();
            var matchmaking = json["matchmaking"];
            MatchmakingQueued = matchmaking["queued"].AsInt32OrDefault();
            MatchmakingWaitingLongestSeconds = matchmaking["waitingLongestSeconds"].AsInt32OrDefault();
            MatchmakingWidestSearchBand = matchmaking["widestSearchBand"].AsDecimalOrNull() ?? 0;
            MatchmakingMaxWaitSeconds = matchmaking["maxWaitSeconds"].AsInt32OrDefault();
            var webhooks = json["webhooks"];
            WebhookEndpoints = webhooks["endpoints"].AsInt32OrDefault();
            DisabledWebhookEndpoints = webhooks["disabledEndpoints"].AsInt32OrDefault();
            PendingWebhookDeliveries = webhooks["pending"].AsInt32OrDefault();
            DeadWebhookDeliveries = webhooks["dead"].AsInt32OrDefault();
            var buffer = json["writeBuffer"];
            WriteBufferEnabled = buffer["enabled"].AsBooleanOrDefault();
            WriteBufferPendingSessions = buffer["pendingSessions"].AsInt32OrDefault();
            WriteBufferPendingBytes = buffer["pendingBytes"].AsInt64OrDefault();
            WriteBufferMaxPendingBytes = buffer["maxPendingBytes"].AsInt64OrDefault();
            ObservedAt = json["observedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>The game's slug.</summary>
        public string Slug { get; }

        /// <summary>The runtime hosting its logic: <c>script</c> or <c>container</c>.</summary>
        public string Runtime { get; }

        /// <summary>True when the game is enabled.</summary>
        public bool IsEnabled { get; }

        /// <summary>The rate the platform ticks it at.</summary>
        public double TickRateHz { get; }

        /// <summary>Sessions in progress.</summary>
        public int ActiveSessions { get; }

        /// <summary>Sessions that have finished.</summary>
        public int FinishedSessions { get; }

        /// <summary>Game sockets open now.</summary>
        public int LiveConnections { get; }

        /// <summary>Active sessions at least one player is connected to.</summary>
        public int SessionsWithLiveConnection { get; }

        /// <summary>When the oldest active session began.</summary>
        public DateTimeOffset? OldestActiveSince { get; }

        /// <summary>Script invocations counted.</summary>
        public long ScriptInvocations { get; }

        /// <summary>Average script invocation time.</summary>
        public double ScriptAverageMilliseconds { get; }

        /// <summary>Slowest script invocation.</summary>
        public int ScriptPeakMilliseconds { get; }

        /// <summary>When the script last ran.</summary>
        public DateTimeOffset? ScriptLastInvokedAt { get; }

        /// <summary>CPU budget per invocation.</summary>
        public int ScriptCpuMillisecondsBudget { get; }

        /// <summary>Memory budget per invocation.</summary>
        public long ScriptMemoryBudgetBytes { get; }

        /// <summary>Statement budget per invocation.</summary>
        public long ScriptMaxStatements { get; }

        /// <summary>Players waiting in matchmaking.</summary>
        public int MatchmakingQueued { get; }

        /// <summary>How long the longest-waiting player has waited.</summary>
        public int MatchmakingWaitingLongestSeconds { get; }

        /// <summary>The widest rating band a search has reached.</summary>
        public decimal MatchmakingWidestSearchBand { get; }

        /// <summary>How long a search may wait before it expires.</summary>
        public int MatchmakingMaxWaitSeconds { get; }

        /// <summary>Webhook endpoints registered.</summary>
        public int WebhookEndpoints { get; }

        /// <summary>Endpoints disabled after failing.</summary>
        public int DisabledWebhookEndpoints { get; }

        /// <summary>Deliveries waiting to be sent.</summary>
        public int PendingWebhookDeliveries { get; }

        /// <summary>Deliveries that ran out of attempts.</summary>
        public int DeadWebhookDeliveries { get; }

        /// <summary>True when session writes are batched.</summary>
        public bool WriteBufferEnabled { get; }

        /// <summary>Sessions with state not yet written out.</summary>
        public int WriteBufferPendingSessions { get; }

        /// <summary>Bytes of state not yet written out.</summary>
        public long WriteBufferPendingBytes { get; }

        /// <summary>Most the buffer holds before it writes out early.</summary>
        public long WriteBufferMaxPendingBytes { get; }

        /// <summary>When these numbers were read.</summary>
        public DateTimeOffset? ObservedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameDiagnostics Read(JsonValue json) => new StarhermitGameDiagnostics(json);
    }

    /// <summary>A URL the platform calls when something happens in a game.</summary>
    /// <remarks>
    /// Deliveries are signed over the timestamp and the body. <see cref="Secret"/> is returned once, when
    /// the endpoint is created; store it with the backend that verifies deliveries and never ship it in
    /// a game client.
    /// </remarks>
    public sealed class StarhermitWebhookEndpoint : StarhermitModel
    {
        private StarhermitWebhookEndpoint(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Url = json["url"].AsStringOrNull() ?? string.Empty;
            Events = json["events"].AsList(value => value.AsStringOrNull() ?? string.Empty);
            IsEnabled = json["enabled"].AsBooleanOrDefault();
            ConsecutiveFailures = json["consecutiveFailures"].AsInt32OrDefault();
            DisabledReason = json["disabledReason"].AsStringOrNull();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            Secret = json["secret"].AsStringOrNull();
        }

        /// <summary>Endpoint id.</summary>
        public Guid Id { get; }

        /// <summary>Where deliveries go.</summary>
        public string Url { get; }

        /// <summary>The events delivered to it.</summary>
        public IReadOnlyList<string> Events { get; }

        /// <summary>False once the platform disabled it for failing; resume it to deliver again.</summary>
        public bool IsEnabled { get; }

        /// <summary>Failures in a row.</summary>
        public int ConsecutiveFailures { get; }

        /// <summary>Why it was disabled, while it is.</summary>
        public string? DisabledReason { get; }

        /// <summary>When it was registered.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>The signing secret - present only in the answer to creating the endpoint.</summary>
        public string? Secret { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitWebhookEndpoint Read(JsonValue json) => new StarhermitWebhookEndpoint(json);
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
        /// <summary>Searching for an opponent.</summary>
        public const string Queued = "queued";

        /// <summary>Never sent by the API, which reports a searching ticket as <see cref="Queued"/>.</summary>
        [Obsolete("The API reports a searching ticket as \"queued\". Compare with Queued.")]
        public const string Waiting = "waiting";

        /// <summary>Matched into a session.</summary>
        public const string Matched = "matched";

        /// <summary>Cancelled by the player.</summary>
        public const string Cancelled = "cancelled";

        /// <summary>The search ran out of time without a match.</summary>
        public const string Expired = "expired";
    }
}
