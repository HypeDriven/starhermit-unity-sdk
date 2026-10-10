using System;
using System.Collections.Generic;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>A publisher account.</summary>
    public sealed class StarhermitPublisher : StarhermitModel
    {
        private StarhermitPublisher(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Name = json["name"].AsStringOrNull() ?? string.Empty;
            Description = json["description"].AsStringOrNull() ?? string.Empty;
            OwnerUserId = json["ownerUserId"].AsGuidOrNull() ?? Guid.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Publisher id.</summary>
        public Guid Id { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Description.</summary>
        public string Description { get; }

        /// <summary>The account that owns the publisher.</summary>
        public Guid OwnerUserId { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it was last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitPublisher Read(JsonValue json) => new StarhermitPublisher(json);
    }

    /// <summary>Someone's membership of a publisher.</summary>
    public sealed class StarhermitPublisherMember : StarhermitModel
    {
        private StarhermitPublisherMember(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            PublisherId = json["publisherId"].AsGuidOrNull() ?? Guid.Empty;
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Role = json["role"].AsStringOrNull() ?? string.Empty;
            JoinedAt = json["joinedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Membership row id.</summary>
        public Guid Id { get; }

        /// <summary>The publisher.</summary>
        public Guid PublisherId { get; }

        /// <summary>The member.</summary>
        public Guid UserId { get; }

        /// <summary>Their role.</summary>
        public string Role { get; }

        /// <summary>When they joined.</summary>
        public DateTimeOffset? JoinedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitPublisherMember Read(JsonValue json) => new StarhermitPublisherMember(json);
    }

    /// <summary>An upload target for one asset of a build.</summary>
    /// <remarks>The URL is signed: treat it as a credential and never log it.</remarks>
    public sealed class StarhermitUploadTarget : StarhermitModel
    {
        private StarhermitUploadTarget(JsonValue json) : base(json)
        {
            Type = json["type"].AsStringOrNull() ?? string.Empty;
            UploadUrl = json["uploadUrl"].AsStringOrNull() ?? string.Empty;
            FieldKey = json["fieldKey"].AsStringOrNull() ?? string.Empty;
        }

        /// <summary>Which asset this target is for.</summary>
        public string Type { get; }

        /// <summary>The signed URL to upload to.</summary>
        public string UploadUrl { get; }

        /// <summary>The field key the storage backend expects.</summary>
        public string FieldKey { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitUploadTarget Read(JsonValue json) => new StarhermitUploadTarget(json);
    }

    /// <summary>Describes an uploaded asset when finalising a build.</summary>
    public sealed class StarhermitAssetDescriptor
    {
        /// <summary>Creates a descriptor.</summary>
        /// <param name="type">Asset type, matching the upload target.</param>
        /// <param name="checksum">Checksum of the uploaded bytes.</param>
        /// <param name="fieldKey">The field key from the upload target.</param>
        public StarhermitAssetDescriptor(string type, string checksum, string fieldKey)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Checksum = checksum ?? throw new ArgumentNullException(nameof(checksum));
            FieldKey = fieldKey ?? throw new ArgumentNullException(nameof(fieldKey));
        }

        /// <summary>Asset type.</summary>
        public string Type { get; }

        /// <summary>Checksum of the uploaded bytes.</summary>
        public string Checksum { get; }

        /// <summary>The field key the asset was uploaded under.</summary>
        public string FieldKey { get; }

        /// <summary>Writes the descriptor as the API's request shape.</summary>
        /// <param name="writer">Writer positioned where the object should be written.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.WriteStartObject();
            writer.Write("type", Type);
            writer.Write("checksum", Checksum);
            writer.Write("fieldKey", FieldKey);
            writer.WriteEndObject();
        }
    }

    /// <summary>Where a browser game is deployed and how that deployment is going.</summary>
    public sealed class StarhermitHostingStatus : StarhermitModel
    {
        private StarhermitHostingStatus(JsonValue json) : base(json)
        {
            HostingEnabled = json["hostingEnabled"].AsBooleanOrDefault();
            HostedUrl = json["hostedUrl"].AsStringOrNull();
            DeployStatus = json["deployStatus"].AsStringOrNull() ?? string.Empty;
            PinnedCommitSha = json["pinnedCommitSha"].AsStringOrNull();
            DeployedCommitSha = json["deployedCommitSha"].AsStringOrNull();
            DeployError = json["deployError"].AsStringOrNull();
            DeployedAt = json["deployedAt"].AsDateTimeOffsetOrNull();
            ExternalLaunchUrl = json["externalLaunchUrl"].AsStringOrNull();
        }

        /// <summary>Whether the platform hosts the game.</summary>
        public bool HostingEnabled { get; }

        /// <summary>Where it is served, when hosting is on.</summary>
        public string? HostedUrl { get; }

        /// <summary>Deployment status.</summary>
        public string DeployStatus { get; }

        /// <summary>The commit the owner pinned, if any.</summary>
        public string? PinnedCommitSha { get; }

        /// <summary>The commit actually deployed.</summary>
        public string? DeployedCommitSha { get; }

        /// <summary>Why the last deployment failed, when it did.</summary>
        public string? DeployError { get; }

        /// <summary>When the deployment last succeeded.</summary>
        public DateTimeOffset? DeployedAt { get; }

        /// <summary>Where the game runs when it is hosted elsewhere.</summary>
        public string? ExternalLaunchUrl { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitHostingStatus Read(JsonValue json) => new StarhermitHostingStatus(json);
    }

    /// <summary>A browser game submitted from a GitHub repository.</summary>
    public sealed class StarhermitBrowserGame : StarhermitModel
    {
        private StarhermitBrowserGame(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            RepoUrl = json["repoUrl"].AsStringOrNull() ?? string.Empty;
            OwnerLogin = json["ownerLogin"].AsStringOrNull() ?? string.Empty;
            RepoName = json["repoName"].AsStringOrNull() ?? string.Empty;
            DisplayName = json["displayName"].AsStringOrNull() ?? string.Empty;
            LaunchPath = json["launchPath"].AsStringOrNull() ?? string.Empty;
            ServerScriptPath = json["serverScriptPath"].AsStringOrNull();
            GameSlug = json["gameSlug"].AsStringOrNull();
            IsVerifiedOwner = json["isVerifiedOwner"].AsBooleanOrDefault();
            MetadataSource = json["metadataSource"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            CoverArtSource = json["coverArtSource"].AsStringOrNull();
            CoverArtUpdatedAt = json["coverArtUpdatedAt"].AsDateTimeOffsetOrNull();
            Hosting = json["hosting"].IsObject ? StarhermitHostingStatus.Read(json["hosting"]) : null;
            SubmittedByUserId = json["submittedByUserId"].AsGuidOrNull();
            SubmittedByUsername = json["submittedByUsername"].AsStringOrNull();
            Description = json["description"].AsStringOrNull();
            ReleaseNotesUpdatedAt = json["releaseNotesUpdatedAt"].AsDateTimeOffsetOrNull();
            ServerRuntime = json["serverRuntime"].AsStringOrNull();
        }

        /// <summary>Game id.</summary>
        public Guid Id { get; }

        /// <summary>Repository URL.</summary>
        public string RepoUrl { get; }

        /// <summary>Repository owner login.</summary>
        public string OwnerLogin { get; }

        /// <summary>Repository name.</summary>
        public string RepoName { get; }

        /// <summary>Display name.</summary>
        public string DisplayName { get; }

        /// <summary>Entry point within the repository.</summary>
        public string LaunchPath { get; }

        /// <summary>Server script path, for a game with authoritative logic.</summary>
        public string? ServerScriptPath { get; }

        /// <summary>The authoritative-game slug this browser game backs, when it has one.</summary>
        public string? GameSlug { get; }

        /// <summary>True when the submitter proved they own the repository.</summary>
        public bool IsVerifiedOwner { get; }

        /// <summary>Where the metadata came from.</summary>
        public string MetadataSource { get; }

        /// <summary>When it was submitted.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Where the cover art came from.</summary>
        public string? CoverArtSource { get; }

        /// <summary>When the cover art last changed.</summary>
        public DateTimeOffset? CoverArtUpdatedAt { get; }

        /// <summary>Hosting and deployment state.</summary>
        public StarhermitHostingStatus? Hosting { get; }

        /// <summary>Who submitted it, on the shared listing.</summary>
        public Guid? SubmittedByUserId { get; }

        /// <summary>Their username, on the shared listing.</summary>
        public string? SubmittedByUsername { get; }

        /// <summary>The game's description, when it has one.</summary>
        public string? Description { get; }

        /// <summary>
        /// When the release notes last changed, or null when the game has none - read them with
        /// <see cref="StarhermitBrowserGamesClient.GetReleaseNotesAsync"/>.
        /// </summary>
        public DateTimeOffset? ReleaseNotesUpdatedAt { get; }

        /// <summary>
        /// Which runtime the game's authoritative backend runs on - <c>script</c> or <c>container</c> -
        /// or null for a browser-only game. Ask this rather than inferring a backend from
        /// <see cref="ServerScriptPath"/> or <see cref="GameSlug"/>: every game whose author is known has
        /// a slug, and a container game has no script path.
        /// </summary>
        public string? ServerRuntime { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitBrowserGame Read(JsonValue json) => new StarhermitBrowserGame(json);
    }

    /// <summary>The outcome of publishing a bundle.</summary>
    public sealed class StarhermitBundleResult : StarhermitModel
    {
        private StarhermitBundleResult(JsonValue json) : base(json)
        {
            ClientPublished = json["clientPublished"].AsBooleanOrDefault();
            ServerImageLoaded = json["serverImageLoaded"].AsBooleanOrDefault();
            ImageDigest = json["imageDigest"].AsStringOrNull();
            BytesReceived = json["bytesReceived"].AsInt64OrDefault();
        }

        /// <summary>True when the client bundle was published.</summary>
        public bool ClientPublished { get; }

        /// <summary>True when a server image was loaded from the bundle.</summary>
        public bool ServerImageLoaded { get; }

        /// <summary>Digest of the loaded server image.</summary>
        public string? ImageDigest { get; }

        /// <summary>How many bytes the server accepted.</summary>
        public long BytesReceived { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitBundleResult Read(JsonValue json) => new StarhermitBundleResult(json);
    }

    /// <summary>How many people are playing a browser game.</summary>
    public sealed class StarhermitGameAudience : StarhermitModel
    {
        private StarhermitGameAudience(JsonValue json) : base(json)
        {
            TotalPlayers = json["totalPlayers"].AsInt32OrDefault();
            PlayingNow = json["playingNow"].AsInt32OrDefault();
            TotalSessions = json["totalSessions"].AsInt64OrDefault();
            TotalPlaytimeMinutes = json["totalPlaytimeMinutes"].AsInt64OrDefault();
            LastPlayedAt = json["lastPlayedAt"].AsDateTimeOffsetOrNull();
            LivenessWindowHours = json["livenessWindowHours"].AsInt32OrDefault();
        }

        /// <summary>Distinct players ever.</summary>
        public int TotalPlayers { get; }

        /// <summary>Players active within the liveness window.</summary>
        public int PlayingNow { get; }

        /// <summary>Sessions ever.</summary>
        public long TotalSessions { get; }

        /// <summary>Total minutes played.</summary>
        public long TotalPlaytimeMinutes { get; }

        /// <summary>When it was last played.</summary>
        public DateTimeOffset? LastPlayedAt { get; }

        /// <summary>How wide the "playing now" window is, in hours.</summary>
        public int LivenessWindowHours { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitGameAudience Read(JsonValue json) => new StarhermitGameAudience(json);
    }

    /// <summary>Fields of an achievement definition to change. Unset members are left alone.</summary>
    public sealed class StarhermitAchievementUpdate
    {
        /// <summary>New stable key.</summary>
        public Optional<string> Key { get; set; }

        /// <summary>New display name.</summary>
        public Optional<string> Name { get; set; }

        /// <summary>New description.</summary>
        public Optional<string> Description { get; set; }

        /// <summary>New icon reference.</summary>
        public Optional<string> Icon { get; set; }

        /// <summary>Whether the achievement is hidden until unlocked.</summary>
        public Optional<bool> IsSecret { get; set; }

        /// <summary>New point value.</summary>
        public Optional<int> Points { get; set; }

        /// <summary>New visibility rule.</summary>
        public Optional<string> Visibility { get; set; }

        /// <summary>New criteria description.</summary>
        public Optional<string> Criteria { get; set; }

        /// <summary>Writes the update as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (Key.IsSet) writer.Write("key", Key.Value);
            if (Name.IsSet) writer.Write("name", Name.Value);
            if (Description.IsSet) writer.Write("description", Description.Value);
            if (Icon.IsSet) writer.Write("icon", Icon.Value);
            if (IsSecret.IsSet) writer.Write("secret", IsSecret.Value);
            if (Points.IsSet) writer.Write("points", Points.Value);
            if (Visibility.IsSet) writer.Write("visibility", Visibility.Value);
            if (Criteria.IsSet) writer.Write("criteria", Criteria.Value);
        }
    }

    /// <summary>A leaderboard definition to create or change.</summary>
    public sealed class StarhermitLeaderboardDefinition
    {
        /// <summary>Display name.</summary>
        public Optional<string> Name { get; set; }

        /// <summary>What the score means.</summary>
        public Optional<string> ScoreType { get; set; }

        /// <summary>Whether higher or lower is better.</summary>
        public Optional<string> SortDirection { get; set; }

        /// <summary>Reset schedule.</summary>
        public Optional<string> ResetSchedule { get; set; }

        /// <summary>Lowest accepted score.</summary>
        public Optional<decimal> MinScore { get; set; }

        /// <summary>Highest accepted score.</summary>
        public Optional<decimal> MaxScore { get; set; }

        /// <summary>Scope.</summary>
        public Optional<string> Scope { get; set; }

        /// <summary>Region, for a regional board.</summary>
        public Optional<string> Region { get; set; }

        /// <summary>Whether the board accepts submissions.</summary>
        public Optional<bool> IsActive { get; set; }

        /// <summary>Title the board belongs to.</summary>
        public Optional<Guid> SoftwareTitleId { get; set; }

        /// <summary>Writes the definition as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (Name.IsSet) writer.Write("name", Name.Value);
            if (ScoreType.IsSet) writer.Write("scoreType", ScoreType.Value);
            if (SortDirection.IsSet) writer.Write("sortDirection", SortDirection.Value);
            if (ResetSchedule.IsSet) writer.Write("resetSchedule", ResetSchedule.Value);
            if (MinScore.IsSet)
            {
                writer.WritePropertyName("minScore");
                writer.WriteNumber(MinScore.Value);
            }

            if (MaxScore.IsSet)
            {
                writer.WritePropertyName("maxScore");
                writer.WriteNumber(MaxScore.Value);
            }

            if (Scope.IsSet) writer.Write("scope", Scope.Value);
            if (Region.IsSet) writer.Write("region", Region.Value);
            if (IsActive.IsSet) writer.Write("isActive", IsActive.Value);
            if (SoftwareTitleId.IsSet) writer.Write("softwareTitleId", SoftwareTitleId.Value);
        }
    }

    /// <summary>A game the caller removed, and whether it can come back.</summary>
    public sealed class StarhermitRemovedBrowserGame : StarhermitModel
    {
        private StarhermitRemovedBrowserGame(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            DisplayName = json["displayName"].AsStringOrNull() ?? string.Empty;
            RepoUrl = json["repoUrl"].AsStringOrNull() ?? string.Empty;
            RemovedAt = json["removedAt"].AsDateTimeOffsetOrNull();
            IsRestorable = json["restorable"].AsBooleanOrDefault();
            FilesKept = json["filesKept"].AsBooleanOrDefault();
        }

        /// <summary>Game id - unchanged by a restore.</summary>
        public Guid Id { get; }

        /// <summary>Display name.</summary>
        public string DisplayName { get; }

        /// <summary>The URL it was added from; empty for an uploaded game.</summary>
        public string RepoUrl { get; }

        /// <summary>When it was removed.</summary>
        public DateTimeOffset? RemovedAt { get; }

        /// <summary>
        /// True when <see cref="StarhermitBrowserGamesClient.RestoreAsync"/> can bring it back. A game
        /// with a source is restored by adding its URL again instead.
        /// </summary>
        public bool IsRestorable { get; }

        /// <summary>True when its files have not been reclaimed, so a restore brings them back too.</summary>
        public bool FilesKept { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitRemovedBrowserGame Read(JsonValue json) => new StarhermitRemovedBrowserGame(json);
    }

    /// <summary>A game's release notes.</summary>
    public sealed class StarhermitReleaseNotes : StarhermitModel
    {
        private StarhermitReleaseNotes(JsonValue json) : base(json)
        {
            GameId = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Notes = json["releaseNotes"].AsStringOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
            CommitSha = json["commitSha"].AsStringOrNull();
        }

        /// <summary>The game.</summary>
        public Guid GameId { get; }

        /// <summary>The notes, as the developer wrote them.</summary>
        public string? Notes { get; }

        /// <summary>When they last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>The commit deployed, for a game built from a repository.</summary>
        public string? CommitSha { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitReleaseNotes Read(JsonValue json) => new StarhermitReleaseNotes(json);
    }

    /// <summary>An achievement a game's owner manages, with how many players hold it.</summary>
    public sealed class StarhermitOwnedAchievement : StarhermitModel
    {
        private StarhermitOwnedAchievement(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Key = json["key"].AsStringOrNull() ?? string.Empty;
            Name = json["name"].AsStringOrNull() ?? string.Empty;
            Description = json["description"].AsStringOrNull() ?? string.Empty;
            Icon = json["icon"].AsStringOrNull();
            IsSecret = json["secret"].AsBooleanOrDefault();
            Points = json["points"].AsInt32OrDefault();
            Origin = json["origin"].AsStringOrNull() ?? string.Empty;
            Unlocks = json["unlocks"].AsInt32OrDefault();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Achievement id.</summary>
        public Guid Id { get; }

        /// <summary>The key the game's server logic unlocks it by.</summary>
        public string Key { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Description.</summary>
        public string Description { get; }

        /// <summary>Icon reference.</summary>
        public string? Icon { get; }

        /// <summary>True when it is hidden until unlocked.</summary>
        public bool IsSecret { get; }

        /// <summary>Point value.</summary>
        public int Points { get; }

        /// <summary>
        /// <c>declared</c> when the game's script declares it - replaced on every redeploy and not
        /// editable here - or <c>owner</c> when it was created through this API.
        /// </summary>
        public string Origin { get; }

        /// <summary>How many players hold it.</summary>
        public int Unlocks { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When it last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitOwnedAchievement Read(JsonValue json) => new StarhermitOwnedAchievement(json);
    }

    /// <summary>
    /// An achievement for an owner to create or change. Unset members are left alone; <see cref="Key"/>
    /// is read only on create, since it is what the game's server logic unlocks by.
    /// </summary>
    public sealed class StarhermitOwnedAchievementDraft
    {
        /// <summary>The key the game's server logic unlocks it by. Create only.</summary>
        public Optional<string> Key { get; set; }

        /// <summary>Display name.</summary>
        public Optional<string> Name { get; set; }

        /// <summary>Description.</summary>
        public Optional<string> Description { get; set; }

        /// <summary>Icon reference.</summary>
        public Optional<string> Icon { get; set; }

        /// <summary>Whether it is hidden until unlocked.</summary>
        public Optional<bool> IsSecret { get; set; }

        /// <summary>Point value.</summary>
        public Optional<int> Points { get; set; }

        /// <summary>Writes the draft as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (Key.IsSet) writer.Write("key", Key.Value);
            if (Name.IsSet) writer.Write("name", Name.Value);
            if (Description.IsSet) writer.Write("description", Description.Value);
            if (Icon.IsSet) writer.Write("icon", Icon.Value);
            if (IsSecret.IsSet) writer.Write("secret", IsSecret.Value);
            if (Points.IsSet) writer.Write("points", Points.Value);
        }
    }

    /// <summary>
    /// A leaderboard for a game's owner to create or change. Unset members are left alone;
    /// <see cref="Key"/>, <see cref="ScoreType"/> and <see cref="SortDirection"/> are read only on create.
    /// </summary>
    /// <remarks>
    /// The game fills its boards through its server logic's <c>scores</c> result; no client can write
    /// to them. A <see cref="ResetSchedule"/> may change at any time: it decides which entries are
    /// shown, not which are kept.
    /// </remarks>
    public sealed class StarhermitOwnedLeaderboardDraft
    {
        /// <summary>The key the game's server logic submits scores under. Create only.</summary>
        public Optional<string> Key { get; set; }

        /// <summary>Display name.</summary>
        public Optional<string> Name { get; set; }

        /// <summary>What the score means. Create only.</summary>
        public Optional<string> ScoreType { get; set; }

        /// <summary>Whether higher or lower is better. Create only.</summary>
        public Optional<string> SortDirection { get; set; }

        /// <summary>Lowest accepted score.</summary>
        public Optional<decimal> MinScore { get; set; }

        /// <summary>Highest accepted score.</summary>
        public Optional<decimal> MaxScore { get; set; }

        /// <summary>Whether the board accepts scores.</summary>
        public Optional<bool> IsActive { get; set; }

        /// <summary>When the board starts over; <c>never</c> stops resetting.</summary>
        public Optional<string> ResetSchedule { get; set; }

        /// <summary>Writes the draft as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (Key.IsSet) writer.Write("key", Key.Value);
            if (Name.IsSet) writer.Write("name", Name.Value);
            if (ScoreType.IsSet) writer.Write("scoreType", ScoreType.Value);
            if (SortDirection.IsSet) writer.Write("sortDirection", SortDirection.Value);
            if (MinScore.IsSet)
            {
                writer.WritePropertyName("minScore");
                writer.WriteNumber(MinScore.Value);
            }

            if (MaxScore.IsSet)
            {
                writer.WritePropertyName("maxScore");
                writer.WriteNumber(MaxScore.Value);
            }

            if (IsActive.IsSet) writer.Write("isActive", IsActive.Value);
            if (ResetSchedule.IsSet) writer.Write("resetSchedule", ResetSchedule.Value);
        }
    }

    /// <summary>A player's rating after the owner reset it.</summary>
    public sealed class StarhermitEloReset : StarhermitModel
    {
        private StarhermitEloReset(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Elo = json["elo"].AsDecimalOrNull() ?? 0;
        }

        /// <summary>The player.</summary>
        public Guid UserId { get; }

        /// <summary>Their rating now - the starting rating.</summary>
        public decimal Elo { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitEloReset Read(JsonValue json) => new StarhermitEloReset(json);
    }

    /// <summary>A player report as the game's owner sees it in a listing.</summary>
    public sealed class StarhermitReportSummary : StarhermitModel
    {
        private StarhermitReportSummary(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Kind = json["kind"].AsStringOrNull() ?? string.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            Title = json["title"].AsStringOrNull() ?? string.Empty;
            ReporterUserId = json["reporterUserId"].AsGuidOrNull() ?? Guid.Empty;
            ReporterName = json["reporterName"].AsStringOrNull();
            ClientVersion = json["clientVersion"].AsStringOrNull();
            Platform = json["platform"].AsStringOrNull();
            AttachmentCount = json["attachmentCount"].AsInt32OrDefault();
            SizeBytes = json["sizeBytes"].AsInt64OrDefault();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Report id.</summary>
        public Guid Id { get; }

        /// <summary>See <see cref="StarhermitReportKinds"/>.</summary>
        public string Kind { get; }

        /// <summary>See <see cref="StarhermitReportStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>The player's one-line summary.</summary>
        public string Title { get; }

        /// <summary>Who filed it.</summary>
        public Guid ReporterUserId { get; }

        /// <summary>Their username, when they still have one.</summary>
        public string? ReporterName { get; }

        /// <summary>The game client's version, as reported.</summary>
        public string? ClientVersion { get; }

        /// <summary>The platform, as reported.</summary>
        public string? Platform { get; }

        /// <summary>Files attached.</summary>
        public int AttachmentCount { get; }

        /// <summary>Stored size, text and attachments together.</summary>
        public long SizeBytes { get; }

        /// <summary>When it was filed.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When its status last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitReportSummary Read(JsonValue json) => new StarhermitReportSummary(json);
    }

    /// <summary>A player report in full.</summary>
    public sealed class StarhermitReportDetail : StarhermitModel
    {
        private StarhermitReportDetail(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Kind = json["kind"].AsStringOrNull() ?? string.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            Title = json["title"].AsStringOrNull() ?? string.Empty;
            Description = json["description"].AsStringOrNull() ?? string.Empty;
            ReporterUserId = json["reporterUserId"].AsGuidOrNull() ?? Guid.Empty;
            ReporterName = json["reporterName"].AsStringOrNull();
            ClientVersion = json["clientVersion"].AsStringOrNull();
            Platform = json["platform"].AsStringOrNull();
            UserAgent = json["userAgent"].AsStringOrNull();
            BuildId = json["buildId"].AsStringOrNull();
            SessionId = json["sessionId"].AsGuidOrNull();
            SizeBytes = json["sizeBytes"].AsInt64OrDefault();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
            Attachments = json["attachments"].AsList(StarhermitReportAttachmentInfo.Read);
        }

        /// <summary>Report id.</summary>
        public Guid Id { get; }

        /// <summary>See <see cref="StarhermitReportKinds"/>.</summary>
        public string Kind { get; }

        /// <summary>See <see cref="StarhermitReportStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>The player's one-line summary.</summary>
        public string Title { get; }

        /// <summary>What the player wrote. Untrusted text - escape it before rendering.</summary>
        public string Description { get; }

        /// <summary>Who filed it.</summary>
        public Guid ReporterUserId { get; }

        /// <summary>Their username, when they still have one.</summary>
        public string? ReporterName { get; }

        /// <summary>The game client's version, as reported.</summary>
        public string? ClientVersion { get; }

        /// <summary>The platform, as reported.</summary>
        public string? Platform { get; }

        /// <summary>The browser's user agent, as reported.</summary>
        public string? UserAgent { get; }

        /// <summary>The build the player was running, as reported.</summary>
        public string? BuildId { get; }

        /// <summary>The session the problem happened in, as reported.</summary>
        public Guid? SessionId { get; }

        /// <summary>Stored size, text and attachments together.</summary>
        public long SizeBytes { get; }

        /// <summary>When it was filed.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When its status last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Attached files; download one with <see cref="StarhermitBrowserGamesClient.GetReportAttachmentAsync"/>.</summary>
        public IReadOnlyList<StarhermitReportAttachmentInfo> Attachments { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitReportDetail Read(JsonValue json) => new StarhermitReportDetail(json);
    }

    /// <summary>A file attached to a player report, without its contents.</summary>
    public sealed class StarhermitReportAttachmentInfo : StarhermitModel
    {
        private StarhermitReportAttachmentInfo(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            FileName = json["fileName"].AsStringOrNull() ?? string.Empty;
            ContentType = json["contentType"].AsStringOrNull() ?? string.Empty;
            SizeBytes = json["sizeBytes"].AsInt64OrDefault();
        }

        /// <summary>Attachment id.</summary>
        public Guid Id { get; }

        /// <summary>The name the player's client gave it. Untrusted - never use it as a local path as-is.</summary>
        public string FileName { get; }

        /// <summary>Media type, as the client labelled it.</summary>
        public string ContentType { get; }

        /// <summary>Size.</summary>
        public long SizeBytes { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitReportAttachmentInfo Read(JsonValue json) => new StarhermitReportAttachmentInfo(json);
    }

    /// <summary>The recent output of a game's server container.</summary>
    public sealed class StarhermitContainerLogs : StarhermitModel
    {
        private StarhermitContainerLogs(JsonValue json) : base(json)
        {
            Source = json["source"].AsStringOrNull() ?? string.Empty;
            DeploymentStatus = json["deploymentStatus"].AsStringOrNull();
            CapturedAt = json["capturedAt"].AsDateTimeOffsetOrNull();
            IsTruncated = json["truncated"].AsBooleanOrDefault();
            Logs = json["logs"].AsStringOrNull() ?? string.Empty;
        }

        /// <summary>
        /// Where the output came from: <c>live</c> (the running container), <c>last_crash</c> (the
        /// newest crash report, when nothing is running) or <c>none</c>.
        /// </summary>
        public string Source { get; }

        /// <summary>The deployment's state when it was read.</summary>
        public string? DeploymentStatus { get; }

        /// <summary>When the output was captured.</summary>
        public DateTimeOffset? CapturedAt { get; }

        /// <summary>True when older output was cut to fit the game's limit; the newest is kept.</summary>
        public bool IsTruncated { get; }

        /// <summary>The output text.</summary>
        public string Logs { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitContainerLogs Read(JsonValue json) => new StarhermitContainerLogs(json);
    }

    /// <summary>A recorded crash of a game's server container.</summary>
    public sealed class StarhermitContainerCrash : StarhermitModel
    {
        private StarhermitContainerCrash(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            OccurredAt = json["occurredAt"].AsDateTimeOffsetOrNull();
            Kind = json["kind"].AsStringOrNull() ?? string.Empty;
            ExitCode = json["exitCode"].AsInt32OrNull();
            Detail = json["detail"].AsStringOrNull() ?? string.Empty;
            ImageDigest = json["imageDigest"].AsStringOrNull() ?? string.Empty;
            RestartCount = json["restartCount"].AsInt32OrDefault();
            AffectedSessions = json["affectedSessions"].AsInt32OrDefault();
            LogLength = json["logLength"].AsInt32OrNull();
            LogsTruncated = json["logsTruncated"].AsBooleanOrDefault();
            Logs = json["logs"].AsStringOrNull();
        }

        /// <summary>Crash id.</summary>
        public Guid Id { get; }

        /// <summary>When it happened.</summary>
        public DateTimeOffset? OccurredAt { get; }

        /// <summary>What kind of failure the platform saw.</summary>
        public string Kind { get; }

        /// <summary>The process's exit code, when it exited.</summary>
        public int? ExitCode { get; }

        /// <summary>The platform's description of the failure.</summary>
        public string Detail { get; }

        /// <summary>The image that was running.</summary>
        public string ImageDigest { get; }

        /// <summary>Restarts the deployment had been through.</summary>
        public int RestartCount { get; }

        /// <summary>Live sessions the crash interrupted.</summary>
        public int AffectedSessions { get; }

        /// <summary>Length of the kept output, in a listing; null in the full report.</summary>
        public int? LogLength { get; }

        /// <summary>True when the kept output was cut to the game's limit.</summary>
        public bool LogsTruncated { get; }

        /// <summary>The container's final output - present only when one crash is read in full.</summary>
        public string? Logs { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitContainerCrash Read(JsonValue json) => new StarhermitContainerCrash(json);
    }
}
