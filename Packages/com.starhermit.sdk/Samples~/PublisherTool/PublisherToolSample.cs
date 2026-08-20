#if UNITY_2021_3_OR_NEWER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Starhermit.Samples
{
    /// <summary>
    /// Publisher operations, and a streamed bundle upload over the WebSocket protocol.
    /// </summary>
    /// <remarks>
    /// The upload never buffers the archive. Chunks go out as they are read, the server acknowledges
    /// bytes as it receives them, and nothing is published until the explicit completion frame - so an
    /// interrupted upload leaves the live game exactly as it was.
    /// </remarks>
    public sealed class PublisherToolSample : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private StarhermitGameUploadConnection? _upload;

        /// <summary>The signed-in client to use.</summary>
        public StarhermitClient? Client { get; set; }

        /// <summary>Path to the archive to publish.</summary>
        public string ArchivePath { get; set; } = string.Empty;

        private void Start()
        {
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            var client = Client;
            if (client == null) return;

            var publishers = await client.Publishers.GetMineAsync(_lifetime.Token);
            if (publishers.Count == 0)
            {
                Debug.Log("[Sample] This account belongs to no publisher.");
                return;
            }

            var publisher = publishers[0];
            var downloads = await client.Publishers.GetDownloadAnalyticsAsync(publisher.Id, _lifetime.Token);
            foreach (var entry in downloads) Debug.Log($"[Sample] title {entry.Key}: {entry.Value} downloads.");

            var games = await client.BrowserGames.ListMineAsync(_lifetime.Token);
            if (games.Count == 0 || string.IsNullOrEmpty(ArchivePath) || !File.Exists(ArchivePath))
            {
                Debug.Log("[Sample] Nothing to publish.");
                return;
            }

            _upload = client.CreateBundleUploadConnection(games[0].Id);
            _upload.BytesAcknowledged += received => Debug.Log($"[Sample] server has {received} bytes.");
            _upload.PublishProgress += (phase, received) => Debug.Log($"[Sample] {phase}: {received} bytes.");

            await _upload.ConnectAsync(_lifetime.Token);
            var ready = await _upload.WaitForReadyAsync(_lifetime.Token);
            Debug.Log($"[Sample] Uploading in {ready.Mode} mode; allowance {ready.LimitBytes} bytes.");

            using var archive = File.OpenRead(ArchivePath);
            var progress = new Progress<StarhermitTransferProgress>(
                report => Debug.Log($"[Sample] {report.BytesTransferred}/{report.TotalBytes} bytes sent."));

            try
            {
                var outcome = await _upload.UploadAsync(archive, progress, cancellationToken: _lifetime.Token);
                Debug.Log($"[Sample] Published: client={outcome.ClientPublished}, image={outcome.ImageDigest}.");
            }
            catch (OperationCanceledException)
            {
                // Cancelling before the completion frame publishes nothing; the live game is untouched.
                await _upload.AbortAsync(CancellationToken.None);
            }
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _upload?.Dispose();
        }
    }
}
#endif
