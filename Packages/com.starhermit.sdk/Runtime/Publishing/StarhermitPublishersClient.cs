using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// The publisher surface: publishers and members, titles and builds, entitlement grants,
    /// achievement and leaderboard definitions, and download/launch analytics.
    /// </summary>
    /// <remarks>
    /// Every operation here needs publisher permissions the account either has or does not; the SDK
    /// passes the server's refusal through rather than pre-checking it, because only the server knows
    /// what a membership currently grants.
    /// </remarks>
    public sealed class StarhermitPublishersClient : StarhermitServiceClient
    {
        internal StarhermitPublishersClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Creates a publisher owned by the caller.</summary>
        /// <param name="name">Display name.</param>
        /// <param name="description">Description.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new publisher.</returns>
        public Task<StarhermitPublisher> CreateAsync(
            string name,
            string description,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("publisher"), writer =>
            {
                writer.Write("name", name);
                writer.Write("description", description);
            });

            return SendAsync(request, "publishers.create", StarhermitPublisher.Read, cancellationToken);
        }

        /// <summary>Lists publishers the caller belongs to.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The publishers.</returns>
        public async Task<IReadOnlyList<StarhermitPublisher>> GetMineAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("publisher"), "publishers.getMine", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitPublisher.Read);
        }

        /// <summary>Adds a member to a publisher.</summary>
        /// <param name="publisherId">The publisher.</param>
        /// <param name="userId">The account to add.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the membership exists.</returns>
        public Task AddMemberAsync(Guid publisherId, Guid userId, CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post($"publisher/{Escape(publisherId)}/members"), writer => writer.Write("userId", userId)),
                "publishers.addMember",
                cancellationToken);

        /// <summary>Removes a member from a publisher.</summary>
        /// <param name="publisherId">The publisher.</param>
        /// <param name="memberUserId">The member to remove.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the membership is gone.</returns>
        public Task RemoveMemberAsync(Guid publisherId, Guid memberUserId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"publisher/{Escape(publisherId)}/members/{Escape(memberUserId)}"),
                "publishers.removeMember",
                cancellationToken);

        /// <summary>Reads one membership.</summary>
        /// <param name="publisherId">The publisher.</param>
        /// <param name="memberUserId">The member.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The membership.</returns>
        public Task<StarhermitPublisherMember> GetMemberAsync(
            Guid publisherId,
            Guid memberUserId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"publisher/{Escape(publisherId)}/members/{Escape(memberUserId)}"),
                "publishers.getMember",
                StarhermitPublisherMember.Read,
                cancellationToken);

        /// <summary>
        /// Creates or updates a catalog title. The server decides which by whether the payload carries
        /// an id it already knows.
        /// </summary>
        /// <param name="title">The title to store.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The stored title.</returns>
        public Task<StarhermitSoftwareTitle> CreateOrUpdateTitleAsync(
            StarhermitTitleDraft title,
            CancellationToken cancellationToken = default)
        {
            if (title == null) throw new ArgumentNullException(nameof(title));
            return SendAsync(
                WithBody(Post("publisher/software"), title.Write),
                "publishers.createOrUpdateTitle",
                StarhermitSoftwareTitle.Read,
                cancellationToken);
        }

        /// <summary>Requests signed upload targets for a title's next build.</summary>
        /// <param name="titleId">The title.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>One target per asset the platform expects.</returns>
        public async Task<IReadOnlyList<StarhermitUploadTarget>> GenerateUploadTargetsAsync(
            Guid titleId,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("publisher/software/upload"), writer => writer.Write("titleId", titleId));
            var json = await SendJsonAsync(request, "publishers.generateUploadTargets", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitUploadTarget.Read);
        }

        /// <summary>Finalises a build once its assets have been uploaded.</summary>
        /// <param name="titleId">The title.</param>
        /// <param name="version">Version string.</param>
        /// <param name="releaseNotes">Release notes.</param>
        /// <param name="assets">The uploaded assets.</param>
        /// <param name="buildId">Existing build to finalise, when replacing one.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the build is published.</returns>
        public Task FinalizeBuildAsync(
            Guid titleId,
            string version,
            string releaseNotes,
            IEnumerable<StarhermitAssetDescriptor> assets,
            Guid? buildId = null,
            CancellationToken cancellationToken = default)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            var request = WithBody(Post("publisher/software/build/finalize"), writer =>
            {
                writer.Write("titleId", titleId);
                writer.WriteIfPresent("buildId", buildId);
                writer.Write("version", version);
                writer.Write("releaseNotes", releaseNotes);
                writer.WriteArray("assets", assets, (w, asset) => asset.Write(w));
            });

            return SendAsync(request, "publishers.finalizeBuild", cancellationToken);
        }

        /// <summary>Reads download counts per title.</summary>
        /// <param name="publisherId">The publisher.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>Download counts keyed by title id.</returns>
        public Task<IReadOnlyDictionary<Guid, int>> GetDownloadAnalyticsAsync(
            Guid publisherId,
            CancellationToken cancellationToken = default) =>
            ReadAnalyticsAsync(
                Get("publisher/analytics/downloads").WithQuery("publisherId", publisherId),
                "publishers.getDownloadAnalytics",
                cancellationToken);

        /// <summary>Reads launch counts per title.</summary>
        /// <param name="publisherId">The publisher.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>Launch counts keyed by title id.</returns>
        public Task<IReadOnlyDictionary<Guid, int>> GetLaunchAnalyticsAsync(
            Guid publisherId,
            CancellationToken cancellationToken = default) =>
            ReadAnalyticsAsync(
                Get("publisher/analytics/launches").WithQuery("publisherId", publisherId),
                "publishers.getLaunchAnalytics",
                cancellationToken);

        /// <summary>Grants an entitlement to a player.</summary>
        /// <param name="userId">The account to grant to.</param>
        /// <param name="softwareTitleId">The title to grant.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the entitlement exists.</returns>
        public Task GrantEntitlementAsync(
            Guid userId,
            Guid softwareTitleId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post("publisher/entitlements/grant"), writer =>
                {
                    writer.Write("userId", userId);
                    writer.Write("softwareTitleId", softwareTitleId);
                }),
                "publishers.grantEntitlement",
                cancellationToken);

        /// <summary>Revokes an entitlement.</summary>
        /// <param name="userId">The account to revoke from.</param>
        /// <param name="softwareTitleId">The title to revoke.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the entitlement is revoked.</returns>
        public Task RevokeEntitlementAsync(
            Guid userId,
            Guid softwareTitleId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post("publisher/entitlements/revoke"), writer =>
                {
                    writer.Write("userId", userId);
                    writer.Write("softwareTitleId", softwareTitleId);
                }),
                "publishers.revokeEntitlement",
                cancellationToken);

        /// <summary>Creates an achievement definition on a title.</summary>
        /// <param name="titleId">The title.</param>
        /// <param name="key">Stable key.</param>
        /// <param name="name">Display name.</param>
        /// <param name="description">Description.</param>
        /// <param name="points">Point value.</param>
        /// <param name="icon">Icon reference.</param>
        /// <param name="isSecret">Whether it is hidden until unlocked.</param>
        /// <param name="visibility">Visibility rule.</param>
        /// <param name="criteria">Criteria description.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new definition.</returns>
        public Task<StarhermitAchievement> CreateAchievementAsync(
            Guid titleId,
            string key,
            string name,
            string description,
            int points = 0,
            string? icon = null,
            bool isSecret = false,
            string? visibility = null,
            string? criteria = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post($"publisher/titles/{Escape(titleId)}/achievements"), writer =>
            {
                writer.Write("key", key);
                writer.Write("name", name);
                writer.Write("description", description);
                writer.WriteIfPresent("icon", icon);
                writer.Write("secret", isSecret);
                writer.Write("points", points);
                writer.WriteIfPresent("visibility", visibility);
                writer.WriteIfPresent("criteria", criteria);
            });

            return SendAsync(request, "publishers.createAchievement", StarhermitAchievement.Read, cancellationToken);
        }

        /// <summary>Lists a title's achievement definitions.</summary>
        /// <param name="titleId">The title.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The definitions.</returns>
        public async Task<IReadOnlyList<StarhermitAchievement>> ListAchievementsAsync(
            Guid titleId,
            CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(
                    Get($"publisher/titles/{Escape(titleId)}/achievements"),
                    "publishers.listAchievements",
                    cancellationToken)
                .ConfigureAwait(false);
            return json.AsList(StarhermitAchievement.Read);
        }

        /// <summary>Reads one achievement definition.</summary>
        /// <param name="achievementId">The definition to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The definition.</returns>
        public Task<StarhermitAchievement> GetAchievementAsync(Guid achievementId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"publisher/achievements/{Escape(achievementId)}"),
                "publishers.getAchievement",
                StarhermitAchievement.Read,
                cancellationToken);

        /// <summary>Updates an achievement definition.</summary>
        /// <param name="achievementId">The definition to update.</param>
        /// <param name="update">Fields to change.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The updated definition.</returns>
        public Task<StarhermitAchievement> UpdateAchievementAsync(
            Guid achievementId,
            StarhermitAchievementUpdate update,
            CancellationToken cancellationToken = default)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            return SendAsync(
                WithBody(Put($"publisher/achievements/{Escape(achievementId)}"), update.Write),
                "publishers.updateAchievement",
                StarhermitAchievement.Read,
                cancellationToken);
        }

        /// <summary>Deletes an achievement definition.</summary>
        /// <param name="achievementId">The definition to delete.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once it is gone.</returns>
        public Task DeleteAchievementAsync(Guid achievementId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"publisher/achievements/{Escape(achievementId)}"),
                "publishers.deleteAchievement",
                cancellationToken);

        /// <summary>Creates a leaderboard definition.</summary>
        /// <param name="definition">The definition to create.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new leaderboard.</returns>
        public Task<StarhermitLeaderboard> CreateLeaderboardAsync(
            StarhermitLeaderboardDefinition definition,
            CancellationToken cancellationToken = default)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return SendAsync(
                WithBody(Post("publisher/leaderboards"), definition.Write),
                "publishers.createLeaderboard",
                StarhermitLeaderboard.Read,
                cancellationToken);
        }

        /// <summary>Reads one leaderboard definition through the publisher surface.</summary>
        /// <param name="leaderboardId">The board to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The definition.</returns>
        public Task<StarhermitLeaderboard> GetLeaderboardAsync(Guid leaderboardId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"publisher/leaderboards/{Escape(leaderboardId)}"),
                "publishers.getLeaderboard",
                StarhermitLeaderboard.Read,
                cancellationToken);

        /// <summary>Updates a leaderboard definition.</summary>
        /// <param name="leaderboardId">The board to update.</param>
        /// <param name="definition">Fields to change.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The updated definition.</returns>
        public Task<StarhermitLeaderboard> UpdateLeaderboardAsync(
            Guid leaderboardId,
            StarhermitLeaderboardDefinition definition,
            CancellationToken cancellationToken = default)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return SendAsync(
                WithBody(Put($"publisher/leaderboards/{Escape(leaderboardId)}"), definition.Write),
                "publishers.updateLeaderboard",
                StarhermitLeaderboard.Read,
                cancellationToken);
        }

        /// <summary>Deletes a leaderboard definition.</summary>
        /// <param name="leaderboardId">The board to delete.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once it is gone.</returns>
        public Task DeleteLeaderboardAsync(Guid leaderboardId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"publisher/leaderboards/{Escape(leaderboardId)}"),
                "publishers.deleteLeaderboard",
                cancellationToken);

        private async Task<IReadOnlyDictionary<Guid, int>> ReadAnalyticsAsync(
            StarhermitRequest request,
            string operationId,
            CancellationToken cancellationToken)
        {
            var json = await SendJsonAsync(request, operationId, cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<Guid, int>(json.Count);
            foreach (var member in json.Members)
                if (Guid.TryParse(member.Key, out var titleId))
                    result[titleId] = member.Value.AsInt32OrDefault();
            return result;
        }
    }

    /// <summary>A catalog title to create or update through the publisher surface.</summary>
    public sealed class StarhermitTitleDraft
    {
        /// <summary>Existing title id, when updating.</summary>
        public Guid? Id { get; set; }

        /// <summary>Display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Store description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Publisher that owns it.</summary>
        public Guid PublisherId { get; set; }

        /// <summary>Catalog category.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Target platform.</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>Release status.</summary>
        public string ReleaseStatus { get; set; } = string.Empty;

        /// <summary>Store tags.</summary>
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

        /// <summary>Price in minor units. Zero makes the title free to claim.</summary>
        public int PriceCents { get; set; }

        /// <summary>Writes the draft as the API's request body.</summary>
        /// <param name="writer">Writer positioned inside the request object.</param>
        public void Write(Json.JsonWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (Id.HasValue) writer.Write("id", Id.Value);
            writer.Write("name", Name);
            writer.Write("description", Description);
            writer.Write("publisherId", PublisherId);
            writer.Write("category", Category);
            writer.Write("platform", Platform);
            writer.Write("releaseStatus", ReleaseStatus);
            writer.WriteArray("tags", Tags, (w, tag) => w.WriteString(tag));
            writer.Write("priceCents", PriceCents);
        }
    }
}
