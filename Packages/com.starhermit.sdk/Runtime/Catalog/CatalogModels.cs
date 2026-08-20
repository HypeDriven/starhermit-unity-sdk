using System;
using System.Collections.Generic;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>A catalog title.</summary>
    public sealed class StarhermitSoftwareTitle : StarhermitModel
    {
        private StarhermitSoftwareTitle(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Name = json["name"].AsStringOrNull() ?? string.Empty;
            Description = json["description"].AsStringOrNull() ?? string.Empty;
            PublisherId = json["publisherId"].AsGuidOrNull() ?? Guid.Empty;
            Category = json["category"].AsStringOrNull() ?? string.Empty;
            Platform = json["platform"].AsStringOrNull() ?? string.Empty;
            ReleaseStatus = json["releaseStatus"].AsStringOrNull() ?? string.Empty;
            Tags = json["tags"].AsList(value => value.AsStringOrNull() ?? string.Empty);
            PriceCents = json["priceCents"].AsInt32OrDefault();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
            Builds = json["builds"].IsArray ? json["builds"].AsList(StarhermitSoftwareBuild.Read) : null;
        }

        /// <summary>Title id.</summary>
        public Guid Id { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Store description.</summary>
        public string Description { get; }

        /// <summary>Publisher that owns the title.</summary>
        public Guid PublisherId { get; }

        /// <summary>Catalog category.</summary>
        public string Category { get; }

        /// <summary>Platform the title targets.</summary>
        public string Platform { get; }

        /// <summary>Release status, for example <c>released</c>.</summary>
        public string ReleaseStatus { get; }

        /// <summary>Store tags.</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Price in minor units. Zero means the title can be claimed for free.</summary>
        public int PriceCents { get; }

        /// <summary>When the title was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it was last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Builds, when the endpoint included them.</summary>
        public IReadOnlyList<StarhermitSoftwareBuild>? Builds { get; }

        /// <summary>True when the title is free to claim.</summary>
        public bool IsFree => PriceCents <= 0;

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitSoftwareTitle Read(JsonValue json) => new StarhermitSoftwareTitle(json);
    }

    /// <summary>One published build of a title.</summary>
    public sealed class StarhermitSoftwareBuild : StarhermitModel
    {
        private StarhermitSoftwareBuild(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            TitleId = json["titleId"].AsGuidOrNull() ?? Guid.Empty;
            Version = json["version"].AsStringOrNull() ?? string.Empty;
            ReleaseDate = json["releaseDate"].AsDateTimeOffsetOrNull();
            ReleaseNotes = json["releaseNotes"].AsStringOrNull() ?? string.Empty;
            Metadata = json["metadata"].AsStringOrNull();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            Assets = json["assets"].AsList(StarhermitSoftwareAsset.Read);
        }

        /// <summary>Build id.</summary>
        public Guid Id { get; }

        /// <summary>Title the build belongs to.</summary>
        public Guid TitleId { get; }

        /// <summary>Version string.</summary>
        public string Version { get; }

        /// <summary>Release date.</summary>
        public DateTimeOffset? ReleaseDate { get; }

        /// <summary>Release notes.</summary>
        public string ReleaseNotes { get; }

        /// <summary>Free-form metadata.</summary>
        public string? Metadata { get; }

        /// <summary>When the build row was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Assets attached to the build.</summary>
        public IReadOnlyList<StarhermitSoftwareAsset> Assets { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitSoftwareBuild Read(JsonValue json) => new StarhermitSoftwareBuild(json);
    }

    /// <summary>A downloadable asset belonging to a build.</summary>
    /// <remarks>
    /// <see cref="MalwareScanStatus"/> is the gate: the SDK refuses to download an asset whose scan has
    /// not come back clean, because a launcher that ships a file the platform has flagged is the worst
    /// possible convenience.
    /// </remarks>
    public sealed class StarhermitSoftwareAsset : StarhermitModel
    {
        private StarhermitSoftwareAsset(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            BuildId = json["buildId"].AsGuidOrNull() ?? Guid.Empty;
            Type = json["type"].AsStringOrNull() ?? string.Empty;
            Checksum = json["checksum"].AsStringOrNull();
            MalwareScanStatus = json["malwareScanStatus"].AsStringOrNull() ?? string.Empty;
            IsProcessed = json["processed"].AsBooleanOrDefault();
            Metadata = json["metadata"].AsStringOrNull();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Asset id.</summary>
        public Guid Id { get; }

        /// <summary>Build the asset belongs to.</summary>
        public Guid BuildId { get; }

        /// <summary>Asset type, for example <c>installer</c>.</summary>
        public string Type { get; }

        /// <summary>Checksum, when the platform computed one.</summary>
        public string? Checksum { get; }

        /// <summary>Scan status - see <see cref="StarhermitScanStatuses"/>.</summary>
        public string MalwareScanStatus { get; }

        /// <summary>True once ingestion finished.</summary>
        public bool IsProcessed { get; }

        /// <summary>Free-form metadata.</summary>
        public string? Metadata { get; }

        /// <summary>When the asset row was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>True when the asset is safe to download by the platform's own judgement.</summary>
        public bool IsScanClean =>
            string.Equals(MalwareScanStatus, StarhermitScanStatuses.Clean, StringComparison.OrdinalIgnoreCase);

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitSoftwareAsset Read(JsonValue json) => new StarhermitSoftwareAsset(json);
    }

    /// <summary>Malware-scan states an asset can be in.</summary>
    public static class StarhermitScanStatuses
    {
        /// <summary>Scanned and clean.</summary>
        public const string Clean = "clean";

        /// <summary>Not yet scanned.</summary>
        public const string Pending = "pending";

        /// <summary>Flagged; the asset must not be downloaded.</summary>
        public const string Infected = "infected";
    }

    /// <summary>A started launch, which the game ends when the player stops playing.</summary>
    public sealed class StarhermitLaunchSession : StarhermitModel
    {
        private StarhermitLaunchSession(JsonValue json) : base(json)
        {
            LaunchId = json["launchId"].AsGuidOrNull() ?? Guid.Empty;
            StartedAt = json["startTime"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Launch id, needed to end the session.</summary>
        public Guid LaunchId { get; }

        /// <summary>When the launch was recorded.</summary>
        public DateTimeOffset? StartedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitLaunchSession Read(JsonValue json) => new StarhermitLaunchSession(json);
    }

    /// <summary>Playtime the account has accumulated on one title.</summary>
    public sealed class StarhermitPlaytime : StarhermitModel
    {
        private StarhermitPlaytime(JsonValue json) : base(json)
        {
            SoftwareTitleId = json["softwareTitleId"].AsGuidOrNull();
            Provider = json["provider"].AsStringOrNull();
            ExternalId = json["externalId"].AsStringOrNull();
            TotalSeconds = json["totalSeconds"].AsInt64OrDefault();
            TotalMinutes = json["totalMinutes"].AsInt64OrDefault();
        }

        /// <summary>The catalog title, for a Starhermit title.</summary>
        public Guid? SoftwareTitleId { get; }

        /// <summary>The provider, for an external title.</summary>
        public string? Provider { get; }

        /// <summary>The provider's software id, for an external title.</summary>
        public string? ExternalId { get; }

        /// <summary>Total seconds played.</summary>
        public long TotalSeconds { get; }

        /// <summary>Total minutes played, as the server rounds them.</summary>
        public long TotalMinutes { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitPlaytime Read(JsonValue json) => new StarhermitPlaytime(json);
    }

    /// <summary>How long one friend has played a title.</summary>
    public sealed class StarhermitFriendPlaytime : StarhermitModel
    {
        private StarhermitFriendPlaytime(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            Minutes = json["minutes"].AsInt64OrDefault();
            Seconds = json["seconds"].AsInt64OrDefault();
        }

        /// <summary>The friend.</summary>
        public Guid UserId { get; }

        /// <summary>Their username.</summary>
        public string Username { get; }

        /// <summary>Minutes played.</summary>
        public long Minutes { get; }

        /// <summary>Seconds played.</summary>
        public long Seconds { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitFriendPlaytime Read(JsonValue json) => new StarhermitFriendPlaytime(json);
    }

    /// <summary>An entry in an activity feed.</summary>
    public sealed class StarhermitActivity : StarhermitModel
    {
        private StarhermitActivity(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            Type = json["type"].AsStringOrNull() ?? string.Empty;
            Timestamp = json["timestamp"].AsDateTimeOffsetOrNull();
            SoftwareTitleId = json["softwareTitleId"].AsGuidOrNull();
            TitleName = json["titleName"].AsStringOrNull();
            Metadata = json["metadata"].AsStringOrNull();
        }

        /// <summary>Activity id.</summary>
        public Guid Id { get; }

        /// <summary>Whose activity it is.</summary>
        public Guid UserId { get; }

        /// <summary>Their username.</summary>
        public string Username { get; }

        /// <summary>Activity type, for example <c>launch</c>.</summary>
        public string Type { get; }

        /// <summary>When it happened.</summary>
        public DateTimeOffset? Timestamp { get; }

        /// <summary>Title involved, when there is one.</summary>
        public Guid? SoftwareTitleId { get; }

        /// <summary>That title's name.</summary>
        public string? TitleName { get; }

        /// <summary>Free-form metadata.</summary>
        public string? Metadata { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitActivity Read(JsonValue json) => new StarhermitActivity(json);
    }

    /// <summary>One entry of a game's public activity feed.</summary>
    public sealed class StarhermitGameFeedItem : StarhermitModel
    {
        private StarhermitGameFeedItem(JsonValue json) : base(json)
        {
            Type = json["type"].AsStringOrNull() ?? string.Empty;
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            Timestamp = json["timestamp"].AsDateTimeOffsetOrNull();
            Minutes = json["minutes"].AsInt64OrDefault();
            Stars = json["stars"].AsInt32OrDefault();
        }

        /// <summary>Item type, for example a session or a rating.</summary>
        public string Type { get; }

        /// <summary>Who it concerns.</summary>
        public Guid UserId { get; }

        /// <summary>Their username.</summary>
        public string Username { get; }

        /// <summary>When it happened.</summary>
        public DateTimeOffset? Timestamp { get; }

        /// <summary>Minutes played, for a session item.</summary>
        public long Minutes { get; }

        /// <summary>Stars given, for a rating item.</summary>
        public int Stars { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameFeedItem Read(JsonValue json) => new StarhermitGameFeedItem(json);
    }

    /// <summary>Aggregated rating for one game key.</summary>
    public sealed class StarhermitRatingSummary : StarhermitModel
    {
        private StarhermitRatingSummary(JsonValue json) : base(json)
        {
            GameKey = json["gameKey"].AsStringOrNull() ?? string.Empty;
            Average = json["average"].AsDoubleOrNull() ?? 0d;
            Count = json["count"].AsInt32OrDefault();
            MyStars = json["myStars"].AsInt32OrNull();
        }

        /// <summary>The uniform game key the rating applies to.</summary>
        public string GameKey { get; }

        /// <summary>Average score across all ratings.</summary>
        public double Average { get; }

        /// <summary>How many ratings were counted.</summary>
        public int Count { get; }

        /// <summary>The caller's own rating, when they have one.</summary>
        public int? MyStars { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRatingSummary Read(JsonValue json) => new StarhermitRatingSummary(json);
    }

    /// <summary>A written review.</summary>
    public sealed class StarhermitReview : StarhermitModel
    {
        private StarhermitReview(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            Stars = json["stars"].AsInt32OrDefault();
            Review = json["review"].AsStringOrNull() ?? string.Empty;
            Timestamp = json["timestamp"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Who wrote it.</summary>
        public Guid UserId { get; }

        /// <summary>Their username.</summary>
        public string Username { get; }

        /// <summary>Score out of five.</summary>
        public int Stars { get; }

        /// <summary>The review text, which a player wrote and a game should moderate before showing.</summary>
        public string Review { get; }

        /// <summary>When it was written.</summary>
        public DateTimeOffset? Timestamp { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitReview Read(JsonValue json) => new StarhermitReview(json);
    }

    /// <summary>Metadata about the account's cloud save for one game key.</summary>
    public sealed class StarhermitCloudSaveInfo : StarhermitModel
    {
        private StarhermitCloudSaveInfo(JsonValue json) : base(json)
        {
            Exists = json["exists"].AsBooleanOrDefault();
            SizeBytes = json["sizeBytes"].AsInt64OrDefault();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
            GameKey = json["gameKey"].AsStringOrNull();
        }

        /// <summary>True when a save is stored.</summary>
        public bool Exists { get; }

        /// <summary>Size of the stored archive.</summary>
        public long SizeBytes { get; }

        /// <summary>When it was last written.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>The game key, when the response echoed it.</summary>
        public string? GameKey { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitCloudSaveInfo Read(JsonValue json) => new StarhermitCloudSaveInfo(json);
    }

    /// <summary>An achievement definition.</summary>
    public sealed class StarhermitAchievement : StarhermitModel
    {
        private StarhermitAchievement(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Key = json["key"].AsStringOrNull() ?? string.Empty;
            Name = json["name"].AsStringOrNull() ?? string.Empty;
            Description = json["description"].AsStringOrNull() ?? string.Empty;
            Icon = json["icon"].AsStringOrNull();
            IsSecret = json["secret"].AsBooleanOrDefault();
            Points = json["points"].AsInt32OrDefault();
            Visibility = json["visibility"].AsStringOrNull();
            Criteria = json["criteria"].AsStringOrNull();
        }

        /// <summary>Definition id.</summary>
        public Guid Id { get; }

        /// <summary>Stable key the game refers to it by.</summary>
        public string Key { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Description.</summary>
        public string Description { get; }

        /// <summary>Icon reference.</summary>
        public string? Icon { get; }

        /// <summary>True when the achievement is hidden until unlocked.</summary>
        public bool IsSecret { get; }

        /// <summary>Point value.</summary>
        public int Points { get; }

        /// <summary>Visibility rule the publisher set.</summary>
        public string? Visibility { get; }

        /// <summary>Criteria description.</summary>
        public string? Criteria { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitAchievement Read(JsonValue json) => new StarhermitAchievement(json);
    }

    /// <summary>An achievement the account has unlocked.</summary>
    public sealed class StarhermitUnlockedAchievement : StarhermitModel
    {
        private StarhermitUnlockedAchievement(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            AchievementDefinitionId = json["achievementDefinitionId"].AsGuidOrNull() ?? Guid.Empty;
            Key = json["key"].AsStringOrNull() ?? string.Empty;
            Name = json["name"].AsStringOrNull() ?? string.Empty;
            Description = json["description"].AsStringOrNull() ?? string.Empty;
            Icon = json["icon"].AsStringOrNull();
            Points = json["points"].AsInt32OrDefault();
            UnlockedAt = json["unlockedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Unlock row id.</summary>
        public Guid Id { get; }

        /// <summary>The definition that was unlocked.</summary>
        public Guid AchievementDefinitionId { get; }

        /// <summary>Its stable key.</summary>
        public string Key { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Description.</summary>
        public string Description { get; }

        /// <summary>Icon reference.</summary>
        public string? Icon { get; }

        /// <summary>Point value.</summary>
        public int Points { get; }

        /// <summary>When it was unlocked.</summary>
        public DateTimeOffset? UnlockedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitUnlockedAchievement Read(JsonValue json) => new StarhermitUnlockedAchievement(json);
    }

    /// <summary>A leaderboard definition.</summary>
    public sealed class StarhermitLeaderboard : StarhermitModel
    {
        private StarhermitLeaderboard(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            SoftwareTitleId = json["softwareTitleId"].AsGuidOrNull();
            Name = json["name"].AsStringOrNull() ?? string.Empty;
            ScoreType = json["scoreType"].AsStringOrNull() ?? string.Empty;
            SortDirection = json["sortDirection"].AsStringOrNull() ?? string.Empty;
            ResetSchedule = json["resetSchedule"].AsStringOrNull();
            MinScore = json["minScore"].AsDecimalOrNull();
            MaxScore = json["maxScore"].AsDecimalOrNull();
            Scope = json["scope"].AsStringOrNull() ?? string.Empty;
            Region = json["region"].AsStringOrNull();
            IsActive = json["isActive"].AsBooleanOrDefault();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Leaderboard id.</summary>
        public Guid Id { get; }

        /// <summary>Title it belongs to, when it is title-scoped.</summary>
        public Guid? SoftwareTitleId { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>What the score means.</summary>
        public string ScoreType { get; }

        /// <summary>Whether higher or lower is better.</summary>
        public string SortDirection { get; }

        /// <summary>Reset schedule, when it resets.</summary>
        public string? ResetSchedule { get; }

        /// <summary>Lowest accepted score.</summary>
        public decimal? MinScore { get; }

        /// <summary>Highest accepted score.</summary>
        public decimal? MaxScore { get; }

        /// <summary>Scope, for example global or regional.</summary>
        public string Scope { get; }

        /// <summary>Region, for a regional board.</summary>
        public string? Region { get; }

        /// <summary>Whether the board accepts submissions.</summary>
        public bool IsActive { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it was last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitLeaderboard Read(JsonValue json) => new StarhermitLeaderboard(json);
    }

    /// <summary>One row on a leaderboard.</summary>
    public sealed class StarhermitLeaderboardEntry : StarhermitModel
    {
        private StarhermitLeaderboardEntry(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            Score = json["score"].AsDecimalOrNull() ?? 0m;
            Rank = json["rank"].AsInt32OrDefault();
            Region = json["region"].AsStringOrNull();
            SubmittedAt = json["submittedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Entry id.</summary>
        public Guid Id { get; }

        /// <summary>Whose score it is.</summary>
        public Guid UserId { get; }

        /// <summary>Their username.</summary>
        public string Username { get; }

        /// <summary>The score.</summary>
        public decimal Score { get; }

        /// <summary>Rank the server computed. Authoritative; the SDK never recomputes it.</summary>
        public int Rank { get; }

        /// <summary>Region the entry belongs to.</summary>
        public string? Region { get; }

        /// <summary>When it was submitted.</summary>
        public DateTimeOffset? SubmittedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitLeaderboardEntry Read(JsonValue json) => new StarhermitLeaderboardEntry(json);
    }

    /// <summary>A link to an external game library.</summary>
    public sealed class StarhermitExternalLibraryLink : StarhermitModel
    {
        private StarhermitExternalLibraryLink(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Provider = json["provider"].AsStringOrNull() ?? string.Empty;
            ExternalUserId = json["externalUserId"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Link id.</summary>
        public Guid Id { get; }

        /// <summary>Provider key.</summary>
        public string Provider { get; }

        /// <summary>The account's id at that provider.</summary>
        public string ExternalUserId { get; }

        /// <summary>When the link was made.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it was last refreshed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitExternalLibraryLink Read(JsonValue json) => new StarhermitExternalLibraryLink(json);
    }

    /// <summary>A title the account owns on an external provider.</summary>
    public sealed class StarhermitExternalOwnership : StarhermitModel
    {
        private StarhermitExternalOwnership(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Provider = json["provider"].AsStringOrNull() ?? string.Empty;
            ExternalSoftwareId = json["externalSoftwareId"].AsStringOrNull() ?? string.Empty;
            Name = json["name"].AsStringOrNull();
            SoftwareTitleId = json["softwareTitleId"].AsGuidOrNull();
            AcquiredAt = json["acquiredAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Ownership row id, used to launch the title.</summary>
        public Guid Id { get; }

        /// <summary>Provider key.</summary>
        public string Provider { get; }

        /// <summary>The provider's id for the title.</summary>
        public string ExternalSoftwareId { get; }

        /// <summary>Title name as the provider reports it.</summary>
        public string? Name { get; }

        /// <summary>Matching Starhermit catalog title, when one is known.</summary>
        public Guid? SoftwareTitleId { get; }

        /// <summary>When ownership was recorded.</summary>
        public DateTimeOffset? AcquiredAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitExternalOwnership Read(JsonValue json) => new StarhermitExternalOwnership(json);
    }
}
