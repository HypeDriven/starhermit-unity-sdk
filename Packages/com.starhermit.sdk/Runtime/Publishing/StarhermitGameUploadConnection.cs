using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>What the server said when the upload socket opened.</summary>
    public readonly struct StarhermitUploadReady
    {
        /// <summary>Creates the ready notice.</summary>
        /// <param name="mode">Either <c>create</c> or <c>bundle</c>.</param>
        /// <param name="limitBytes">Largest archive this game may upload.</param>
        /// <param name="heartbeatSeconds">How often the server reports progress while publishing.</param>
        public StarhermitUploadReady(string mode, long limitBytes, int heartbeatSeconds)
        {
            Mode = mode;
            LimitBytes = limitBytes;
            HeartbeatSeconds = heartbeatSeconds;
        }

        /// <summary>Whether this upload creates a game or replaces an existing one's bundle.</summary>
        public string Mode { get; }

        /// <summary>The byte allowance the deployment applies to this game.</summary>
        public long LimitBytes { get; }

        /// <summary>How often the server sends a publishing heartbeat.</summary>
        public int HeartbeatSeconds { get; }
    }

    /// <summary>The result of a completed upload.</summary>
    public sealed class StarhermitUploadOutcome : StarhermitModel
    {
        internal StarhermitUploadOutcome(JsonValue json) : base(json)
        {
            Status = json["status"].AsInt32OrDefault();
            ClientPublished = json["clientPublished"].AsBooleanOrDefault();
            ServerImageLoaded = json["serverImageLoaded"].AsBooleanOrDefault();
            ImageDigest = json["imageDigest"].AsStringOrNull();
            BytesReceived = json["bytesReceived"].AsInt64OrDefault();
            Game = json["game"].IsObject ? StarhermitBrowserGame.Read(json["game"]) : null;
        }

        /// <summary>The HTTP-equivalent status the server reported.</summary>
        public int Status { get; }

        /// <summary>True when the client bundle was published.</summary>
        public bool ClientPublished { get; }

        /// <summary>True when a server image was loaded from the archive.</summary>
        public bool ServerImageLoaded { get; }

        /// <summary>Digest of the loaded server image.</summary>
        public string? ImageDigest { get; }

        /// <summary>How many bytes the server accepted.</summary>
        public long BytesReceived { get; }

        /// <summary>The created game, for an upload that created one.</summary>
        public StarhermitBrowserGame? Game { get; }
    }

    /// <summary>
    /// The streamed game-upload socket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Binary frames are concatenated server-side in arrival order, so the client may pick any chunk
    /// size and never has to hold the archive in memory. A multi-gigabyte bundle moves through a fixed
    /// buffer.
    /// </para>
    /// <para>
    /// Nothing is published until the explicit <c>complete</c> control frame is sent. A connection that
    /// drops mid-transfer publishes nothing, which is what makes an interrupted upload safe to retry
    /// from the beginning rather than leaving a half-game live.
    /// </para>
    /// <para>
    /// Automatic reconnection is off for this protocol: reattaching mid-stream would silently splice
    /// two byte ranges into one archive.
    /// </para>
    /// </remarks>
    public sealed class StarhermitGameUploadConnection : StarhermitConnection
    {
        private readonly Guid? _gameId;
        private readonly string? _displayName;
        private readonly string? _launchPath;

        private TaskCompletionSource<StarhermitUploadReady> _ready =
            new TaskCompletionSource<StarhermitUploadReady>(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<StarhermitUploadOutcome> _outcome =
            new TaskCompletionSource<StarhermitUploadOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal StarhermitGameUploadConnection(
            StarhermitClient client,
            Guid? gameId,
            string? displayName,
            string? launchPath)
            : base(client, "game-upload")
        {
            _gameId = gameId;
            _displayName = displayName;
            _launchPath = launchPath;
            AutoReconnect = false;
        }

        /// <inheritdoc />
        protected override string Path => "game-upload";

        /// <inheritdoc />
        protected override void BuildQuery(IList<KeyValuePair<string, string>> query)
        {
            if (_gameId.HasValue) query.Add(new KeyValuePair<string, string>("gameId", _gameId.Value.ToString("D")));
            if (_displayName != null) query.Add(new KeyValuePair<string, string>("displayName", _displayName));
            if (_launchPath != null) query.Add(new KeyValuePair<string, string>("launchPath", _launchPath));
        }

        /// <summary>Raised as the server acknowledges received bytes.</summary>
        public event Action<long>? BytesAcknowledged;

        /// <summary>Raised while the server publishes, after the archive has landed.</summary>
        public event Action<string, long>? PublishProgress;

        /// <summary>Waits for the server's ready notice, which carries this game's byte allowance.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The ready notice.</returns>
        public async Task<StarhermitUploadReady> WaitForReadyAsync(CancellationToken cancellationToken = default)
        {
            using (cancellationToken.Register(() => _ready.TrySetCanceled(cancellationToken)))
            {
                return await _ready.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs a whole upload: connect, wait for ready, stream the archive, declare it complete, and
        /// wait for the result.
        /// </summary>
        /// <param name="archive">The archive to send. Read once, never buffered whole.</param>
        /// <param name="progress">Optional progress reporting.</param>
        /// <param name="chunkSize">Bytes per frame. Larger frames mean fewer round trips.</param>
        /// <param name="cancellationToken">Cancels the upload, which publishes nothing.</param>
        /// <returns>What the server did with the archive.</returns>
        public async Task<StarhermitUploadOutcome> UploadAsync(
            Stream archive,
            IProgress<StarhermitTransferProgress>? progress = null,
            int chunkSize = 256 * 1024,
            CancellationToken cancellationToken = default)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            if (chunkSize < 1024) throw new ArgumentOutOfRangeException(nameof(chunkSize), "Use a chunk size of at least 1 KB.");

            if (State != StarhermitConnectionState.Connected)
                await ConnectAsync(cancellationToken).ConfigureAwait(false);

            var ready = await WaitForReadyAsync(cancellationToken).ConfigureAwait(false);

            long? total = archive.CanSeek ? archive.Length : (long?)null;
            if (total.HasValue && ready.LimitBytes > 0 && total.Value > ready.LimitBytes)
            {
                // Refused before a byte is sent: the server would reject it after the whole transfer,
                // and spending a player's upload bandwidth to learn that is pure waste.
                await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                throw new StarhermitProtocolException(
                    $"The archive is {total.Value} bytes and this game's allowance is {ready.LimitBytes} bytes.");
            }

            var buffer = new byte[chunkSize];
            long sent = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await archive.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read <= 0) break;

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                await SendBinaryAsync(chunk, cancellationToken).ConfigureAwait(false);

                sent += read;
                progress?.Report(new StarhermitTransferProgress(sent, total, isUpload: true));
            }

            await SendTextAsync(JsonWriter.SerializeObject(writer => writer.Write("type", "complete")), cancellationToken)
                .ConfigureAwait(false);

            using (cancellationToken.Register(() => _outcome.TrySetCanceled(cancellationToken)))
            {
                return await _outcome.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Abandons the upload. The server discards what it has received rather than being left to
        /// notice a dropped connection.
        /// </summary>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the abort has been sent.</returns>
        public async Task AbortAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await SendTextAsync(JsonWriter.SerializeObject(writer => writer.Write("type", "abort")), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (StarhermitProtocolException)
            {
                // The socket is already gone, which achieves the same thing.
            }
        }

        /// <inheritdoc />
        protected override void HandleText(string text)
        {
            if (!JsonParser.TryParse(text, out var frame) || !frame.IsObject) return;
            var type = frame["type"].AsStringOrNull();

            switch (type)
            {
                case "ready":
                    _ready.TrySetResult(new StarhermitUploadReady(
                        frame["mode"].AsStringOrNull() ?? "bundle",
                        frame["limitBytes"].AsInt64OrDefault(),
                        frame["heartbeatSeconds"].AsInt32OrDefault()));
                    break;

                case "ack":
                {
                    var received = frame["bytesReceived"].AsInt64OrDefault();
                    Raise(() => BytesAcknowledged?.Invoke(received));
                    break;
                }

                case "progress":
                {
                    var phase = frame["phase"].AsStringOrNull() ?? "publishing";
                    var received = frame["bytesReceived"].AsInt64OrDefault();
                    Raise(() => PublishProgress?.Invoke(phase, received));
                    break;
                }

                case "result":
                    _outcome.TrySetResult(new StarhermitUploadOutcome(frame));
                    break;

                case "error":
                {
                    var error = new StarhermitErrorInfo
                    {
                        Status = frame["status"].AsInt32OrDefault(400),
                        Method = "WS",
                        Path = "ws/v1/game-upload",
                        ServerMessage = frame["error"].AsStringOrNull() ?? "The upload was refused."
                    };

                    var exception = StarhermitApiException.Create(error);
                    _ready.TrySetException(exception);
                    _outcome.TrySetException(exception);
                    break;
                }
            }
        }

        /// <inheritdoc />
        protected override void HandleBinary(byte[] payload)
        {
            // The upload protocol is one-directional for binary: the server never sends any.
        }
    }
}
