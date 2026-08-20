using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// One opaque save archive per game key, last write wins.
    /// </summary>
    /// <remarks>
    /// A cloud save is a progression archive and nothing else. Game settings live in the settings
    /// document and authoritative state lives with the game's own logic; putting either in here means
    /// a player who reinstalls gets one back and silently loses the other.
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
            var binary = await SendBytesAsync(Get($"me/cloud-saves/{Escape(gameKey)}"), "cloudSaves.download", cancellationToken)
                .ConfigureAwait(false);
            return binary.Bytes;
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
        /// The deployment enforces its own size budget and answers with a validation error when the
        /// archive is too large; the SDK surfaces that rather than second-guessing the limit locally.
        /// </remarks>
        /// <param name="gameKey">The uniform game key.</param>
        /// <param name="archive">The archive bytes.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>What the server stored.</returns>
        public Task<StarhermitCloudSaveInfo> UploadAsync(
            string gameKey,
            byte[] archive,
            CancellationToken cancellationToken = default)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            var request = WithBody(
                Put($"me/cloud-saves/{Escape(gameKey)}"),
                writer => writer.Write("dataBase64", Convert.ToBase64String(archive)));

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

        /// <summary>Both sides changed since the last sync and no policy resolved it.</summary>
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
    /// Opt-in by design. Cloud saves are last-write-wins at the API, and a synchroniser that silently
    /// picked a winner would be a data-loss feature. When both sides have changed since the last sync
    /// this reports a conflict and leaves both intact unless the caller states a policy.
    /// </remarks>
    public sealed class StarhermitCloudSaveSynchronizer
    {
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
                return await UploadAsync(gameKey, readLocal, cancellationToken).ConfigureAwait(false);

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
                        return await UploadAsync(gameKey, readLocal, cancellationToken).ConfigureAwait(false);
                    case StarhermitConflictPolicy.RemoteWins:
                        return await DownloadAsync(gameKey, cancellationToken).ConfigureAwait(false);
                    case StarhermitConflictPolicy.Abort:
                        return new StarhermitSyncResult(StarhermitSyncOutcome.Aborted, null, info);
                    default:
                        return new StarhermitSyncResult(StarhermitSyncOutcome.Conflict, null, info);
                }
            }

            if (serverMoved) return await DownloadAsync(gameKey, cancellationToken).ConfigureAwait(false);
            if (localMoved) return await UploadAsync(gameKey, readLocal, cancellationToken).ConfigureAwait(false);
            return new StarhermitSyncResult(StarhermitSyncOutcome.UpToDate, null, info);
        }

        private async Task<StarhermitSyncResult> UploadAsync(
            string gameKey,
            Func<CancellationToken, Task<byte[]>> readLocal,
            CancellationToken cancellationToken)
        {
            var archive = await readLocal(cancellationToken).ConfigureAwait(false);
            var stored = await _client.UploadAsync(gameKey, archive, cancellationToken).ConfigureAwait(false);
            return new StarhermitSyncResult(StarhermitSyncOutcome.Uploaded, null, stored);
        }

        private async Task<StarhermitSyncResult> DownloadAsync(string gameKey, CancellationToken cancellationToken)
        {
            var archive = await _client.DownloadAsync(gameKey, cancellationToken).ConfigureAwait(false);
            var info = await _client.GetInfoAsync(gameKey, cancellationToken).ConfigureAwait(false);
            return new StarhermitSyncResult(StarhermitSyncOutcome.Downloaded, archive, info);
        }
    }
}
