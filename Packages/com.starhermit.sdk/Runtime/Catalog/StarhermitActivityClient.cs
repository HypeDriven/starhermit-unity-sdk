using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// Playtime and activity: ending launches, reading playtime, and the personal, friends, public and
    /// per-game feeds.
    /// </summary>
    /// <remarks>
    /// External-library providers are reported by the deployment. The SDK does not present the
    /// backend's provider seam as a guaranteed store integration - whether a provider actually returns
    /// a real library is the deployment's business, not a promise the package makes.
    /// </remarks>
    public sealed class StarhermitActivityClient : StarhermitServiceClient
    {
        internal StarhermitActivityClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Ends a launch session started through the catalog.</summary>
        /// <param name="launchId">The launch to end.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the session is closed.</returns>
        public Task EndLaunchAsync(Guid launchId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"activity/launches/{Escape(launchId)}/end"), "activity.endLaunch", cancellationToken);

        /// <summary>Reads the caller's playtime on a catalog title.</summary>
        /// <param name="softwareTitleId">The title.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The playtime totals.</returns>
        public Task<StarhermitPlaytime> GetPlaytimeAsync(Guid softwareTitleId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"activity/software/{Escape(softwareTitleId)}/playtime"),
                "activity.getPlaytime",
                StarhermitPlaytime.Read,
                cancellationToken);

        /// <summary>Reads friends' playtime on a catalog title.</summary>
        /// <param name="softwareTitleId">The title.</param>
        /// <param name="top">How many friends to return.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The friends with the most playtime, as their privacy settings allow.</returns>
        public async Task<IReadOnlyList<StarhermitFriendPlaytime>> GetFriendsPlaytimeAsync(
            Guid softwareTitleId,
            int top = 5,
            CancellationToken cancellationToken = default)
        {
            var request = Get($"activity/software/{Escape(softwareTitleId)}/friends-playtime").WithQuery("top", top);
            var json = await SendJsonAsync(request, "activity.getFriendsPlaytime", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitFriendPlaytime.Read);
        }

        /// <summary>Records the start of a launch for an externally owned title.</summary>
        /// <param name="provider">Provider key.</param>
        /// <param name="externalSoftwareId">The provider's id for the title.</param>
        /// <param name="name">Title name, when the caller knows it.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The launch id, needed to end the session.</returns>
        public async Task<Guid> StartExternalLaunchAsync(
            string provider,
            string externalSoftwareId,
            string? name = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("activity/external-launches"), writer =>
            {
                writer.Write("provider", provider);
                writer.Write("externalSoftwareId", externalSoftwareId);
                writer.WriteIfPresent("name", name);
            });

            var json = await SendJsonAsync(request, "activity.startExternalLaunch", cancellationToken).ConfigureAwait(false);
            return json["launchId"].AsGuidOrNull() ?? Guid.Empty;
        }

        /// <summary>Ends an external launch session.</summary>
        /// <param name="launchId">The launch to end.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the session is closed.</returns>
        public Task EndExternalLaunchAsync(Guid launchId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"activity/external-launches/{Escape(launchId)}/end"),
                "activity.endExternalLaunch",
                cancellationToken);

        /// <summary>Reads the caller's playtime on an externally owned title.</summary>
        /// <param name="provider">Provider key.</param>
        /// <param name="externalId">The provider's id for the title.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The playtime totals.</returns>
        public Task<StarhermitPlaytime> GetExternalPlaytimeAsync(
            string provider,
            string externalId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                Get("activity/external/playtime").WithQuery("provider", provider).WithQuery("externalId", externalId),
                "activity.getExternalPlaytime",
                StarhermitPlaytime.Read,
                cancellationToken);

        /// <summary>Reads friends' playtime on an externally owned title.</summary>
        /// <param name="provider">Provider key.</param>
        /// <param name="externalId">The provider's id for the title.</param>
        /// <param name="top">How many friends to return.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The friends with the most playtime.</returns>
        public async Task<IReadOnlyList<StarhermitFriendPlaytime>> GetExternalFriendsPlaytimeAsync(
            string provider,
            string externalId,
            int top = 5,
            CancellationToken cancellationToken = default)
        {
            var request = Get("activity/external/friends-playtime")
                .WithQuery("provider", provider)
                .WithQuery("externalId", externalId)
                .WithQuery("top", top);

            var json = await SendJsonAsync(request, "activity.getExternalFriendsPlaytime", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitFriendPlaytime.Read);
        }

        /// <summary>Reads a game's public activity feed by uniform game key.</summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="limit">Maximum items to return.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The feed items.</returns>
        public async Task<IReadOnlyList<StarhermitGameFeedItem>> GetGameFeedAsync(
            string gameKey,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            var request = Get("activity/game-feed")
                .WithCredential(StarhermitCredential.AccountOptional)
                .WithQuery("key", gameKey)
                .WithQuery("limit", limit);

            var json = await SendJsonAsync(request, "activity.getGameFeed", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitGameFeedItem.Read);
        }

        /// <summary>Reads the caller's own activity feed.</summary>
        /// <param name="since">Earliest activity to include.</param>
        /// <param name="until">Latest activity to include.</param>
        /// <param name="limit">Maximum items to return.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The activity items.</returns>
        public async Task<IReadOnlyList<StarhermitActivity>> GetMyActivityAsync(
            DateTimeOffset? since = null,
            DateTimeOffset? until = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            var request = Get("me/activity")
                .WithQuery("since", since)
                .WithQuery("until", until)
                .WithQuery("limit", limit);

            var json = await SendJsonAsync(request, "activity.getMyActivity", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitActivity.Read);
        }

        /// <summary>Reads friends' activity.</summary>
        /// <param name="since">Earliest activity to include.</param>
        /// <param name="until">Latest activity to include.</param>
        /// <param name="limit">Maximum items to return.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The activity items.</returns>
        public async Task<IReadOnlyList<StarhermitActivity>> GetFriendActivityAsync(
            DateTimeOffset? since = null,
            DateTimeOffset? until = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            var request = Get("activity/friends")
                .WithQuery("since", since)
                .WithQuery("until", until)
                .WithQuery("limit", limit);

            var json = await SendJsonAsync(request, "activity.getFriendActivity", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitActivity.Read);
        }

        /// <summary>Reads the public activity feed.</summary>
        /// <param name="since">Earliest activity to include.</param>
        /// <param name="until">Latest activity to include.</param>
        /// <param name="limit">Maximum items to return.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The activity items.</returns>
        public async Task<IReadOnlyList<StarhermitActivity>> GetPublicActivityAsync(
            DateTimeOffset? since = null,
            DateTimeOffset? until = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            var request = Get("activity/public")
                .WithCredential(StarhermitCredential.AccountOptional)
                .WithQuery("since", since)
                .WithQuery("until", until)
                .WithQuery("limit", limit);

            var json = await SendJsonAsync(request, "activity.getPublicActivity", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitActivity.Read);
        }

        /// <summary>Lists the account's external-library links.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The links.</returns>
        public async Task<IReadOnlyList<StarhermitExternalLibraryLink>> GetExternalLibrariesAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/external-libraries"), "activity.getExternalLibraries", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitExternalLibraryLink.Read);
        }

        /// <summary>Links an external library to the account.</summary>
        /// <param name="provider">Provider key.</param>
        /// <param name="externalUserId">The account's id at that provider.</param>
        /// <param name="accessToken">Provider access token. Treated as a credential and never logged.</param>
        /// <param name="refreshToken">Provider refresh token, when there is one.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the link exists.</returns>
        public Task LinkExternalLibraryAsync(
            string provider,
            string externalUserId,
            string accessToken,
            string? refreshToken = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("me/external-libraries/link"), writer =>
            {
                writer.Write("provider", provider);
                writer.Write("externalUserId", externalUserId);
                writer.Write("accessToken", accessToken);
                writer.WriteIfPresent("refreshToken", refreshToken);
            });

            return SendAsync(request, "activity.linkExternalLibrary", cancellationToken);
        }

        /// <summary>Removes an external-library link.</summary>
        /// <param name="provider">Provider key.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the link is gone.</returns>
        public Task UnlinkExternalLibraryAsync(string provider, CancellationToken cancellationToken = default) =>
            SendAsync(Delete($"me/external-libraries/{Escape(provider)}"), "activity.unlinkExternalLibrary", cancellationToken);

        /// <summary>Lists titles the account owns on external providers.</summary>
        /// <param name="provider">Restrict to one provider.</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A page of external ownerships.</returns>
        public async Task<StarhermitPage<StarhermitExternalOwnership>> GetExternalSoftwareAsync(
            string? provider = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var request = Get("me/external-software")
                .WithQuery("provider", provider)
                .WithQuery("page", page)
                .WithQuery("pageSize", pageSize);

            var json = await SendJsonAsync(request, "activity.getExternalSoftware", cancellationToken).ConfigureAwait(false);
            return StarhermitPage<StarhermitExternalOwnership>.Read(json, StarhermitExternalOwnership.Read);
        }

        /// <summary>Launches an externally owned title and records the activity.</summary>
        /// <param name="ownershipId">The ownership row to launch.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The provider URI the platform should open, when it supplies one.</returns>
        public async Task<string?> LaunchExternalAsync(Guid ownershipId, CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(
                    Post($"external-software/{Escape(ownershipId)}/launch"),
                    "activity.launchExternal",
                    cancellationToken)
                .ConfigureAwait(false);
            return json["launchUri"].AsStringOrNull();
        }
    }
}
