using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// One opaque save archive per game key: last write wins unless the writer names the version it
    /// expects to replace.
    /// </summary>
    /// <remarks>
    /// A cloud save is a progression archive and nothing else. Game settings live in the settings
    /// document and authoritative state lives with the game's own logic; putting either in here means
    /// a player who reinstalls gets one back and silently loses the other.
    /// <para>
    /// Every stored save has a version (<see cref="StarhermitCloudSaveInfo.ETag"/>). A game playable on
    /// two devices should upload with <see cref="StarhermitSaveCondition.IfMatch"/> naming the version it
    /// loaded, so a save written elsewhere in the meantime is reported as
    /// <see cref="StarhermitPreconditionFailedException"/> instead of being overwritten.
    /// </para>
    /// <para>
    /// The account's limits - the size of one save, how many games may keep one, and the total - are
    /// set by the platform's operators per account. A refusal names the number in force:
    /// <see cref="StarhermitQuotaExceededException"/> for <c>cloud_save_too_large</c> and
    /// <c>cloud_save_quota_exceeded</c>, <see cref="StarhermitConflictException"/> for
    /// <c>cloud_save_slots_exhausted</c>, each with <see cref="StarhermitApiException.Limit"/> set.
    /// </para>
    /// </remarks>
    public sealed class StarhermitCloudSavesClient : StarhermitServiceClient
    {
        internal StarhermitCloudSavesClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Reads metadata about the stored save without downloading it.</summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>Size and modification time, or a record reporting that nothing is stored.</returns>
        public Task<StarhermitCloudSaveInfo> GetInfoAsync(string gameKey, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"me/cloud-saves/{Escape(gameKey)}/info"),
                "cloudSaves.getInfo",
                StarhermitCloudSaveInfo.Read,
                cancellationToken);

        /// <summary>Downloads the stored save archive.</summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The archive bytes.</returns>
        /// <exception cref="StarhermitNotFoundException">No save is stored for this key.</exception>
        public async Task<byte[]> DownloadAsync(string gameKey, CancellationToken cancellationToken = default)
        {
            var save = await DownloadVersionAsync(gameKey, cancellationToken).ConfigureAwait(false);
            return save.Archive;
        }

        /// <summary>
        /// Downloads the stored save archive together with the version it is, read from the same
        /// response - so the version cannot describe a save written after these bytes were.
        /// </summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The archive and its version.</returns>
        /// <exception cref="StarhermitNotFoundException">No save is stored for this key.</exception>
        public async Task<StarhermitCloudSaveArchive> DownloadVersionAsync(string gameKey, CancellationToken cancellationToken = default)
        {
            var request = Get($"me/cloud-saves/{Escape(gameKey)}").Expecting(StarhermitResponseKind.Bytes);
            using var response = await Rest.SendAsync(request, "cloudSaves.download", cancellationToken).ConfigureAwait(false);
            return new StarhermitCloudSaveArchive(response.Body ?? Array.Empty<byte>(), response.Header("ETag"));
        }

        /// <summary>
        /// Downloads the save if there is one, and reports absence as null rather than as an error.
        /// </summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The archive bytes, or null when nothing is stored.</returns>
        public async Task<byte[]?> TryDownloadAsync(string gameKey, CancellationToken cancellationToken = default)
        {
            try
            {
                return await DownloadAsync(gameKey, cancellationToken).ConfigureAwait(false);
            }
            catch (StarhermitNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Uploads a save archive, replacing whatever was there.
        /// </summary>
        /// <remarks>
        /// Unconditional: a save another device wrote since this one last read is overwritten. Use the
        /// overload taking a <see cref="StarhermitSaveCondition"/> when that matters.
        /// </remarks>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="archive">The archive bytes.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>What the server stored, including its new version.</returns>
        /// <exception cref="StarhermitQuotaExceededException">The save is larger than the account may keep, or the account's saves are full.</exception>
        /// <exception cref="StarhermitConflictException">A new save would exceed the number of games the account may keep saves for.</exception>
        public Task<StarhermitCloudSaveInfo> UploadAsync(
            string gameKey,
            byte[] archive,
            CancellationToken cancellationToken = default) =>
            UploadAsync(gameKey, archive, StarhermitSaveCondition.None, cancellationToken);

        /// <summary>
        /// Uploads a save archive only if the stored save is still the one the condition names.
        /// </summary>
        /// <remarks>
        /// The deployment enforces the account's size, slot and total limits and refuses with the
        /// number in force; the SDK surfaces that rather than second-guessing the limit locally. A
        /// failed condition is checked before any limit, so a stale write learns that first.
        /// </remarks>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="archive">The archive bytes.</param>
        /// <param name="condition">Which stored version the write may replace.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>What the server stored, including its new version.</returns>
        /// <exception cref="StarhermitPreconditionFailedException">The stored save is not the one the condition named; nothing was written.</exception>
        /// <exception cref="StarhermitQuotaExceededException">The save is larger than the account may keep, or the account's saves are full.</exception>
        /// <exception cref="StarhermitConflictException">A new save would exceed the number of games the account may keep saves for.</exception>
        public Task<StarhermitCloudSaveInfo> UploadAsync(
            string gameKey,
            byte[] archive,
            StarhermitSaveCondition condition,
            CancellationToken cancellationToken = default)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            var request = WithBody(
                Put($"me/cloud-saves/{Escape(gameKey)}"),
                writer => writer.Write("dataBase64", Convert.ToBase64String(archive)))
                .WithHeader("If-Match", condition.IfMatchHeader)
                .WithHeader("If-None-Match", condition.IfNoneMatchHeader);

            return SendAsync(request, "cloudSaves.upload", StarhermitCloudSaveInfo.Read, cancellationToken);
        }

        /// <summary>Uploads a save read from the configured file store.</summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="path">Path within the file store's root.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>What the server stored.</returns>
        /// <exception cref="StarhermitFeatureUnavailableException">No file store is configured.</exception>
        public async Task<StarhermitCloudSaveInfo> UploadFileAsync(
            string gameKey,
            string path,
            CancellationToken cancellationToken = default)
        {
            var store = RequireFileStore();
            using var source = await store.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            using var buffer = new System.IO.MemoryStream();
            await source.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
            return await UploadAsync(gameKey, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads a save into the file store, writing to a temporary file and promoting it only on
        /// success so an interrupted sync cannot leave a truncated save in place.
        /// </summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="path">Path within the file store's root.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>True when a save existed and was written.</returns>
        public async Task<bool> DownloadToFileAsync(
            string gameKey,
            string path,
            CancellationToken cancellationToken = default)
        {
            var store = RequireFileStore();
            var archive = await TryDownloadAsync(gameKey, cancellationToken).ConfigureAwait(false);
            if (archive == null) return false;

            using var write = await store.BeginWriteAsync(path, cancellationToken).ConfigureAwait(false);
            await write.Stream.WriteAsync(archive, 0, archive.Length, cancellationToken).ConfigureAwait(false);
            await write.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>Creates a synchroniser for this client.</summary>
        /// <returns>A synchroniser that compares local and server state before writing either.</returns>
        public StarhermitCloudSaveSynchronizer CreateSynchronizer() => new StarhermitCloudSaveSynchronizer(this);

        private IStarhermitFileStore RequireFileStore() =>
            Options.FileStore ?? throw new StarhermitFeatureUnavailableException(
                "cloudSaves.file",
                StarhermitFeatureReasons.AdapterNotConfigured,
                "Reading or writing a save file needs an IStarhermitFileStore. Supply one in StarhermitOptions.FileStore, or use the byte-array overloads.");
    }

    /// <summary>A downloaded save archive and the version it is.</summary>
    public sealed class StarhermitCloudSaveArchive
    {
        internal StarhermitCloudSaveArchive(byte[] archive, string? eTag)
        {
            Archive = archive;
            ETag = eTag;
        }

        /// <summary>The archive bytes.</summary>
        public byte[] Archive { get; }

        /// <summary>The version these bytes are, or null when the deployment did not say.</summary>
        public string? ETag { get; }
    }

    /// <summary>
    /// Which stored save a cloud-save upload may replace - sent as <c>If-Match</c> or
    /// <c>If-None-Match</c>, and refused with <see cref="StarhermitPreconditionFailedException"/>
    /// when it does not hold.
    /// </summary>
    public sealed class StarhermitSaveCondition
    {
        private StarhermitSaveCondition(string? ifMatch, string? ifNoneMatch)
        {
            IfMatchHeader = ifMatch;
            IfNoneMatchHeader = ifNoneMatch;
        }

        /// <summary>No condition: last write wins.</summary>
        public static StarhermitSaveCondition None { get; } = new StarhermitSaveCondition(null, null);

        /// <summary>Write only when no save is stored yet (<c>If-None-Match: *</c>).</summary>
        public static StarhermitSaveCondition IfNoSaveExists { get; } = new StarhermitSaveCondition(null, "*");

        /// <summary>Write only over an existing save, whatever its version (<c>If-Match: *</c>).</summary>
        public static StarhermitSaveCondition IfAnySaveExists { get; } = new StarhermitSaveCondition("*", null);

        /// <summary>Write only over the given version (<c>If-Match</c>).</summary>
        /// <param name="eTag">A version from <see cref="StarhermitCloudSaveInfo.ETag"/> or
        /// <see cref="StarhermitCloudSaveArchive.ETag"/>. Bare text is quoted for you.</param>
        /// <returns>The condition.</returns>
        public static StarhermitSaveCondition IfMatch(string eTag)
        {
            if (string.IsNullOrWhiteSpace(eTag)) throw new ArgumentException("A version is required.", nameof(eTag));
            var tag = eTag.Trim();
            if (tag != "*" && !tag.StartsWith("\"", StringComparison.Ordinal) && !tag.StartsWith("W/", StringComparison.Ordinal))
                tag = "\"" + tag + "\"";
            return new StarhermitSaveCondition(tag, null);
        }

        /// <summary>
        /// Write only over the save <paramref name="info"/> describes: its version when one is stored,
        /// otherwise only if nothing has been stored since.
        /// </summary>
        /// <param name="info">Metadata the caller read and made its decision from.</param>
        /// <returns>The condition.</returns>
        public static StarhermitSaveCondition ForVersion(StarhermitCloudSaveInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (!info.Exists) return IfNoSaveExists;
            return string.IsNullOrEmpty(info.ETag) ? None : IfMatch(info.ETag!);
        }

        /// <summary>The <c>If-Match</c> value sent, or null.</summary>
        public string? IfMatchHeader { get; }

        /// <summary>The <c>If-None-Match</c> value sent, or null.</summary>
        public string? IfNoneMatchHeader { get; }
    }

    /// <summary>What a game knows about its own local save.</summary>
    public sealed class StarhermitLocalSaveState
    {
        /// <summary>True when a local save exists.</summary>
        public bool Exists { get; set; }

        /// <summary>When the local save was last written by the game.</summary>
        public DateTimeOffset? ModifiedAt { get; set; }

        /// <summary>
        /// The server timestamp this device last synchronised with. The synchroniser compares against
        /// this marker, which is the only way to tell "the server moved on" from "I have never synced".
        /// </summary>
        public DateTimeOffset? LastSyncedServerTimestamp { get; set; }
    }

    /// <summary>How to resolve a save conflict.</summary>
    public enum StarhermitConflictPolicy
    {
        /// <summary>Do nothing and report the conflict so the game can ask the player.</summary>
        Report = 0,

        /// <summary>Upload the local save over the server's.</summary>
        LocalWins = 1,

        /// <summary>Download the server's save over the local one.</summary>
        RemoteWins = 2,

        /// <summary>Abandon the synchronisation entirely.</summary>
        Abort = 3
    }

    /// <summary>What a synchronisation did.</summary>
    public enum StarhermitSyncOutcome
    {
        /// <summary>Both sides already agreed.</summary>
        UpToDate = 0,

        /// <summary>The local save was uploaded.</summary>
        Uploaded = 1,

        /// <summary>The server's save was downloaded.</summary>
        Downloaded = 2,

        /// <summary>
        /// Both sides changed since the last sync and no policy resolved it - or another device wrote
        /// the server's save while this synchronisation was running, whatever the policy.
        /// </summary>
        Conflict = 3,

        /// <summary>Neither side has a save.</summary>
        NothingToSync = 4,

        /// <summary>The caller asked to abandon the sync.</summary>
        Aborted = 5
    }

    /// <summary>The result of one synchronisation.</summary>
    public sealed class StarhermitSyncResult
    {
        internal StarhermitSyncResult(
            StarhermitSyncOutcome outcome,
            byte[]? downloadedArchive,
            StarhermitCloudSaveInfo? serverInfo)
        {
            Outcome = outcome;
            DownloadedArchive = downloadedArchive;
            ServerInfo = serverInfo;
        }

        /// <summary>What happened.</summary>
        public StarhermitSyncOutcome Outcome { get; }

        /// <summary>The archive that was downloaded, when one was.</summary>
        public byte[]? DownloadedArchive { get; }

        /// <summary>The server's metadata after the operation.</summary>
        public StarhermitCloudSaveInfo? ServerInfo { get; }

        /// <summary>The server timestamp to record as the new sync marker.</summary>
        public DateTimeOffset? ServerTimestamp => ServerInfo?.UpdatedAt;
    }

    /// <summary>
    /// Compares a local save against the server's before writing either.
    /// </summary>
    /// <remarks>
    /// Opt-in by design. A synchroniser that silently picked a winner would be a data-loss feature.
    /// When both sides have changed since the last sync this reports a conflict and leaves both intact
    /// unless the caller states a policy.
    /// <para>
    /// Every upload names the server version the decision was made against, so a save another device
    /// writes between this read and this write is never overwritten: the API refuses it, and the sync
    /// reports <see cref="StarhermitSyncOutcome.Conflict"/> with the server's new metadata - even under
    /// <see cref="StarhermitConflictPolicy.LocalWins"/>, which was a decision about a version that is
    /// no longer current. A download is reported only with the metadata of the bytes it returns.
    /// </para>
    /// </remarks>
    public sealed class StarhermitCloudSaveSynchronizer
    {
        private const int MaxDownloadAttempts = 3;

        private readonly StarhermitCloudSavesClient _client;

        internal StarhermitCloudSaveSynchronizer(StarhermitCloudSavesClient client)
        {
            _client = client;
        }

        /// <summary>Synchronises one game key.</summary>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="local">What the game knows about its local save.</param>
        /// <param name="readLocal">Reads the local archive, called only if it will be uploaded.</param>
        /// <param name="policy">How to resolve a conflict.</param>
        /// <param name="cancellationToken">Cancels the synchronisation.</param>
        /// <returns>What happened, and the archive to apply when one was downloaded.</returns>
        public async Task<StarhermitSyncResult> SynchronizeAsync(
            string gameKey,
            StarhermitLocalSaveState local,
            Func<CancellationToken, Task<byte[]>> readLocal,
            StarhermitConflictPolicy policy = StarhermitConflictPolicy.Report,
            CancellationToken cancellationToken = default)
        {
            if (gameKey == null) throw new ArgumentNullException(nameof(gameKey));
            if (local == null) throw new ArgumentNullException(nameof(local));
            if (readLocal == null) throw new ArgumentNullException(nameof(readLocal));

            var info = await _client.GetInfoAsync(gameKey, cancellationToken).ConfigureAwait(false);

            if (!info.Exists && !local.Exists)
                return new StarhermitSyncResult(StarhermitSyncOutcome.NothingToSync, null, info);

            if (!info.Exists)
                return await UploadAsync(gameKey, info, readLocal, cancellationToken).ConfigureAwait(false);

            if (!local.Exists)
                return await DownloadAsync(gameKey, cancellationToken).ConfigureAwait(false);

            var serverMoved = info.UpdatedAt.HasValue &&
                              (!local.LastSyncedServerTimestamp.HasValue ||
                               info.UpdatedAt.Value > local.LastSyncedServerTimestamp.Value);

            var localMoved = local.ModifiedAt.HasValue &&
                             (!local.LastSyncedServerTimestamp.HasValue ||
                              local.ModifiedAt.Value > local.LastSyncedServerTimestamp.Value);

            if (serverMoved && localMoved)
            {
                switch (policy)
                {
                    case StarhermitConflictPolicy.LocalWins:
                        return await UploadAsync(gameKey, info, readLocal, cancellationToken).ConfigureAwait(false);
                    case StarhermitConflictPolicy.RemoteWins:
                        return await DownloadAsync(gameKey, cancellationToken).ConfigureAwait(false);
                    case StarhermitConflictPolicy.Abort:
                        return new StarhermitSyncResult(StarhermitSyncOutcome.Aborted, null, info);
                    default:
                        return new StarhermitSyncResult(StarhermitSyncOutcome.Conflict, null, info);
                }
            }

            if (serverMoved) return await DownloadAsync(gameKey, cancellationToken).ConfigureAwait(false);
            if (localMoved) return await UploadAsync(gameKey, info, readLocal, cancellationToken).ConfigureAwait(false);
            return new StarhermitSyncResult(StarhermitSyncOutcome.UpToDate, null, info);
        }

        private async Task<StarhermitSyncResult> UploadAsync(
            string gameKey,
            StarhermitCloudSaveInfo decidedAgainst,
            Func<CancellationToken, Task<byte[]>> readLocal,
            CancellationToken cancellationToken)
        {
            var archive = await readLocal(cancellationToken).ConfigureAwait(false);
            try
            {
                var stored = await _client
                    .UploadAsync(gameKey, archive, StarhermitSaveCondition.ForVersion(decidedAgainst), cancellationToken)
                    .ConfigureAwait(false);
                return new StarhermitSyncResult(StarhermitSyncOutcome.Uploaded, null, stored);
            }
            catch (StarhermitPreconditionFailedException)
            {
                // Another device wrote between the read and this write. The decision was about a
                // version that no longer exists, so make none: report both sides as they are now.
                var current = await _client.GetInfoAsync(gameKey, cancellationToken).ConfigureAwait(false);
                return new StarhermitSyncResult(StarhermitSyncOutcome.Conflict, null, current);
            }
        }

        private async Task<StarhermitSyncResult> DownloadAsync(string gameKey, CancellationToken cancellationToken)
        {
            StarhermitCloudSaveInfo info;
            for (var attempt = 1; ; attempt++)
            {
                var save = await _client.DownloadVersionAsync(gameKey, cancellationToken).ConfigureAwait(false);
                info = await _client.GetInfoAsync(gameKey, cancellationToken).ConfigureAwait(false);

                // The marker this result hands back must describe the bytes it hands back. If a write
                // landed between the download and the metadata read, recording the newer timestamp
                // would tell the next sync this device already has a save it never saw.
                if (save.ETag == null || info.ETag == null || string.Equals(save.ETag, info.ETag, StringComparison.Ordinal))
                    return new StarhermitSyncResult(StarhermitSyncOutcome.Downloaded, save.Archive, info);
                if (attempt == MaxDownloadAttempts) break;
            }

            // Something is writing faster than this device can read: apply nothing.
            return new StarhermitSyncResult(StarhermitSyncOutcome.Conflict, null, info);
        }
    }
}
