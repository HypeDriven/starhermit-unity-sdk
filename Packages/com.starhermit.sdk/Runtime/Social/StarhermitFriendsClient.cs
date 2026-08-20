using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>Friend requests and the friend list.</summary>
    public sealed class StarhermitFriendsClient : StarhermitServiceClient
    {
        internal StarhermitFriendsClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Sends a friend request.</summary>
        /// <param name="toUserId">Account to invite.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the request is recorded.</returns>
        public Task SendRequestAsync(Guid toUserId, CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post("me/friend-requests"), writer => writer.Write("toUserId", toUserId)),
                "friends.sendRequest",
                cancellationToken);

        /// <summary>Lists friend requests awaiting an answer.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The pending requests.</returns>
        public async Task<IReadOnlyList<StarhermitFriendRequest>> GetRequestsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/friend-requests"), "friends.getRequests", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitFriendRequest.Read);
        }

        /// <summary>Accepts a friend request.</summary>
        /// <param name="requestId">The request to accept.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the friendship exists.</returns>
        public Task AcceptRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"me/friend-requests/{Escape(requestId)}/accept"),
                "friends.acceptRequest",
                cancellationToken);

        /// <summary>Declines a friend request.</summary>
        /// <param name="requestId">The request to decline.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the request is closed.</returns>
        public Task DeclineRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"me/friend-requests/{Escape(requestId)}/decline"),
                "friends.declineRequest",
                cancellationToken);

        /// <summary>Removes a friend.</summary>
        /// <param name="friendUserId">The account to unfriend.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the friendship is gone.</returns>
        public Task RemoveFriendAsync(Guid friendUserId, CancellationToken cancellationToken = default) =>
            SendAsync(Delete($"me/friends/{Escape(friendUserId)}"), "friends.removeFriend", cancellationToken);

        /// <summary>Lists friends with the presence the viewer is allowed to see.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The friend list.</returns>
        public async Task<IReadOnlyList<StarhermitFriend>> GetFriendsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/friends"), "friends.getFriends", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitFriend.Read);
        }
    }
}
