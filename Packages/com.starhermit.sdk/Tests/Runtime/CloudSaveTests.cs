using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Starhermit.Tests
{
    /// <summary>
    /// Cloud saves are last-write-wins at the API, so the synchroniser's job is to refuse to pick a
    /// winner on its own. Every one of these cases is a way a player could lose progress.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class CloudSaveTests
    {
        private static readonly DateTimeOffset Base = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        [Test]
        public async Task NeitherSideHasASave_IsNothingToDo()
        {
            var client = await ClientWithInfoAsync(exists: false);
            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                "chess",
                new StarhermitLocalSaveState { Exists = false },
                _ => Task.FromResult(Array.Empty<byte>()));

            Assert.AreEqual(StarhermitSyncOutcome.NothingToSync, result.Outcome);
        }

        [Test]
        public async Task OnlyTheLocalSaveExists_ItIsUploaded()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":false,\"sizeBytes\":0}")
                .EnqueueJson(200, "{\"gameKey\":\"chess\",\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T12:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                "chess",
                new StarhermitLocalSaveState { Exists = true, ModifiedAt = Base },
                _ => Task.FromResult(new byte[] { 1, 2, 3 }));

            Assert.AreEqual(StarhermitSyncOutcome.Uploaded, result.Outcome);
            Assert.AreEqual("PUT", transport.Last.Method);
        }

        [Test]
        public async Task OnlyTheServerHasASave_ItIsDownloaded()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T12:00:00Z\"}")
                .Enqueue(_ => new FakeResponse(200, "zip-bytes"))
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T12:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                "chess",
                new StarhermitLocalSaveState { Exists = false },
                _ => Task.FromResult(Array.Empty<byte>()));

            Assert.AreEqual(StarhermitSyncOutcome.Downloaded, result.Outcome);
            Assert.IsNotNull(result.DownloadedArchive);
        }

        [Test]
        public async Task OnlyTheServerMovedOn_ItIsDownloaded()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T13:00:00Z\"}")
                .Enqueue(_ => new FakeResponse(200, "newer"))
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T13:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var local = new StarhermitLocalSaveState
            {
                Exists = true,
                ModifiedAt = Base,
                LastSyncedServerTimestamp = Base
            };

            var result = await client.CloudSaves.CreateSynchronizer()
                .SynchronizeAsync("chess", local, _ => Task.FromResult(new byte[] { 1 }));

            Assert.AreEqual(StarhermitSyncOutcome.Downloaded, result.Outcome);
        }

        [Test]
        public async Task OnlyTheLocalSaveMovedOn_ItIsUploaded()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T12:00:00Z\"}")
                .EnqueueJson(200, "{\"gameKey\":\"chess\",\"sizeBytes\":1,\"updatedAt\":\"2026-08-20T14:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var local = new StarhermitLocalSaveState
            {
                Exists = true,
                ModifiedAt = Base.AddHours(2),
                LastSyncedServerTimestamp = Base
            };

            var result = await client.CloudSaves.CreateSynchronizer()
                .SynchronizeAsync("chess", local, _ => Task.FromResult(new byte[] { 1 }));

            Assert.AreEqual(StarhermitSyncOutcome.Uploaded, result.Outcome);
        }

        [Test]
        public async Task BothMovedOn_ReportsAConflictAndTouchesNeitherSide()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T13:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var local = new StarhermitLocalSaveState
            {
                Exists = true,
                ModifiedAt = Base.AddHours(2),
                LastSyncedServerTimestamp = Base
            };

            var result = await client.CloudSaves.CreateSynchronizer()
                .SynchronizeAsync("chess", local, _ => Task.FromResult(new byte[] { 1 }));

            Assert.AreEqual(StarhermitSyncOutcome.Conflict, result.Outcome);
            Assert.AreEqual(1, transport.Requests.Count, "a conflict writes nothing anywhere");
        }

        [Test]
        public async Task BothMovedOn_WithAStatedPolicy_ResolvesTheCallersWay()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T13:00:00Z\"}")
                .EnqueueJson(200, "{\"gameKey\":\"chess\",\"sizeBytes\":1,\"updatedAt\":\"2026-08-20T14:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var local = new StarhermitLocalSaveState
            {
                Exists = true,
                ModifiedAt = Base.AddHours(2),
                LastSyncedServerTimestamp = Base
            };

            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                "chess",
                local,
                _ => Task.FromResult(new byte[] { 1 }),
                StarhermitConflictPolicy.LocalWins);

            Assert.AreEqual(StarhermitSyncOutcome.Uploaded, result.Outcome);
        }

        [Test]
        public async Task BothMovedOn_WithAbort_DoesNothing()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T13:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var local = new StarhermitLocalSaveState
            {
                Exists = true,
                ModifiedAt = Base.AddHours(2),
                LastSyncedServerTimestamp = Base
            };

            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                "chess", local, _ => Task.FromResult(new byte[] { 1 }), StarhermitConflictPolicy.Abort);

            Assert.AreEqual(StarhermitSyncOutcome.Aborted, result.Outcome);
        }

        [Test]
        public async Task NeitherMovedSinceTheLastSync_IsUpToDate()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T12:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var local = new StarhermitLocalSaveState
            {
                Exists = true,
                ModifiedAt = Base.AddMinutes(-5),
                LastSyncedServerTimestamp = Base
            };

            var result = await client.CloudSaves.CreateSynchronizer()
                .SynchronizeAsync("chess", local, _ => Task.FromResult(new byte[] { 1 }));

            Assert.AreEqual(StarhermitSyncOutcome.UpToDate, result.Outcome);
        }

        [Test]
        public async Task MissingSave_ReadsAsAbsenceRatherThanAnError()
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(404, "{\"error\":\"no save\"}"));
            using var client = await TestHarness.SignedInAsync(transport);

            Assert.IsNull(await client.CloudSaves.TryDownloadAsync("chess"));
            Assert.ThrowsAsync<StarhermitNotFoundException>(() => client.CloudSaves.DownloadAsync("chess"));
        }

        [Test]
        public async Task Upload_SendsBase64AndReportsWhatTheServerStored()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"gameKey\":\"chess\",\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T12:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var info = await client.CloudSaves.UploadAsync("chess", new byte[] { 1, 2, 3 });

            StringAssert.Contains("\"dataBase64\":\"AQID\"", transport.Last.Body);
            Assert.AreEqual(3, info.SizeBytes);
        }

        private static async Task<StarhermitClient> ClientWithInfoAsync(bool exists)
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(
                200,
                exists
                    ? "{\"exists\":true,\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T12:00:00Z\"}"
                    : "{\"exists\":false,\"sizeBytes\":0}"));
            return await TestHarness.SignedInAsync(transport);
        }
    }
}
