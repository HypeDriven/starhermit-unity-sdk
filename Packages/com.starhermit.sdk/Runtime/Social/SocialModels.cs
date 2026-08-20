using System;
using System.Collections.Generic;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>An accepted friend, with the presence the viewer is allowed to see.</summary>
    public sealed class StarhermitFriend : StarhermitModel
    {
        private StarhermitFriend(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            IsOnline = json["online"].AsBooleanOrDefault();
            CurrentGame = json["currentGame"].AsStringOrNull();
        }

        /// <summary>The friend's account id.</summary>
        public Guid UserId { get; }

        /// <summary>The friend's username.</summary>
        public string Username { get; }

        /// <summary>
        /// Whether they are online. Reads false when their privacy settings hide presence from the
        /// viewer - absence of a signal, not proof of absence.
        /// </summary>
        public bool IsOnline { get; }

        /// <summary>What they are playing, when they share it.</summary>
        public string? CurrentGame { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitFriend Read(JsonValue json) => new StarhermitFriend(json);
    }

    /// <summary>A pending friend request addressed to the signed-in account.</summary>
    public sealed class StarhermitFriendRequest : StarhermitModel
    {
        private StarhermitFriendRequest(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            SenderUserId = json["senderUserId"].AsGuidOrNull() ?? Guid.Empty;
            SenderUsername = json["senderUsername"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Request id, used to accept or decline.</summary>
        public Guid Id { get; }

        /// <summary>Who sent it.</summary>
        public Guid SenderUserId { get; }

        /// <summary>Their username.</summary>
        public string SenderUsername { get; }

        /// <summary>When it was sent.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitFriendRequest Read(JsonValue json) => new StarhermitFriendRequest(json);
    }

    /// <summary>A member of a conversation.</summary>
    public sealed class StarhermitChatParticipant : StarhermitModel
    {
        private StarhermitChatParticipant(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
        }

        /// <summary>The participant's account id.</summary>
        public Guid UserId { get; }

        /// <summary>Their username.</summary>
        public string Username { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitChatParticipant Read(JsonValue json) => new StarhermitChatParticipant(json);
    }

    /// <summary>A direct conversation or a named room.</summary>
    public sealed class StarhermitConversation : StarhermitModel
    {
        private StarhermitConversation(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Type = json["type"].AsStringOrNull() ?? string.Empty;
            Name = json["name"].AsStringOrNull();
            CreatedByUserId = json["createdByUserId"].AsGuidOrNull();
            JoinPolicy = json["joinPolicy"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            Participants = json["participants"].AsList(StarhermitChatParticipant.Read);
            OtherParticipant = json["otherParticipant"].IsObject
                ? StarhermitChatParticipant.Read(json["otherParticipant"])
                : null;
            UnreadCount = json["unreadCount"].AsInt32OrDefault();
        }

        /// <summary>Conversation id.</summary>
        public Guid Id { get; }

        /// <summary>Conversation type - see <see cref="StarhermitConversationTypes"/>.</summary>
        public string Type { get; }

        /// <summary>Room name, for a group conversation.</summary>
        public string? Name { get; }

        /// <summary>Who created it.</summary>
        public Guid? CreatedByUserId { get; }

        /// <summary>Join policy - see <see cref="StarhermitJoinPolicies"/>.</summary>
        public string JoinPolicy { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Everyone in the conversation.</summary>
        public IReadOnlyList<StarhermitChatParticipant> Participants { get; }

        /// <summary>For a direct conversation, the participant who is not the caller.</summary>
        public StarhermitChatParticipant? OtherParticipant { get; }

        /// <summary>Unread messages for the caller.</summary>
        public int UnreadCount { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitConversation Read(JsonValue json) => new StarhermitConversation(json);
    }

    /// <summary>A chat message.</summary>
    /// <remarks>
    /// <see cref="Content"/> is player-authored text. The SDK never renders, executes or trusts it -
    /// a game applies its own moderation and formatting policy.
    /// </remarks>
    public sealed class StarhermitMessage : StarhermitModel
    {
        private StarhermitMessage(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            ConversationId = json["conversationId"].AsGuidOrNull() ?? Guid.Empty;
            SenderId = json["senderId"].AsGuidOrNull() ?? Guid.Empty;
            SenderUsername = json["senderUsername"].AsStringOrNull() ?? string.Empty;
            Content = json["content"].AsStringOrNull();
            Kind = json["kind"].AsStringOrNull() ?? StarhermitMessageKinds.Text;
            Metadata = json["metadata"].IsObject
                ? json["metadata"].AsDictionary(value => value.AsStringOrNull() ?? string.Empty)
                : null;
            SentAt = json["sentAt"].AsDateTimeOffsetOrNull();
            EditedAt = json["editedAt"].AsDateTimeOffsetOrNull();
            IsDeleted = json["isDeleted"].AsBooleanOrDefault();
        }

        /// <summary>Message id, assigned by the server.</summary>
        public Guid Id { get; }

        /// <summary>Conversation the message belongs to.</summary>
        public Guid ConversationId { get; }

        /// <summary>Who sent it.</summary>
        public Guid SenderId { get; }

        /// <summary>Their username at the time of reading.</summary>
        public string SenderUsername { get; }

        /// <summary>The message text. Null once deleted.</summary>
        public string? Content { get; }

        /// <summary>Message kind - see <see cref="StarhermitMessageKinds"/>.</summary>
        public string Kind { get; }

        /// <summary>Structured detail carried by a system note.</summary>
        public IReadOnlyDictionary<string, string>? Metadata { get; }

        /// <summary>When it was sent.</summary>
        public DateTimeOffset? SentAt { get; }

        /// <summary>When it was last edited.</summary>
        public DateTimeOffset? EditedAt { get; }

        /// <summary>True once the message has been deleted.</summary>
        public bool IsDeleted { get; }

        /// <summary>True when this is a system note rather than something a player typed.</summary>
        public bool IsSystem => !string.Equals(Kind, StarhermitMessageKinds.Text, StringComparison.Ordinal);

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitMessage Read(JsonValue json) => new StarhermitMessage(json);
    }

    /// <summary>An invitation to join a conversation.</summary>
    public sealed class StarhermitChatInvite : StarhermitModel
    {
        private StarhermitChatInvite(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            ConversationId = json["conversationId"].AsGuidOrNull() ?? Guid.Empty;
            ConversationName = json["conversationName"].AsStringOrNull();
            FromUserId = json["fromUserId"].AsGuidOrNull() ?? Guid.Empty;
            FromUsername = json["fromUsername"].AsStringOrNull() ?? string.Empty;
            ToUserId = json["toUserId"].AsGuidOrNull() ?? Guid.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Invite id.</summary>
        public Guid Id { get; }

        /// <summary>Conversation being offered.</summary>
        public Guid ConversationId { get; }

        /// <summary>Its name, when it has one.</summary>
        public string? ConversationName { get; }

        /// <summary>Who sent the invite.</summary>
        public Guid FromUserId { get; }

        /// <summary>Their username.</summary>
        public string FromUsername { get; }

        /// <summary>Who it was sent to.</summary>
        public Guid ToUserId { get; }

        /// <summary>Invite status - see <see cref="StarhermitInviteStatuses"/>.</summary>
        public string Status { get; }

        /// <summary>When it was sent.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitChatInvite Read(JsonValue json) => new StarhermitChatInvite(json);
    }

    /// <summary>A room the caller may join without an invitation.</summary>
    public sealed class StarhermitJoinableRoom : StarhermitModel
    {
        private StarhermitJoinableRoom(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            Name = json["name"].AsStringOrNull();
            JoinPolicy = json["joinPolicy"].AsStringOrNull() ?? string.Empty;
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            Participants = json["participants"].AsList(StarhermitChatParticipant.Read);
        }

        /// <summary>Conversation id.</summary>
        public Guid Id { get; }

        /// <summary>Room name.</summary>
        public string? Name { get; }

        /// <summary>Join policy.</summary>
        public string JoinPolicy { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Who is currently in it.</summary>
        public IReadOnlyList<StarhermitChatParticipant> Participants { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitJoinableRoom Read(JsonValue json) => new StarhermitJoinableRoom(json);
    }

    /// <summary>Unread totals across the caller's conversations.</summary>
    public sealed class StarhermitUnreadSummary : StarhermitModel
    {
        private StarhermitUnreadSummary(JsonValue json) : base(json)
        {
            Total = json["total"].AsInt32OrDefault();
            var counts = new Dictionary<Guid, int>();
            foreach (var item in json["conversations"].AsArray())
            {
                var id = item["conversationId"].AsGuidOrNull();
                if (id.HasValue) counts[id.Value] = item["count"].AsInt32OrDefault();
            }

            Conversations = counts;
        }

        /// <summary>Total unread messages.</summary>
        public int Total { get; }

        /// <summary>Unread count per conversation.</summary>
        public IReadOnlyDictionary<Guid, int> Conversations { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitUnreadSummary Read(JsonValue json) => new StarhermitUnreadSummary(json);
    }

    /// <summary>A voice room anchored to a chat conversation.</summary>
    public sealed class StarhermitVoiceRoom : StarhermitModel
    {
        private StarhermitVoiceRoom(JsonValue json) : base(json)
        {
            Id = json["id"].AsGuidOrNull() ?? Guid.Empty;
            ConversationId = json["conversationId"].AsGuidOrNull() ?? Guid.Empty;
            CreatorUserId = json["creatorUserId"].AsGuidOrNull() ?? Guid.Empty;
            Codec = json["codec"].AsStringOrNull() ?? string.Empty;
            Status = json["status"].AsStringOrNull() ?? string.Empty;
            MaxParticipants = json["maxParticipants"].AsInt32OrDefault();
            CreatedAt = json["createdAt"].AsDateTimeOffsetOrNull();
            Participants = json["participants"].AsList(StarhermitVoiceParticipant.Read);
        }

        /// <summary>Room id, used when connecting the voice socket.</summary>
        public Guid Id { get; }

        /// <summary>The conversation the room belongs to.</summary>
        public Guid ConversationId { get; }

        /// <summary>Who opened it.</summary>
        public Guid CreatorUserId { get; }

        /// <summary>Codec the room advertises.</summary>
        public string Codec { get; }

        /// <summary>Room status.</summary>
        public string Status { get; }

        /// <summary>Participant limit the deployment applied.</summary>
        public int MaxParticipants { get; }

        /// <summary>When it was created.</summary>
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Who is in the room.</summary>
        public IReadOnlyList<StarhermitVoiceParticipant> Participants { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitVoiceRoom Read(JsonValue json) => new StarhermitVoiceRoom(json);
    }

    /// <summary>Someone in a voice room.</summary>
    public sealed class StarhermitVoiceParticipant : StarhermitModel
    {
        private StarhermitVoiceParticipant(JsonValue json) : base(json)
        {
            UserId = json["userId"].AsGuidOrNull() ?? Guid.Empty;
            Username = json["username"].AsStringOrNull() ?? string.Empty;
            IsMuted = json["isMuted"].AsBooleanOrDefault();
            IsConnected = json["isConnected"].AsBooleanOrDefault();
            JoinedAt = json["joinedAt"].AsDateTimeOffsetOrNull();
        }

        /// <summary>Their account id, which is also the sender id stamped on their audio frames.</summary>
        public Guid UserId { get; }

        /// <summary>Their username.</summary>
        public string Username { get; }

        /// <summary>Whether they are muted server-side.</summary>
        public bool IsMuted { get; }

        /// <summary>Whether their socket is currently connected.</summary>
        public bool IsConnected { get; }

        /// <summary>When they joined.</summary>
        public DateTimeOffset? JoinedAt { get; }

        /// <summary>Reads the model from a response body.</summary>
        /// <param name="json">Response body.</param>
        /// <returns>The parsed model.</returns>
        public static StarhermitVoiceParticipant Read(JsonValue json) => new StarhermitVoiceParticipant(json);
    }

    /// <summary>Conversation types the API uses.</summary>
    public static class StarhermitConversationTypes
    {
        /// <summary>A one-to-one conversation between two accounts.</summary>
        public const string Direct = "direct";

        /// <summary>A named room with any number of participants.</summary>
        public const string Group = "group";
    }

    /// <summary>Join policies a room can have.</summary>
    public static class StarhermitJoinPolicies
    {
        /// <summary>Anyone may join without an invitation.</summary>
        public const string Open = "open";

        /// <summary>Only invited accounts may join.</summary>
        public const string Closed = "closed";
    }

    /// <summary>Message kinds the API emits.</summary>
    public static class StarhermitMessageKinds
    {
        /// <summary>Text a participant typed.</summary>
        public const string Text = "text";

        /// <summary>A system note recording that the room was renamed.</summary>
        public const string SystemRename = "system_rename";
    }

    /// <summary>Statuses an invitation moves through.</summary>
    public static class StarhermitInviteStatuses
    {
        /// <summary>Waiting for an answer.</summary>
        public const string Pending = "pending";

        /// <summary>Accepted.</summary>
        public const string Accepted = "accepted";

        /// <summary>Declined.</summary>
        public const string Declined = "declined";
    }
}
