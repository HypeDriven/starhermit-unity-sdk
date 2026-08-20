using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>Star ratings and written reviews, keyed by the platform's uniform game key.</summary>
    public sealed class StarhermitRatingsClient : StarhermitServiceClient
    {
        internal StarhermitRatingsClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Stores or replaces the caller's rating for a game.</summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="stars">Score from 0 to 5.</param>
        /// <param name="review">Optional review text.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The updated aggregate for that game.</returns>
        public Task<StarhermitRatingSummary> RateAsync(
            string gameKey,
            int stars,
            string? review = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Put("me/ratings"), writer =>
            {
                writer.Write("gameKey", gameKey);
                writer.Write("stars", stars);
                writer.WriteIfPresent("review", review);
            });

            return SendAsync(request, "ratings.rate", StarhermitRatingSummary.Read, cancellationToken);
        }

        /// <summary>Reads rating aggregates for several games at once.</summary>
        /// <param name="gameKeys">The uniform game keys to query.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>One summary per key the server knows about.</returns>
        public async Task<IReadOnlyList<StarhermitRatingSummary>> QueryAsync(
            IEnumerable<string> gameKeys,
            CancellationToken cancellationToken = default)
        {
            if (gameKeys == null) throw new ArgumentNullException(nameof(gameKeys));
            var request = WithBody(Post("ratings/query"), writer => writer.WriteArray("keys", gameKeys, (w, key) => w.WriteString(key)))
                .WithCredential(StarhermitCredential.AccountOptional);

            var json = await SendJsonAsync(request, "ratings.query", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitRatingSummary.Read);
        }

        /// <summary>Reads written reviews for a game.</summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="top">How many reviews to return.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The reviews.</returns>
        public async Task<IReadOnlyList<StarhermitReview>> GetReviewsAsync(
            string gameKey,
            int top = 10,
            CancellationToken cancellationToken = default)
        {
            var request = Get("ratings/reviews")
                .WithCredential(StarhermitCredential.AccountOptional)
                .WithQuery("key", gameKey)
                .WithQuery("top", top);

            var json = await SendJsonAsync(request, "ratings.getReviews", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitReview.Read);
        }
    }

    /// <summary>The account's wishlist.</summary>
    /// <remarks>Adding and removing are idempotent, exactly as the API defines them.</remarks>
    public sealed class StarhermitWishlistClient : StarhermitServiceClient
    {
        internal StarhermitWishlistClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Lists the wishlisted title ids.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The title ids.</returns>
        public async Task<IReadOnlyList<Guid>> ListAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/wishlist"), "wishlist.list", cancellationToken).ConfigureAwait(false);
            var ids = new List<Guid>(json["titleIds"].Count);
            foreach (var item in json["titleIds"].AsArray())
            {
                var id = item.AsGuidOrNull();
                if (id.HasValue) ids.Add(id.Value);
            }

            return ids;
        }

        /// <summary>Adds a title to the wishlist. Adding one that is already there is not an error.</summary>
        /// <param name="titleId">The title to add.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the title is on the list.</returns>
        public Task AddAsync(Guid titleId, CancellationToken cancellationToken = default) =>
            SendAsync(Put($"me/wishlist/{Escape(titleId)}"), "wishlist.add", cancellationToken);

        /// <summary>Removes a title from the wishlist. Removing one that is absent is not an error.</summary>
        /// <param name="titleId">The title to remove.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the title is off the list.</returns>
        public Task RemoveAsync(Guid titleId, CancellationToken cancellationToken = default) =>
            SendAsync(Delete($"me/wishlist/{Escape(titleId)}"), "wishlist.remove", cancellationToken);
    }

    /// <summary>Achievements the account can see and, where the definition allows, claim.</summary>
    /// <remarks>
    /// Achievements attached to an authoritative game are unlocked by the game's own server-side logic.
    /// A client that tries to claim one gets the API's refusal, and the SDK surfaces it rather than
    /// pretending the unlock happened.
    /// </remarks>
    public sealed class StarhermitAchievementsClient : StarhermitServiceClient
    {
        internal StarhermitAchievementsClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Lists the account's unlocked achievements.</summary>
        /// <param name="titleId">Restrict to one catalog title.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The unlocks.</returns>
        public async Task<IReadOnlyList<StarhermitUnlockedAchievement>> GetMineAsync(
            Guid? titleId = null,
            CancellationToken cancellationToken = default)
        {
            var request = Get("me/achievements").WithQuery("titleId", titleId);
            var json = await SendJsonAsync(request, "achievements.getMine", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitUnlockedAchievement.Read);
        }

        /// <summary>
        /// Unlocks a client-claimable achievement. The call is idempotent: unlocking one the account
        /// already holds succeeds without creating a second unlock.
        /// </summary>
        /// <param name="achievementDefinitionId">The definition to unlock.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the unlock is recorded.</returns>
        public Task UnlockAsync(Guid achievementDefinitionId, CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post("me/achievements/unlock"), writer => writer.Write("achievementDefinitionId", achievementDefinitionId)),
                "achievements.unlock",
                cancellationToken);
    }

    /// <summary>Leaderboard definitions, entries and score submission.</summary>
    public sealed class StarhermitLeaderboardsClient : StarhermitServiceClient
    {
        internal StarhermitLeaderboardsClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Lists leaderboard definitions.</summary>
        /// <param name="scope">Restrict to one scope.</param>
        /// <param name="region">Restrict to one region.</param>
        /// <param name="titleId">Restrict to one catalog title.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The definitions.</returns>
        public async Task<IReadOnlyList<StarhermitLeaderboard>> GetLeaderboardsAsync(
            string? scope = null,
            string? region = null,
            Guid? titleId = null,
            CancellationToken cancellationToken = default)
        {
            var request = Get("leaderboards")
                .WithCredential(StarhermitCredential.AccountOptional)
                .WithQuery("scope", scope)
                .WithQuery("region", region)
                .WithQuery("titleId", titleId);

            var json = await SendJsonAsync(request, "leaderboards.list", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitLeaderboard.Read);
        }

        /// <summary>Reads one leaderboard definition.</summary>
        /// <param name="leaderboardId">The board to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The definition.</returns>
        public Task<StarhermitLeaderboard> GetLeaderboardAsync(Guid leaderboardId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"leaderboards/{Escape(leaderboardId)}").WithCredential(StarhermitCredential.AccountOptional),
                "leaderboards.get",
                StarhermitLeaderboard.Read,
                cancellationToken);

        /// <summary>Reads a page of entries.</summary>
        /// <param name="leaderboardId">The board.</param>
        /// <param name="scope">Restrict to one scope.</param>
        /// <param name="region">Restrict to one region.</param>
        /// <param name="friendsOnly">Only include the caller's friends.</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A page of entries with server-assigned ranks.</returns>
        public async Task<StarhermitPage<StarhermitLeaderboardEntry>> GetEntriesAsync(
            Guid leaderboardId,
            string? scope = null,
            string? region = null,
            bool friendsOnly = false,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var request = Get($"leaderboards/{Escape(leaderboardId)}/entries")
                .WithCredential(friendsOnly ? StarhermitCredential.Account : StarhermitCredential.AccountOptional)
                .WithQuery("scope", scope)
                .WithQuery("region", region)
                .WithQuery("friendsOnly", friendsOnly ? true : (bool?)null)
                .WithQuery("page", page)
                .WithQuery("pageSize", pageSize);

            var json = await SendJsonAsync(request, "leaderboards.getEntries", cancellationToken).ConfigureAwait(false);
            return StarhermitPage<StarhermitLeaderboardEntry>.Read(json, StarhermitLeaderboardEntry.Read);
        }

        /// <summary>Enumerates every entry, fetching pages as they are consumed.</summary>
        /// <param name="leaderboardId">The board.</param>
        /// <param name="scope">Restrict to one scope.</param>
        /// <param name="region">Restrict to one region.</param>
        /// <param name="friendsOnly">Only include the caller's friends.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels enumeration.</param>
        /// <returns>An asynchronous sequence of entries.</returns>
        public IAsyncEnumerable<StarhermitLeaderboardEntry> EnumerateEntriesAsync(
            Guid leaderboardId,
            string? scope = null,
            string? region = null,
            bool friendsOnly = false,
            int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            EnumeratePagesAsync(
                (page, token) => GetEntriesAsync(leaderboardId, scope, region, friendsOnly, page, pageSize, token),
                cancellationToken);

        /// <summary>
        /// Submits a score, where the definition permits client submission. The server validates it
        /// against the board's own limits and owns the resulting rank.
        /// </summary>
        /// <param name="leaderboardId">The board.</param>
        /// <param name="score">The score to submit.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The stored entry.</returns>
        public Task<StarhermitLeaderboardEntry> SubmitScoreAsync(
            Guid leaderboardId,
            decimal score,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(
                Post($"leaderboards/{Escape(leaderboardId)}/submit"),
                writer =>
                {
                    writer.WritePropertyName("score");
                    writer.WriteNumber(score);
                });

            return SendAsync(request, "leaderboards.submitScore", StarhermitLeaderboardEntry.Read, cancellationToken);
        }
    }
}
