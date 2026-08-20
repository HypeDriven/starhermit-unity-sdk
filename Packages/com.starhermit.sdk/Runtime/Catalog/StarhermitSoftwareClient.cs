using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>Search and filter options for the catalog.</summary>
    public sealed class StarhermitCatalogQuery
    {
        /// <summary>Free-text search.</summary>
        public string? Search { get; set; }

        /// <summary>Restrict to one category.</summary>
        public string? Category { get; set; }

        /// <summary>Restrict to one tag.</summary>
        public string? Tag { get; set; }

        /// <summary>Restrict to one publisher.</summary>
        public Guid? PublisherId { get; set; }

        /// <summary>Restrict to one platform.</summary>
        public string? Platform { get; set; }

        /// <summary>Restrict to one release status.</summary>
        public string? ReleaseStatus { get; set; }

        /// <summary>Sort key the deployment understands. Defaults to <c>name</c>.</summary>
        public string? Sort { get; set; }

        /// <summary>Applies the query to a request.</summary>
        /// <param name="request">The request to extend.</param>
        /// <returns>The same request, for chaining.</returns>
        public StarhermitRequest Apply(StarhermitRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return request
                .WithQuery("q", Search)
                .WithQuery("category", Category)
                .WithQuery("tag", Tag)
                .WithQuery("publisherId", PublisherId)
                .WithQuery("platform", Platform)
                .WithQuery("releaseStatus", ReleaseStatus)
                .WithQuery("sort", Sort);
        }
    }

    /// <summary>
    /// The catalog: browsing titles, claiming free ones, reading builds, recording launches and
    /// downloading entitled assets.
    /// </summary>
    public sealed class StarhermitSoftwareClient : StarhermitServiceClient
    {
        internal StarhermitSoftwareClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Lists catalog titles.</summary>
        /// <param name="query">Search and filter options.</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A page of titles.</returns>
        public async Task<StarhermitPage<StarhermitSoftwareTitle>> GetTitlesAsync(
            StarhermitCatalogQuery? query = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var request = Get("software").WithCredential(StarhermitCredential.AccountOptional);
            query?.Apply(request);
            request.WithQuery("page", page).WithQuery("pageSize", pageSize);

            var json = await SendJsonAsync(request, "software.getTitles", cancellationToken).ConfigureAwait(false);
            return StarhermitPage<StarhermitSoftwareTitle>.Read(json, StarhermitSoftwareTitle.Read);
        }

        /// <summary>Enumerates every matching title, fetching pages as they are consumed.</summary>
        /// <param name="query">Search and filter options.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels enumeration.</param>
        /// <returns>An asynchronous sequence of titles.</returns>
        public IAsyncEnumerable<StarhermitSoftwareTitle> EnumerateTitlesAsync(
            StarhermitCatalogQuery? query = null,
            int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            EnumeratePagesAsync(
                (page, token) => GetTitlesAsync(query, page, pageSize, token),
                cancellationToken);

        /// <summary>Reads one title.</summary>
        /// <param name="titleId">The title to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The title.</returns>
        public Task<StarhermitSoftwareTitle> GetTitleAsync(Guid titleId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"software/{Escape(titleId)}").WithCredential(StarhermitCredential.AccountOptional),
                "software.getTitle",
                StarhermitSoftwareTitle.Read,
                cancellationToken);

        /// <summary>
        /// Claims a free title, granting the account an entitlement.
        /// </summary>
        /// <remarks>
        /// A paid title answers <c>402</c>, which arrives as
        /// <see cref="StarhermitEntitlementException"/>: purchasing is not part of the v1 API.
        /// </remarks>
        /// <param name="titleId">The title to claim.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the entitlement exists.</returns>
        public Task ClaimFreeTitleAsync(Guid titleId, CancellationToken cancellationToken = default) =>
            SendAsync(Post($"software/{Escape(titleId)}/claim"), "software.claimFree", cancellationToken);

        /// <summary>Lists a title's builds.</summary>
        /// <param name="titleId">The title.</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Page size to request.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A page of builds.</returns>
        public async Task<StarhermitPage<StarhermitSoftwareBuild>> GetBuildsAsync(
            Guid titleId,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var request = Get($"software/{Escape(titleId)}/builds")
                .WithCredential(StarhermitCredential.AccountOptional)
                .WithQuery("page", page)
                .WithQuery("pageSize", pageSize);

            var json = await SendJsonAsync(request, "software.getBuilds", cancellationToken).ConfigureAwait(false);
            return StarhermitPage<StarhermitSoftwareBuild>.Read(json, StarhermitSoftwareBuild.Read);
        }

        /// <summary>
        /// Records that the player is launching a title. End it with
        /// <see cref="StarhermitActivityClient.EndLaunchAsync"/> so playtime is not left open.
        /// </summary>
        /// <param name="titleId">The title being launched.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The launch session.</returns>
        public Task<StarhermitLaunchSession> StartLaunchAsync(Guid titleId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Post($"software/{Escape(titleId)}/launch"),
                "software.startLaunch",
                StarhermitLaunchSession.Read,
                cancellationToken);

        /// <summary>
        /// Requests a signed download URL for an entitled title's latest clean assets.
        /// </summary>
        /// <param name="titleId">The title to download.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The signed URL. Treat it as a credential: never log it.</returns>
        public async Task<Uri> RequestDownloadUrlAsync(Guid titleId, CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Post($"software/{Escape(titleId)}/download"), "software.requestDownload", cancellationToken)
                .ConfigureAwait(false);
            var url = json["downloadUrl"].AsStringOrNull();
            if (string.IsNullOrEmpty(url))
                throw new StarhermitSerializationException("The download response carried no URL.");
            return new Uri(url!, UriKind.Absolute);
        }

        /// <summary>
        /// Opens the download stream for an entitled title, optionally resuming from a byte offset.
        /// </summary>
        /// <remarks>
        /// Use this for a large download the player may interrupt: keep your own partial file, pass its
        /// length as <paramref name="resumeFromBytes"/>, and check
        /// <see cref="StarhermitDownload.IsResumed"/> before appending. A signed origin that does not
        /// support ranges answers with the whole file, and the result says so rather than letting you
        /// append the beginning of the file to the middle of your partial one.
        /// </remarks>
        /// <param name="titleId">The title to download.</param>
        /// <param name="resumeFromBytes">Byte offset to resume from, or zero to start at the beginning.</param>
        /// <param name="progress">Optional progress reporting.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The open download, which the caller disposes.</returns>
        public async Task<StarhermitDownload> OpenDownloadAsync(
            Guid titleId,
            long resumeFromBytes = 0,
            IProgress<StarhermitTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var url = await RequestDownloadUrlAsync(titleId, cancellationToken).ConfigureAwait(false);
            var response = await Rest
                .FetchSignedAsync(url, progress, null, resumeFromBytes, cancellationToken)
                .ConfigureAwait(false);

            return new StarhermitDownload(response, resumeFromBytes);
        }

        /// <summary>
        /// Downloads an entitled title to the file store, writing to a temporary file and promoting it
        /// only once the transfer completes and any checksum matches.
        /// </summary>
        /// <param name="titleId">The title to download.</param>
        /// <param name="destinationPath">Path within the file store's root.</param>
        /// <param name="expectedSha256">Optional hex SHA-256 to verify against.</param>
        /// <param name="progress">Optional progress reporting.</param>
        /// <param name="cancellationToken">Cancels the download.</param>
        /// <returns>How many bytes were written.</returns>
        /// <exception cref="StarhermitFeatureUnavailableException">No file store is configured.</exception>
        public async Task<long> DownloadTitleAsync(
            Guid titleId,
            string destinationPath,
            string? expectedSha256 = null,
            IProgress<StarhermitTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var store = Options.FileStore ?? throw new StarhermitFeatureUnavailableException(
                "catalog.download",
                StarhermitFeatureReasons.AdapterNotConfigured,
                "Downloading to a file needs an IStarhermitFileStore. Supply one in StarhermitOptions.FileStore, or use RequestDownloadUrlAsync and stream it yourself.");

            var url = await RequestDownloadUrlAsync(titleId, cancellationToken).ConfigureAwait(false);
            using var response = await Rest.FetchSignedAsync(url, progress, null, 0, cancellationToken).ConfigureAwait(false);
            var body = response.BodyStream ?? throw new StarhermitTransportException("The download returned no body.");

            using var write = await store.BeginWriteAsync(destinationPath, cancellationToken).ConfigureAwait(false);
            using var hasher = expectedSha256 == null ? null : SHA256.Create();

            var buffer = new byte[128 * 1024];
            long total = 0;
            while (true)
            {
                var read = await body.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read <= 0) break;
                hasher?.TransformBlock(buffer, 0, read, null, 0);
                await write.Stream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                total += read;
            }

            if (hasher != null)
            {
                hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var actual = ToHex(hasher.Hash!);
                if (!string.Equals(actual, expectedSha256!.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase))
                {
                    // The partial file is discarded by the write handle's disposal; nothing is promoted.
                    throw new StarhermitProtocolException(
                        $"The downloaded file's SHA-256 ({actual}) does not match the expected checksum.");
                }
            }

            await write.CommitAsync(cancellationToken).ConfigureAwait(false);
            return total;
        }

        /// <summary>Lists the achievement definitions published for a catalog title.</summary>
        /// <param name="titleId">The title.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The definitions.</returns>
        public async Task<IReadOnlyList<StarhermitAchievement>> GetTitleAchievementsAsync(
            Guid titleId,
            CancellationToken cancellationToken = default)
        {
            var request = Get($"software/{Escape(titleId)}/achievements").WithCredential(StarhermitCredential.AccountOptional);
            var json = await SendJsonAsync(request, "software.getTitleAchievements", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitAchievement.Read);
        }

        private static string ToHex(byte[] bytes)
        {
            var characters = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";
            for (var i = 0; i < bytes.Length; i++)
            {
                characters[i * 2] = digits[bytes[i] >> 4];
                characters[i * 2 + 1] = digits[bytes[i] & 0xF];
            }

            return new string(characters);
        }
    }
}
