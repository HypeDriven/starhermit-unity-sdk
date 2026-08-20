using System;
using System.Collections.Generic;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>How widely one kind of profile information is shared.</summary>
    public enum StarhermitPrivacyLevel
    {
        /// <summary>Visible only to the account itself.</summary>
        Private = 0,

        /// <summary>Visible to accepted friends.</summary>
        FriendsOnly = 1,

        /// <summary>Visible to anyone.</summary>
        Public = 2
    }

    /// <summary>The signed-in account's own profile.</summary>
    public sealed class StarhermitProfile : StarhermitModel
    {
        private StarhermitProfile(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            Nickname = json["nickname"].AsStringOrNull();
            Email = json["email"].AsStringOrNull() ?? string.Empty;
            Metadata = json["metadata"].AsStringOrNull();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            UpdatedAt = json["updatedAt"].AsDateTimeOffsetOrNull();
            TermsAcceptedHash = json["termsAcceptedHash"].AsStringOrNull();
            TermsAcceptedAt = json["termsAcceptedAt"].AsDateTimeOffsetOrNull();
            Privacy = json["privacy"].IsObject ? StarhermitPrivacySettings.Read(json["privacy"]) : null;
        }

        /// <summary>Account id.</summary>
        public Guid Id { get; }

        /// <summary>Unique username.</summary>
        public string Username { get; }

        /// <summary>Display nickname, which is not unique.</summary>
        public string? Nickname { get; }

        /// <summary>Account email address. Treat it as a credential channel, not a profile field.</summary>
        public string Email { get; }

        /// <summary>Free-form metadata the account stores on itself.</summary>
        public string? Metadata { get; }

        /// <summary>When the account was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>When the account was last changed.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Hash of the terms version the account accepted, if any.</summary>
        public string? TermsAcceptedHash { get; }

        /// <summary>When those terms were accepted.</summary>
        public DateTimeOffset? TermsAcceptedAt { get; }

        /// <summary>Privacy settings, when the account has saved any.</summary>
        public StarhermitPrivacySettings? Privacy { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitProfile Read(JsonValue json) => new StarhermitProfile(json);
    }

    /// <summary>Another account as it is visible publicly.</summary>
    public sealed class StarhermitPublicProfile : StarhermitModel
    {
        private StarhermitPublicProfile(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            Nickname = json["nickname"].AsStringOrNull();
        }

        /// <summary>Account id.</summary>
        public Guid Id { get; }

        /// <summary>Unique username.</summary>
        public string Username { get; }

        /// <summary>Display nickname.</summary>
        public string? Nickname { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitPublicProfile Read(JsonValue json) => new StarhermitPublicProfile(json);
    }

    /// <summary>What the account chooses to share, per kind of information.</summary>
    public sealed class StarhermitPrivacySettings : StarhermitModel
    {
        /// <summary>Creates a settings object with the server's own defaults.</summary>
        public StarhermitPrivacySettings()
            : base(JsonValue.EmptyObject)
        {
            OnlineStatus = StarhermitPrivacyLevel.FriendsOnly;
            CurrentlyPlaying = StarhermitPrivacyLevel.FriendsOnly;
            RecentLaunchActivity = StarhermitPrivacyLevel.FriendsOnly;
            HoursPlayed = StarhermitPrivacyLevel.Private;
            RecentDownloads = StarhermitPrivacyLevel.Private;
            Achievements = StarhermitPrivacyLevel.FriendsOnly;
            FriendDiscoverability = StarhermitPrivacyLevel.Public;
            ProfileVisibility = StarhermitPrivacyLevel.Public;
        }

        private StarhermitPrivacySettings(JsonValue json) : base(json)
        {
            OnlineStatus = ReadLevel(json["onlineStatus"], StarhermitPrivacyLevel.FriendsOnly);
            CurrentlyPlaying = ReadLevel(json["currentlyPlaying"], StarhermitPrivacyLevel.FriendsOnly);
            RecentLaunchActivity = ReadLevel(json["recentLaunchActivity"], StarhermitPrivacyLevel.FriendsOnly);
            HoursPlayed = ReadLevel(json["hoursPlayed"], StarhermitPrivacyLevel.Private);
            RecentDownloads = ReadLevel(json["recentDownloads"], StarhermitPrivacyLevel.Private);
            Achievements = ReadLevel(json["achievements"], StarhermitPrivacyLevel.FriendsOnly);
            FriendDiscoverability = ReadLevel(json["friendDiscoverability"], StarhermitPrivacyLevel.Public);
            ProfileVisibility = ReadLevel(json["profileVisibility"], StarhermitPrivacyLevel.Public);
        }

        /// <summary>Who may see whether the account is online.</summary>
        public StarhermitPrivacyLevel OnlineStatus { get; set; }

        /// <summary>Who may see what the account is playing.</summary>
        public StarhermitPrivacyLevel CurrentlyPlaying { get; set; }

        /// <summary>Who may see recent launches.</summary>
        public StarhermitPrivacyLevel RecentLaunchActivity { get; set; }

        /// <summary>Who may see playtime totals.</summary>
        public StarhermitPrivacyLevel HoursPlayed { get; set; }

        /// <summary>Who may see recent downloads.</summary>
        public StarhermitPrivacyLevel RecentDownloads { get; set; }

        /// <summary>Who may see unlocked achievements.</summary>
        public StarhermitPrivacyLevel Achievements { get; set; }

        /// <summary>Who may find the account when looking for friends.</summary>
        public StarhermitPrivacyLevel FriendDiscoverability { get; set; }

        /// <summary>Who may see the profile at all.</summary>
        public StarhermitPrivacyLevel ProfileVisibility { get; set; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitPrivacySettings Read(JsonValue json) => new StarhermitPrivacySettings(json);

        /// <summary>Writes the settings as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.Write("onlineStatus", (long)OnlineStatus);
            writer.Write("currentlyPlaying", (long)CurrentlyPlaying);
            writer.Write("recentLaunchActivity", (long)RecentLaunchActivity);
            writer.Write("hoursPlayed", (long)HoursPlayed);
            writer.Write("recentDownloads", (long)RecentDownloads);
            writer.Write("achievements", (long)Achievements);
            writer.Write("friendDiscoverability", (long)FriendDiscoverability);
            writer.Write("profileVisibility", (long)ProfileVisibility);
        }

        private static StarhermitPrivacyLevel ReadLevel(JsonValue value, StarhermitPrivacyLevel fallback)
        {
            if (value.Kind == JsonKind.Number)
            {
                var number = value.AsInt32();
                // An unknown level from a newer deployment must not read as Public by accident; the
                // safest interpretation of "I do not know what this means" is the most private one.
                return number >= 0 && number <= 2 ? (StarhermitPrivacyLevel)number : StarhermitPrivacyLevel.Private;
            }

            if (value.Kind == JsonKind.String)
            {
                switch (value.AsString().ToLowerInvariant())
                {
                    case "private": return StarhermitPrivacyLevel.Private;
                    case "friendsonly":
                    case "friends_only": return StarhermitPrivacyLevel.FriendsOnly;
                    case "public": return StarhermitPrivacyLevel.Public;
                    default: return StarhermitPrivacyLevel.Private;
                }
            }

            return fallback;
        }
    }

    /// <summary>A provider identity linked to the account.</summary>
    public sealed class StarhermitIdentity : StarhermitModel
    {
        private StarhermitIdentity(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Provider = json["provider"].AsStringOrNull() ?? string.Empty;
            ProviderUserId = json["providerUserId"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            Metadata = json["metadata"].AsStringOrNull();
        }

        /// <summary>Identity row id.</summary>
        public Guid Id { get; }

        /// <summary>Provider key, for example <c>github</c>.</summary>
        public string Provider { get; }

        /// <summary>The account's id at that provider.</summary>
        public string ProviderUserId { get; }

        /// <summary>When the identity was linked.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Free-form metadata stored with the identity.</summary>
        public string? Metadata { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitIdentity Read(JsonValue json) => new StarhermitIdentity(json);
    }

    /// <summary>Confirmation that the account accepted a specific terms version.</summary>
    public sealed class StarhermitTermsAcceptance : StarhermitModel
    {
        private StarhermitTermsAcceptance(JsonValue json) : base(json)
        {
            Hash = json["termsAcceptedHash"].AsStringOrNull() ?? string.Empty;
            AcceptedAt = json["termsAcceptedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Hash of the accepted terms.</summary>
        public string Hash { get; }

        /// <summary>When they were accepted.</summary>
        public DateTimeOffset? AcceptedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitTermsAcceptance Read(JsonValue json) => new StarhermitTermsAcceptance(json);
    }

    /// <summary>An entitlement granting the account access to a catalog title.</summary>
    public sealed class StarhermitEntitlement : StarhermitModel
    {
        private StarhermitEntitlement(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            SoftwareTitleId = json["softwareTitleId"].AsGuidOrNull() ?? Guid.Empty;
            GrantedBy = json["grantedBy"].AsStringOrNull();
            GrantedAt = json["grantedAt"].AsDateTimeOffsetOrNull();
            IsRevoked = json["isRevoked"].AsBooleanOrDefault();
            RevokedAt = json["revokedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Entitlement id.</summary>
        public Guid Id { get; }

        /// <summary>Account the entitlement belongs to.</summary>
        public Guid UserId { get; }

        /// <summary>Title it grants.</summary>
        public Guid SoftwareTitleId { get; }

        /// <summary>Who granted it.</summary>
        public string? GrantedBy { get; }

        /// <summary>When it was granted.</summary>
        public DateTimeOffset? GrantedAt { get; }

        /// <summary>True when it has been revoked.</summary>
        public bool IsRevoked { get; }

        /// <summary>When it was revoked.</summary>
        public DateTimeOffset? RevokedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitEntitlement Read(JsonValue json) => new StarhermitEntitlement(json);
    }

    /// <summary>An avatar image and the media type the server labelled it with.</summary>
    public readonly struct StarhermitAvatar
    {
        /// <summary>Creates an avatar payload.</summary>
        /// <param name="bytes">Image bytes.</param>
        /// <param name="contentType">Media type.</param>
        public StarhermitAvatar(byte[] bytes, string contentType)
        {
            Bytes = bytes ?? Array.Empty<byte>();
            ContentType = contentType ?? "image/png";
        }

        /// <summary>The image bytes, PNG in the current contract.</summary>
        public byte[] Bytes { get; }

        /// <summary>Media type from the response.</summary>
        public string ContentType { get; }
    }

    /// <summary>Fields of the profile to change. Unset members are left alone.</summary>
    public sealed class StarhermitProfileUpdate
    {
        /// <summary>New unique username.</summary>
        public Optional<string> Username { get; set; }

        /// <summary>
        /// New account email. The API only accepts this from an OAuth session, because the address is
        /// the channel that hands out credentials.
        /// </summary>
        public Optional<string> Email { get; set; }

        /// <summary>New free-form metadata, at most 4096 characters.</summary>
        public Optional<string> Metadata { get; set; }

        /// <summary>New nickname. Set it to an empty string to clear it.</summary>
        public Optional<string> Nickname { get; set; }

        /// <summary>Writes the update as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (Username.IsSet) writer.Write("username", Username.Value);
            if (Email.IsSet) writer.Write("email", Email.Value);
            if (Metadata.IsSet) writer.Write("metadata", Metadata.Value);
            if (Nickname.IsSet) writer.Write("nickname", Nickname.Value);
        }
    }
}
