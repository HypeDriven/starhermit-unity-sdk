using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// The whole conversation model: direct chats, rooms, invitations, participants and messages.
    /// </summary>
    /// <remarks>
    /// REST here is the source of record. The chat socket delivers the same events live, and a game
    /// that keeps both running should de-duplicate by server message id: the SDK never invents an id
    /// or an optimistic timestamp for a message the server has not accepted yet.
    /// </remarks>
    public sealed class StarhermitChatClient : StarhermitServiceClient
    {
        internal StarhermitChatClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Creates or fetches the direct conversation with one account.</summary>
        /// <param name="friendUserId">The other participant.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The conversation.</returns>
        public Task<StarhermitConversation> CreateDirectConversationAsync(
            Guid friendUserId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post("chat/conversations"), writer => writer.Write("friendUserId", friendUserId)),
                "chat.createDirectConversation",
                StarhermitConversation.Read,
                cancellationToken);

        /// <summary>Creates a named group conversation.</summary>
        /// <param name="name">Room name.</param>
        /// <param name="participantIds">Accounts to add immediately.</param>
        /// <param name="joinPolicy">Join policy - see <see cref="StarhermitJoinPolicies"/>.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The conversation.</returns>
        public Task<StarhermitConversation> CreateGroupConversationAsync(
            string name,
            IEnumerable<Guid>? participantIds = null,
            string? joinPolicy = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("chat/conversations"), writer =>
            {
                writer.Write("name", name);
                writer.WriteIfPresent("joinPolicy", joinPolicy);
                if (participantIds != null)
                    writer.WriteArray("participantIds", participantIds, (w, id) => w.WriteGuid(id));
            });

            return SendAsync(request, "chat.createGroupConversation", StarhermitConversation.Read, cancellationToken);
        }

        /// <summary>Renames a conversation.</summary>
        /// <param name="conversationId">The conversation to rename.</param>
        /// <param name="name">The new name.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The updated conversation.</returns>
        public Task<StarhermitConversation> RenameConversationAsync(
            Guid conversationId,
            string name,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Patch($"chat/conversations/{Escape(conversationId)}"), writer => writer.Write("name", name)),
                "chat.renameConversation",
                StarhermitConversation.Read,
                cancellationToken);

        /// <summary>Invites an account to a conversation.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="toUserId">Who to invite.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The invitation.</returns>
        public Task<StarhermitChatInvite> InviteAsync(
            Guid conversationId,
            Guid toUserId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post($"chat/conversations/{Escape(conversationId)}/invites"), writer => writer.Write("toUserId", toUserId)),
                "chat.invite",
                StarhermitChatInvite.Read,
                cancellationToken);

        /// <summary>Lists invitations addressed to the caller.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The invitations.</returns>
        public async Task<IReadOnlyList<StarhermitChatInvite>> GetInvitesAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("chat/invites"), "chat.getInvites", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitChatInvite.Read);
        }

        /// <summary>Accepts an invitation and joins the conversation.</summary>
        /// <param name="inviteId">The invitation to accept.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The conversation that was joined.</returns>
        public Task<StarhermitConversation> AcceptInviteAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"chat/invites/{Escape(inviteId)}/accept"),
                "chat.acceptInvite",
                StarhermitConversation.Read,
                cancellationToken);

        /// <summary>Declines an invitation.</summary>
        /// <param name="inviteId">The invitation to decline.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The closed invitation.</returns>
        public Task<StarhermitChatInvite> DeclineInviteAsync(Guid inviteId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"chat/invites/{Escape(inviteId)}/decline"),
                "chat.declineInvite",
                StarhermitChatInvite.Read,
                cancellationToken);

        /// <summary>Lists rooms the caller may join without an invitation.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The joinable rooms.</returns>
        public async Task<IReadOnlyList<StarhermitJoinableRoom>> GetJoinableRoomsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("chat/rooms/joinable"), "chat.getJoinableRooms", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitJoinableRoom.Read);
        }

        /// <summary>Joins an open room.</summary>
        /// <param name="conversationId">The room to join.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The conversation that was joined.</returns>
        public Task<StarhermitConversation> JoinRoomAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"chat/conversations/{Escape(conversationId)}/join"),
                "chat.joinRoom",
                StarhermitConversation.Read,
                cancellationToken);

        /// <summary>Adds participants to a conversation.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="participantIds">Accounts to add.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The updated conversation.</returns>
        public Task<StarhermitConversation> AddParticipantsAsync(
            Guid conversationId,
            IEnumerable<Guid> participantIds,
            CancellationToken cancellationToken = default)
        {
            if (participantIds == null) throw new ArgumentNullException(nameof(participantIds));
            var request = WithBody(
                Post($"chat/conversations/{Escape(conversationId)}/participants"),
                writer => writer.WriteArray("participantIds", participantIds, (w, id) => w.WriteGuid(id)));

            return SendAsync(request, "chat.addParticipants", StarhermitConversation.Read, cancellationToken);
        }

        /// <summary>Removes a participant from a conversation.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="userId">The participant to remove.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once they are removed.</returns>
        public Task RemoveParticipantAsync(
            Guid conversationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"chat/conversations/{Escape(conversationId)}/participants/{Escape(userId)}"),
                "chat.removeParticipant",
                cancellationToken);

        /// <summary>Leaves a conversation.</summary>
        /// <param name="conversationId">The conversation to leave.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the caller has left.</returns>
        public Task LeaveConversationAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"chat/conversations/{Escape(conversationId)}/leave"),
                "chat.leaveConversation",
                cancellationToken);

        /// <summary>Lists the caller's conversations.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The conversations.</returns>
        public async Task<IReadOnlyList<StarhermitConversation>> GetConversationsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("chat/conversations"), "chat.getConversations", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitConversation.Read);
        }

        /// <summary>Reads one conversation.</summary>
        /// <param name="conversationId">The conversation to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The conversation.</returns>
        public Task<StarhermitConversation> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"chat/conversations/{Escape(conversationId)}"),
                "chat.getConversation",
                StarhermitConversation.Read,
                cancellationToken);

        /// <summary>Reads unread totals across all conversations.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The unread summary.</returns>
        public Task<StarhermitUnreadSummary> GetUnreadAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Get("chat/unread"), "chat.getUnread", StarhermitUnreadSummary.Read, cancellationToken);

        /// <summary>Marks a conversation as read up to now.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the read marker moves.</returns>
        public Task MarkReadAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"chat/conversations/{Escape(conversationId)}/read"), "chat.markRead", cancellationToken);

        /// <summary>Reads one page of messages.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The page of messages.</returns>
        public async Task<StarhermitPage<StarhermitMessage>> GetMessagesAsync(
            Guid conversationId,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var request = Get($"chat/conversations/{Escape(conversationId)}/messages")
                .WithQuery("page", page)
                .WithQuery("pageSize", pageSize);

            var json = await SendJsonAsync(request, "chat.getMessages", cancellationToken).ConfigureAwait(false);
            return StarhermitPage<StarhermitMessage>.Read(json, StarhermitMessage.Read);
        }

        /// <summary>Enumerates every message in a conversation, fetching pages as they are consumed.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels enumeration.</param>
        /// <returns>An asynchronous sequence of messages.</returns>
        public IAsyncEnumerable<StarhermitMessage> EnumerateMessagesAsync(
            Guid conversationId,
            int pageSize = 50,
            CancellationToken cancellationToken = default) =>
            EnumeratePagesAsync(
                (page, token) => GetMessagesAsync(conversationId, page, pageSize, token),
                cancellationToken);

        /// <summary>Posts a message.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="content">Message text.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The stored message, with the server's id and timestamp.</returns>
        public Task<StarhermitMessage> SendMessageAsync(
            Guid conversationId,
            string content,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post($"chat/conversations/{Escape(conversationId)}/messages"), writer => writer.Write("content", content)),
                "chat.sendMessage",
                StarhermitMessage.Read,
                cancellationToken);

        /// <summary>Edits a message the caller sent.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="messageId">The message to edit.</param>
        /// <param name="content">The new text.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The updated message.</returns>
        public Task<StarhermitMessage> EditMessageAsync(
            Guid conversationId,
            Guid messageId,
            string content,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(
                    Put($"chat/conversations/{Escape(conversationId)}/messages/{Escape(messageId)}"),
                    writer => writer.Write("content", content)),
                "chat.editMessage",
                StarhermitMessage.Read,
                cancellationToken);

        /// <summary>Deletes a message the caller sent.</summary>
        /// <param name="conversationId">The conversation.</param>
        /// <param name="messageId">The message to delete.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The deleted message as the server now reports it.</returns>
        public Task<StarhermitMessage> DeleteMessageAsync(
            Guid conversationId,
            Guid messageId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"chat/conversations/{Escape(conversationId)}/messages/{Escape(messageId)}"),
                "chat.deleteMessage",
                StarhermitMessage.Read,
                cancellationToken);
    }
}
