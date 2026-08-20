#if UNITY_2021_3_OR_NEWER
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Starhermit.Samples
{
    /// <summary>
    /// The player-facing catalog: search, claim, download, launch, rate, wishlist and cloud saves.
    /// </summary>
    /// <remarks>
    /// The cloud-save section is the one to read twice. Saves are last-write-wins at the API, so the
    /// synchroniser refuses to pick a winner when both sides changed: it reports a conflict and leaves
    /// both copies alone until the game says which one wins.
    /// </remarks>
    public sealed class CatalogSample : MonoBehaviour
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        /// <summary>The signed-in client to use.</summary>
        public StarhermitClient? Client { get; set; }

        private void Start()
        {
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            var client = Client;
            if (client == null) return;

            var page = await client.Software.GetTitlesAsync(
                new StarhermitCatalogQuery { Search = "space", Sort = "name" },
                pageSize: 10,
                cancellationToken: _lifetime.Token);

            Debug.Log($"[Sample] {page.TotalCount} titles match; showing {page.Count}.");
            if (page.Count == 0) return;

            var title = page[0];

            if (title.IsFree && !await client.Entitlements.HasEntitlementAsync(title.Id, _lifetime.Token))
            {
                try
                {
                    await client.Software.ClaimFreeTitleAsync(title.Id, _lifetime.Token);
                }
                catch (StarhermitEntitlementException)
                {
                    // The title is not actually free to this account; purchasing is not part of v1.
                    Debug.Log("[Sample] This title cannot be claimed.");
                }
            }

            await client.Wishlist.AddAsync(title.Id, _lifetime.Token);
            await client.Ratings.RateAsync(title.Id.ToString("D"), 5, "Runs beautifully.", _lifetime.Token);

            var launch = await client.Software.StartLaunchAsync(title.Id, _lifetime.Token);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.Token);
            }
            finally
            {
                await client.Activity.EndLaunchAsync(launch.LaunchId, CancellationToken.None);
            }

            await SynchroniseSaveAsync(client, title.Id.ToString("D"));
        }

        private async Task SynchroniseSaveAsync(StarhermitClient client, string gameKey)
        {
            var local = new StarhermitLocalSaveState
            {
                Exists = true,
                ModifiedAt = DateTimeOffset.UtcNow,

                // A real game persists this marker beside its save file; without it the synchroniser
                // cannot tell "the server moved on" from "I have never synchronised".
                LastSyncedServerTimestamp = null
            };

            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                gameKey,
                local,
                _ => Task.FromResult(new byte[] { 0x50, 0x4B }),
                StarhermitConflictPolicy.Report,
                _lifetime.Token);

            switch (result.Outcome)
            {
                case StarhermitSyncOutcome.Conflict:
                    Debug.LogWarning("[Sample] Both copies changed. Ask the player which to keep, then re-run with a policy.");
                    break;
                case StarhermitSyncOutcome.Downloaded:
                    Debug.Log($"[Sample] Applied the server's save ({result.DownloadedArchive?.Length} bytes).");
                    break;
                default:
                    Debug.Log($"[Sample] Cloud save: {result.Outcome}.");
                    break;
            }
        }

        private void OnDestroy() => _lifetime.Cancel();
    }
}
#endif
