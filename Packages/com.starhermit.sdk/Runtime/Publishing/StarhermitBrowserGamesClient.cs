using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// Browser games submitted from a GitHub repository or uploaded as a bundle: submission,
    /// ownership, cover art, hosting, deployment and bundle uploads - and, for a game's owner, its
    /// achievements and leaderboards, live sessions, player reports and server container output.
    /// </summary>
    /// <remarks>
    /// Bundle uploads stream. The archive is never buffered by the SDK, and the deployment enforces a
    /// per-game byte allowance, answering <c>413</c> when an upload exceeds it - which arrives as a
    /// <see cref="StarhermitApiException"/> carrying the limit the server applied.
    /// </remarks>
    public sealed class StarhermitBrowserGamesClient : StarhermitServiceClient
    {
        internal StarhermitBrowserGamesClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Submits a repository as a browser game.</summary>
        /// <param name="repositoryUrl">The repository URL.</param>
        /// <param name="displayName">Display name, when overriding the repository's.</param>
        /// <param name="launchPath">Entry point within the repository.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The submitted game.</returns>
        public Task<StarhermitBrowserGame> SubmitAsync(
            string repositoryUrl,
            string? displayName = null,
            string? launchPath = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("me/github-games"), writer =>
            {
                writer.Write("repoUrl", repositoryUrl);
                writer.WriteIfPresent("displayName", displayName);
                writer.WriteIfPresent("launchPath", launchPath);
            });

            return SendAsync(request, "browserGames.submit", StarhermitBrowserGame.Read, cancellationToken);
        }

        /// <summary>Claims a submitted game as its verified repository owner.</summary>
        /// <param name="gameId">The game to claim.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The game, now owned by the caller.</returns>
        public Task<StarhermitBrowserGame> ClaimAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"me/github-games/{Escape(gameId)}/claim"),
                "browserGames.claim",
                StarhermitBrowserGame.Read,
                cancellationToken);

        /// <summary>Lists the caller's browser games.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The games.</returns>
        public async Task<IReadOnlyList<StarhermitBrowserGame>> ListMineAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/github-games"), "browserGames.listMine", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitBrowserGame.Read);
        }

        /// <summary>Lists every published browser game.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The games.</returns>
        public async Task<IReadOnlyList<StarhermitBrowserGame>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            var request = Get("github-games").WithCredential(StarhermitCredential.AccountOptional);
            var json = await SendJsonAsync(request, "browserGames.listAll", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitBrowserGame.Read);
        }

        /// <summary>Transfers a game to another account.</summary>
        /// <param name="gameId">The game to transfer.</param>
        /// <param name="toUserId">The new owner.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The transferred game.</returns>
        public Task<StarhermitBrowserGame> TransferAsync(
            Guid gameId,
            Guid toUserId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post($"me/github-games/{Escape(gameId)}/transfer"), writer => writer.Write("toUserId", toUserId)),
                "browserGames.transfer",
                StarhermitBrowserGame.Read,
                cancellationToken);

        /// <summary>Deletes a game.</summary>
        /// <param name="gameId">The game to delete.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once it is gone.</returns>
        public Task DeleteAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            SendAsync(Delete($"me/github-games/{Escape(gameId)}"), "browserGames.delete", cancellationToken);

        /// <summary>Downloads a game's icon.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The image bytes and media type.</returns>
        public Task<StarhermitBinary> GetIconAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            SendBytesAsync(
                Get($"github-games/{Escape(gameId)}/icon").WithCredential(StarhermitCredential.AccountOptional),
                "browserGames.getIcon",
                cancellationToken);

        /// <summary>Downloads a game's cover art.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The image bytes and media type.</returns>
        public Task<StarhermitBinary> GetCoverArtAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            SendBytesAsync(
                Get($"github-games/{Escape(gameId)}/cover").WithCredential(StarhermitCredential.AccountOptional),
                "browserGames.getCoverArt",
                cancellationToken);

        /// <summary>Replaces a game's cover art.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="imageBytes">The image, in one of the formats the API accepts.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the art is stored.</returns>
        public Task SetCoverArtAsync(Guid gameId, byte[] imageBytes, CancellationToken cancellationToken = default)
        {
            if (imageBytes == null) throw new ArgumentNullException(nameof(imageBytes));
            var request = WithBody(
                Put($"me/github-games/{Escape(gameId)}/cover"),
                writer => writer.Write("imageBase64", Convert.ToBase64String(imageBytes)));

            return SendAsync(request, "browserGames.setCoverArt", cancellationToken);
        }

        /// <summary>Clears a game's cover art.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the art is gone.</returns>
        public Task ClearCoverArtAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            SendAsync(Delete($"me/github-games/{Escape(gameId)}/cover"), "browserGames.clearCoverArt", cancellationToken);

        /// <summary>
        /// Uploads a bundle for an existing game, streaming it rather than buffering it.
        /// </summary>
        /// <param name="gameId">The game to publish to.</param>
        /// <param name="openBundle">Opens the archive. Called once per attempt.</param>
        /// <param name="length">Archive length when known, which lets progress report a percentage.</param>
        /// <param name="progress">Optional upload progress.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>What the platform did with the bundle.</returns>
        public Task<StarhermitBundleResult> UploadBundleAsync(
            Guid gameId,
            Func<Stream> openBundle,
            long? length = null,
            IProgress<StarhermitTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (openBundle == null) throw new ArgumentNullException(nameof(openBundle));
            var request = Post($"me/github-games/{Escape(gameId)}/bundle")
                .WithContent(StarhermitContent.Stream(openBundle, length, "application/octet-stream"));
            request.Progress = progress;
            // Publishing is not idempotent, and a partially consumed archive must never be replayed.
            request.IsIdempotent = false;

            return SendAsync(request, "browserGames.uploadBundle", StarhermitBundleResult.Read, cancellationToken);
        }

        /// <summary>Creates a new game by uploading a folder archive.</summary>
        /// <param name="openArchive">Opens the archive. Called once per attempt.</param>
        /// <param name="displayName">Display name for the new game.</param>
        /// <param name="launchPath">Entry point within the archive.</param>
        /// <param name="length">Archive length when known.</param>
        /// <param name="progress">Optional upload progress.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>The new game.</returns>
        public async Task<StarhermitBrowserGame> CreateFromFolderAsync(
            Func<Stream> openArchive,
            string? displayName = null,
            string? launchPath = null,
            long? length = null,
            IProgress<StarhermitTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (openArchive == null) throw new ArgumentNullException(nameof(openArchive));
            var request = Post("me/github-games/upload")
                .WithQuery("displayName", displayName)
                .WithQuery("launchPath", launchPath)
                .WithContent(StarhermitContent.Stream(openArchive, length, "application/octet-stream"));
            request.Progress = progress;
            request.IsIdempotent = false;

            var json = await SendJsonAsync(request, "browserGames.createFromFolder", cancellationToken).ConfigureAwait(false);
            return StarhermitBrowserGame.Read(json["game"].IsObject ? json["game"] : json);
        }

        /// <summary>Reads audience statistics for a game.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The audience numbers.</returns>
        public Task<StarhermitGameAudience> GetStatsAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"me/github-games/{Escape(gameId)}/stats"),
                "browserGames.getStats",
                StarhermitGameAudience.Read,
                cancellationToken);

        /// <summary>Turns platform hosting on or off for a game.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="enabled">True to host it on the platform.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The hosting state that resulted.</returns>
        public Task<StarhermitHostingStatus> SetHostingAsync(
            Guid gameId,
            bool enabled,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Put($"me/github-games/{Escape(gameId)}/hosting"), writer => writer.Write("enabled", enabled)),
                "browserGames.setHosting",
                StarhermitHostingStatus.Read,
                cancellationToken);

        /// <summary>Reads a game's deployment state.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The deployment state.</returns>
        public Task<StarhermitHostingStatus> GetDeploymentAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"me/github-games/{Escape(gameId)}/deployment"),
                "browserGames.getDeployment",
                StarhermitHostingStatus.Read,
                cancellationToken);

        /// <summary>Pins the commit a game deploys from, or unpins it to track the default branch.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="commitSha">The commit to pin, or null to unpin.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The deployment state that resulted.</returns>
        public Task<StarhermitHostingStatus> SetDeploymentAsync(
            Guid gameId,
            string? commitSha,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Put($"me/github-games/{Escape(gameId)}/deployment"), writer => writer.Write("commit", commitSha)),
                "browserGames.setDeployment",
                StarhermitHostingStatus.Read,
                cancellationToken);

        /// <summary>
        /// Points a game at a different URL, keeping its id - and so its origin, playtime, ratings and
        /// reviews. The destination is re-verified and redeployed as a submission would be.
        /// </summary>
        /// <remarks>
        /// Stricter than submitting, because whoever controls a GitHub destination could claim the
        /// game afterwards: the caller must prove the destination is theirs, and its
        /// <c>starhermit.txt</c> must not name another account as owner. Either refusal is a
        /// <see cref="StarhermitAuthorizationException"/>. Rate-limited per account.
        /// </remarks>
        /// <param name="gameId">The game.</param>
        /// <param name="repositoryUrl">The new URL.</param>
        /// <param name="displayName">Display name, read only for a hosted-URL destination, which has no manifest to name the game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The game at its new URL.</returns>
        public Task<StarhermitBrowserGame> ChangeUrlAsync(
            Guid gameId,
            string repositoryUrl,
            string? displayName = null,
            CancellationToken cancellationToken = default)
        {
            if (repositoryUrl == null) throw new ArgumentNullException(nameof(repositoryUrl));
            var request = WithBody(Put($"me/github-games/{Escape(gameId)}/url"), writer =>
            {
                writer.Write("repoUrl", repositoryUrl);
                writer.WriteIfPresent("displayName", displayName);
            });
            return SendAsync(request, "browserGames.changeUrl", StarhermitBrowserGame.Read, cancellationToken);
        }

        /// <summary>Lists the caller's removed games, so a client can offer to restore one.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The removed games.</returns>
        public async Task<IReadOnlyList<StarhermitRemovedBrowserGame>> ListRemovedAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/github-games/removed"), "browserGames.listRemoved", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitRemovedBrowserGame.Read);
        }

        /// <summary>
        /// Restores an uploaded game the caller removed: same id, origin and history, with its files
        /// if they have not been reclaimed.
        /// </summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The restored game.</returns>
        /// <exception cref="StarhermitConflictException">The game has a source; restore it by submitting its URL again.</exception>
        public Task<StarhermitBrowserGame> RestoreAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"me/github-games/{Escape(gameId)}/restore"),
                "browserGames.restore",
                StarhermitBrowserGame.Read,
                cancellationToken);

        /// <summary>Reads a game's release notes. Needs no session.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The notes, or null when the game has none.</returns>
        public async Task<StarhermitReleaseNotes?> GetReleaseNotesAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            try
            {
                return await SendAsync(
                        Get($"github-games/{Escape(gameId)}/release-notes").WithCredential(StarhermitCredential.AccountOptional),
                        "browserGames.getReleaseNotes",
                        StarhermitReleaseNotes.Read,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (StarhermitNotFoundException)
            {
                // A game without notes is a state, not a failure.
                return null;
            }
        }

        /// <summary>Owner: lists the live sessions of the game's server.</summary>
        /// <remarks>
        /// A container game's capacity is counted from its live sessions, so a session the backend no
        /// longer holds still takes a seat until it is ended with <see cref="EndSessionAsync"/>.
        /// </remarks>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The sessions.</returns>
        public async Task<IReadOnlyList<StarhermitGameSessionSummary>> GetSessionsAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get($"me/github-games/{Escape(gameId)}/sessions"), "browserGames.getSessions", cancellationToken)
                .ConfigureAwait(false);
            return json.AsList(StarhermitGameSessionSummary.Read);
        }

        /// <summary>
        /// Owner: ends one live session through the platform's abandonment path - no winner, no rating
        /// change - and returns its seat.
        /// </summary>
        /// <param name="gameId">The game.</param>
        /// <param name="sessionId">The session.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the session has ended.</returns>
        public Task EndSessionAsync(Guid gameId, Guid sessionId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"me/github-games/{Escape(gameId)}/sessions/{Escape(sessionId)}"),
                "browserGames.endSession",
                cancellationToken);

        /// <summary>Owner: lists the game's achievements, declared and owner-created, with unlock counts.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The achievements.</returns>
        public async Task<IReadOnlyList<StarhermitOwnedAchievement>> GetAchievementsAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get($"me/github-games/{Escape(gameId)}/achievements"), "browserGames.getAchievements", cancellationToken)
                .ConfigureAwait(false);
            return json.AsList(StarhermitOwnedAchievement.Read);
        }

        /// <summary>
        /// Owner: creates an achievement. The game's server logic unlocks it by returning its key; no
        /// client can.
        /// </summary>
        /// <param name="gameId">The game.</param>
        /// <param name="achievement">The achievement; <see cref="StarhermitOwnedAchievementDraft.Key"/> and a name are required.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The achievement.</returns>
        public Task<StarhermitOwnedAchievement> CreateAchievementAsync(
            Guid gameId,
            StarhermitOwnedAchievementDraft achievement,
            CancellationToken cancellationToken = default)
        {
            if (achievement == null) throw new ArgumentNullException(nameof(achievement));
            return SendAsync(
                WithBody(Post($"me/github-games/{Escape(gameId)}/achievements"), achievement.Write),
                "browserGames.createAchievement",
                StarhermitOwnedAchievement.Read,
                cancellationToken);
        }

        /// <summary>Owner: changes an owner-created achievement. A script-declared one is changed by redeploying the script.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="achievementId">The achievement.</param>
        /// <param name="achievement">What to change; the key cannot change.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The achievement.</returns>
        public Task<StarhermitOwnedAchievement> UpdateAchievementAsync(
            Guid gameId,
            Guid achievementId,
            StarhermitOwnedAchievementDraft achievement,
            CancellationToken cancellationToken = default)
        {
            if (achievement == null) throw new ArgumentNullException(nameof(achievement));
            return SendAsync(
                WithBody(Put($"me/github-games/{Escape(gameId)}/achievements/{Escape(achievementId)}"), achievement.Write),
                "browserGames.updateAchievement",
                StarhermitOwnedAchievement.Read,
                cancellationToken);
        }

        /// <summary>Owner: deletes an owner-created achievement.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="achievementId">The achievement.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once it is gone.</returns>
        public Task DeleteAchievementAsync(Guid gameId, Guid achievementId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"me/github-games/{Escape(gameId)}/achievements/{Escape(achievementId)}"),
                "browserGames.deleteAchievement",
                cancellationToken);

        /// <summary>Owner: lists the game's leaderboards, inactive ones included.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The leaderboards.</returns>
        public async Task<IReadOnlyList<StarhermitLeaderboard>> GetLeaderboardsAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get($"me/github-games/{Escape(gameId)}/leaderboards"), "browserGames.getLeaderboards", cancellationToken)
                .ConfigureAwait(false);
            return json.AsList(StarhermitLeaderboard.Read);
        }

        /// <summary>Owner: creates a leaderboard the game's server logic fills.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="leaderboard">The board; key, name, score type and sort direction are required.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The leaderboard.</returns>
        public Task<StarhermitLeaderboard> CreateLeaderboardAsync(
            Guid gameId,
            StarhermitOwnedLeaderboardDraft leaderboard,
            CancellationToken cancellationToken = default)
        {
            if (leaderboard == null) throw new ArgumentNullException(nameof(leaderboard));
            return SendAsync(
                WithBody(Post($"me/github-games/{Escape(gameId)}/leaderboards"), leaderboard.Write),
                "browserGames.createLeaderboard",
                StarhermitLeaderboard.Read,
                cancellationToken);
        }

        /// <summary>Owner: changes a leaderboard's name, score range, activity or reset schedule.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="leaderboardId">The board.</param>
        /// <param name="leaderboard">What to change.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The leaderboard.</returns>
        public Task<StarhermitLeaderboard> UpdateLeaderboardAsync(
            Guid gameId,
            Guid leaderboardId,
            StarhermitOwnedLeaderboardDraft leaderboard,
            CancellationToken cancellationToken = default)
        {
            if (leaderboard == null) throw new ArgumentNullException(nameof(leaderboard));
            return SendAsync(
                WithBody(Put($"me/github-games/{Escape(gameId)}/leaderboards/{Escape(leaderboardId)}"), leaderboard.Write),
                "browserGames.updateLeaderboard",
                StarhermitLeaderboard.Read,
                cancellationToken);
        }

        /// <summary>Owner: deletes a leaderboard and its entries.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="leaderboardId">The board.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once it is gone.</returns>
        public Task DeleteLeaderboardAsync(Guid gameId, Guid leaderboardId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"me/github-games/{Escape(gameId)}/leaderboards/{Escape(leaderboardId)}"),
                "browserGames.deleteLeaderboard",
                cancellationToken);

        /// <summary>
        /// Owner: puts one player's rating back to the starting rating - for a smurf, a boosted account
        /// or a rating a bug corrupted. Wins and history are left alone.
        /// </summary>
        /// <param name="gameId">The game.</param>
        /// <param name="playerId">The player.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The player's rating now.</returns>
        public Task<StarhermitEloReset> ResetPlayerEloAsync(Guid gameId, Guid playerId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"me/github-games/{Escape(gameId)}/players/{Escape(playerId)}/elo"),
                "browserGames.resetPlayerElo",
                StarhermitEloReset.Read,
                cancellationToken);

        /// <summary>Owner: reads one page of the crash and bug reports players filed, newest first.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="kind">Only this kind - see <see cref="StarhermitReportKinds"/>.</param>
        /// <param name="status">Only this status - see <see cref="StarhermitReportStatuses"/>.</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A page of reports.</returns>
        public async Task<StarhermitPage<StarhermitReportSummary>> GetReportsAsync(
            Guid gameId,
            string? kind = null,
            string? status = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var request = Get($"me/github-games/{Escape(gameId)}/reports")
                .WithQuery("kind", kind)
                .WithQuery("status", status)
                .WithQuery("page", page)
                .WithQuery("pageSize", pageSize);
            var json = await SendJsonAsync(request, "browserGames.getReports", cancellationToken).ConfigureAwait(false);
            return StarhermitPage<StarhermitReportSummary>.Read(json, StarhermitReportSummary.Read);
        }

        /// <summary>Owner: enumerates every report, fetching pages as they are consumed.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="kind">Only this kind.</param>
        /// <param name="status">Only this status.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels enumeration.</param>
        /// <returns>An asynchronous sequence of reports.</returns>
        public IAsyncEnumerable<StarhermitReportSummary> EnumerateReportsAsync(
            Guid gameId,
            string? kind = null,
            string? status = null,
            int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            EnumeratePagesAsync(
                (page, token) => GetReportsAsync(gameId, kind, status, page, pageSize, token),
                cancellationToken);

        /// <summary>Owner: reads one report in full.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="reportId">The report.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The report.</returns>
        public Task<StarhermitReportDetail> GetReportAsync(Guid gameId, Guid reportId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"me/github-games/{Escape(gameId)}/reports/{Escape(reportId)}"),
                "browserGames.getReport",
                StarhermitReportDetail.Read,
                cancellationToken);

        /// <summary>
        /// Owner: downloads a file a player attached to a report. The bytes are the player's - treat
        /// them as untrusted input.
        /// </summary>
        /// <param name="gameId">The game.</param>
        /// <param name="reportId">The report.</param>
        /// <param name="attachmentId">The attachment.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The file's bytes and media type.</returns>
        public Task<StarhermitBinary> GetReportAttachmentAsync(
            Guid gameId,
            Guid reportId,
            Guid attachmentId,
            CancellationToken cancellationToken = default) =>
            SendBytesAsync(
                Get($"me/github-games/{Escape(gameId)}/reports/{Escape(reportId)}/attachments/{Escape(attachmentId)}"),
                "browserGames.getReportAttachment",
                cancellationToken);

        /// <summary>Owner: moves a report along - the player sees the new status in their own list.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="reportId">The report.</param>
        /// <param name="status">See <see cref="StarhermitReportStatuses"/>.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The report.</returns>
        public Task<StarhermitReportDetail> SetReportStatusAsync(
            Guid gameId,
            Guid reportId,
            string status,
            CancellationToken cancellationToken = default)
        {
            if (status == null) throw new ArgumentNullException(nameof(status));
            return SendAsync(
                WithBody(Patch($"me/github-games/{Escape(gameId)}/reports/{Escape(reportId)}"), writer => writer.Write("status", status)),
                "browserGames.setReportStatus",
                StarhermitReportDetail.Read,
                cancellationToken);
        }

        /// <summary>Owner: deletes a report and its attachments.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="reportId">The report.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once it is gone.</returns>
        public Task DeleteReportAsync(Guid gameId, Guid reportId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"me/github-games/{Escape(gameId)}/reports/{Escape(reportId)}"),
                "browserGames.deleteReport",
                cancellationToken);

        /// <summary>
        /// Owner: reads the recent output of the game's server container - live while it runs, the last
        /// crash's otherwise. Credentials the platform handed the container are redacted.
        /// </summary>
        /// <param name="gameId">The game.</param>
        /// <param name="tail">Most recent lines to return; the game's limit caps it.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The output.</returns>
        /// <exception cref="StarhermitConflictException">The game's backend is not a container.</exception>
        public Task<StarhermitContainerLogs> GetContainerLogsAsync(
            Guid gameId,
            int? tail = null,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"me/github-games/{Escape(gameId)}/container/logs").WithQuery("tail", tail),
                "browserGames.getContainerLogs",
                StarhermitContainerLogs.Read,
                cancellationToken);

        /// <summary>Owner: reads one page of the game's server crash reports, newest first.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A page of crashes, without their output.</returns>
        public async Task<StarhermitPage<StarhermitContainerCrash>> GetContainerCrashesAsync(
            Guid gameId,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var request = Get($"me/github-games/{Escape(gameId)}/container/crashes")
                .WithQuery("page", page)
                .WithQuery("pageSize", pageSize);
            var json = await SendJsonAsync(request, "browserGames.getContainerCrashes", cancellationToken).ConfigureAwait(false);
            return StarhermitPage<StarhermitContainerCrash>.Read(json, StarhermitContainerCrash.Read);
        }

        /// <summary>Owner: reads one crash report with the container's final output.</summary>
        /// <param name="gameId">The game.</param>
        /// <param name="crashId">The crash.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The crash.</returns>
        public Task<StarhermitContainerCrash> GetContainerCrashAsync(Guid gameId, Guid crashId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"me/github-games/{Escape(gameId)}/container/crashes/{Escape(crashId)}"),
                "browserGames.getContainerCrash",
                StarhermitContainerCrash.Read,
                cancellationToken);

        /// <summary>Reads whether the account has a linked GitHub identity, and under what login.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The link state.</returns>
        public async Task<StarhermitGitHubLink> GetGitHubLinkAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/github"), "browserGames.getGitHubLink", cancellationToken).ConfigureAwait(false);
            return new StarhermitGitHubLink(json["linked"].AsBooleanOrDefault(), json["login"].AsStringOrNull());
        }
    }

    /// <summary>Whether the account has a verified GitHub identity.</summary>
    public readonly struct StarhermitGitHubLink
    {
        /// <summary>Creates the link state.</summary>
        /// <param name="isLinked">True when an identity is linked.</param>
        /// <param name="login">The GitHub login, when linked.</param>
        public StarhermitGitHubLink(bool isLinked, string? login)
        {
            IsLinked = isLinked;
            Login = login;
        }

        /// <summary>True when a GitHub identity is linked.</summary>
        public bool IsLinked { get; }

        /// <summary>The linked login, which is what repository ownership is checked against.</summary>
        public string? Login { get; }
    }
}
