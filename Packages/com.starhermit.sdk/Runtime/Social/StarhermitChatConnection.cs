using System;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// The live chat socket: messages, edits, deletions, membership changes and invite pushes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The socket is a delivery mechanism, not the source of record. REST remains authoritative, and
    /// a game that runs both should de-duplicate by <see cref="StarhermitMessage.Id"/> - the server's
    /// id, never one the SDK invented.
    /// </para>
    /// <para>
    /// Frames the SDK does not recognise are raised as <see cref="UnknownEventReceived"/> with their
    /// payload intact, so a deployment can ship a new event type without breaking existing builds.
    /// </para>
    /// </remarks>
    public sealed class StarhermitChatConnection : StarhermitConnection
    {
        internal StarhermitChatConnection(StarhermitClient client) : base(client, "chat")
        {
        }

        /// <inheritdoc />
        protected override string Path => "chat";

        /// <summary>Raised when a message arrives, including system notes.</summary>
        public event Action<StarhermitMessage>? MessageReceived;

        /// <summary>Raised when a message is edited.</summary>
        public event Action<StarhermitMessage>? MessageUpdated;

        /// <summary>Raised when a message is deleted.</summary>
        public event Action<StarhermitMessage>? MessageDeleted;

        /// <summary>Raised when a conversation the caller belongs to is created.</summary>
        public event Action<StarhermitConversation>? ConversationCreated;

        /// <summary>Raised when a conversation is renamed.</summary>
        public event Action<StarhermitConversation>? ConversationRenamed;

        /// <summary>Raised when participants are added to a conversation.</summary>
        public event Action<StarhermitConversation>? ParticipantsAdded;

        /// <summary>Raised when a participant is removed. Carries the conversation and the account.</summary>
        public event Action<Guid, Guid>? ParticipantRemoved;

        /// <summary>Raised when the caller's read marker moves, including from another device.</summary>
        public event Action<Guid>? ConversationRead;

        /// <summary>Raised when a chat invitation arrives.</summary>
        public event Action<StarhermitChatInvite>? ChatInviteReceived;

        /// <summary>Raised when an invitation the caller sent is answered.</summary>
        public event Action<StarhermitChatInvite>? ChatInviteAnswered;

        /// <summary>Raised when a game or room invitation is pushed over the chat socket.</summary>
        public event Action<StarhermitInviteNotification>? GameInviteReceived;

        /// <summary>Raised for any frame this SDK version does not recognise, payload intact.</summary>
        public event Action<string, JsonValue>? UnknownEventReceived;

        /// <inheritdoc />
        protected override void HandleText(string text)
        {
            if (!JsonParser.TryParse(text, out var frame) || !frame.IsObject) return;

            var type = frame["type"].AsStringOrNull();
            if (type == null) return;
            var payload = frame["payload"];

            switch (type)
            {
                case "new_message":
                    Raise(() => MessageReceived?.Invoke(StarhermitMessage.Read(payload)));
                    break;
                case "message_updated":
                    Raise(() => MessageUpdated?.Invoke(StarhermitMessage.Read(payload)));
                    break;
                case "message_deleted":
                    Raise(() => MessageDeleted?.Invoke(StarhermitMessage.Read(payload)));
                    break;
                case "conversation_created":
                    Raise(() => ConversationCreated?.Invoke(StarhermitConversation.Read(payload)));
                    break;
                case "conversation_renamed":
                    Raise(() => ConversationRenamed?.Invoke(StarhermitConversation.Read(payload)));
                    break;
                case "participants_added":
                    Raise(() => ParticipantsAdded?.Invoke(StarhermitConversation.Read(payload)));
                    break;
                case "participant_removed":
                {
                    var conversationId = payload["conversationId"].AsGuidOrNull() ?? Guid.Empty;
                    var userId = payload["userId"].AsGuidOrNull() ?? Guid.Empty;
                    Raise(() => ParticipantRemoved?.Invoke(conversationId, userId));
                    break;
                }

                case "conversation_read":
                {
                    var conversationId = payload["conversationId"].AsGuidOrNull() ?? Guid.Empty;
                    Raise(() => ConversationRead?.Invoke(conversationId));
                    break;
                }

                case "chat_invite":
                    Raise(() => ChatInviteReceived?.Invoke(StarhermitChatInvite.Read(payload)));
                    break;
                case "chat_invite_responded":
                    Raise(() => ChatInviteAnswered?.Invoke(StarhermitChatInvite.Read(payload)));
                    break;
                case "game_invite":
                    Raise(() => GameInviteReceived?.Invoke(StarhermitInviteNotification.Read(payload)));
                    break;
                default:
                    Raise(() => UnknownEventReceived?.Invoke(type, payload));
                    break;
            }
        }
    }
}
