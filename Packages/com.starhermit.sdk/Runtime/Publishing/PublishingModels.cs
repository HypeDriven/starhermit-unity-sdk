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
}
