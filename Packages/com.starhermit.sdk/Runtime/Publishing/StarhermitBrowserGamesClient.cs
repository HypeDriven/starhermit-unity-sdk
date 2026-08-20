using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// Browser games submitted from a GitHub repository: submission, ownership, cover art, hosting,
    /// deployment and bundle uploads.
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
