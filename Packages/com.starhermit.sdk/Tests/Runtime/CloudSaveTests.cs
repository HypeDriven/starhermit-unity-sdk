using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Starhermit.Tests
{
    /// <summary>
    /// Cloud saves are last-write-wins at the API unless the writer names the version it read, so the
    /// synchroniser's job is to refuse to pick a winner on its own - and to name that version on every
    /// write. Every one of these cases is a way a player could lose progress.
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

        [Test]
        public async Task Upload_WithAVersion_SendsIfMatchAndReadsTheNewVersion()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"gameKey\":\"chess\",\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T12:00:01Z\",\"etag\":\"\\\"b2\\\"\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var info = await client.CloudSaves.UploadAsync("chess", new byte[] { 1, 2, 3 }, StarhermitSaveCondition.IfMatch("a1"));

            Assert.AreEqual("\"a1\"", transport.Last.Header("If-Match"), "a bare version is quoted for the caller");
            Assert.IsNull(transport.Last.Header("If-None-Match"));
            Assert.AreEqual("\"b2\"", info.ETag);
            Assert.IsTrue(info.Exists, "an upload's answer describes a save that now exists");
        }

        [Test]
        public async Task Upload_Unconditional_SendsNoPrecondition()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"gameKey\":\"chess\",\"sizeBytes\":1,\"updatedAt\":\"2026-08-20T12:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            await client.CloudSaves.UploadAsync("chess", new byte[] { 1 });

            Assert.IsNull(transport.Last.Header("If-Match"));
            Assert.IsNull(transport.Last.Header("If-None-Match"));
        }

        [Test]
        public async Task Upload_LosingTheRace_IsAPreconditionFailureCarryingTheCurrentVersion()
        {
            var transport = new FakeTransport()
                .EnqueueJson(412, "{\"error\":\"precondition_failed\",\"etag\":\"\\\"c3\\\"\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitPreconditionFailedException>(() =>
                client.CloudSaves.UploadAsync("chess", new byte[] { 1 }, StarhermitSaveCondition.IfNoSaveExists));

            Assert.AreEqual("*", transport.Last.Header("If-None-Match"));
            Assert.AreEqual("\"c3\"", error!.CurrentETag);
            Assert.AreEqual("precondition_failed", error.ErrorCode);
            Assert.AreEqual(1, transport.Requests.Count, "a refused condition is never retried");
        }

        [Test]
        public async Task Upload_OverTheAccountsLimits_NamesTheLimitInForce()
        {
            var cases = new (int Status, string Body, Type Exception, string Code, long Limit, long? Used)[]
            {
                (413, "{\"error\":\"A save may be at most 10 MB on this account.\",\"code\":\"cloud_save_too_large\",\"limit\":10485760}",
                    typeof(StarhermitQuotaExceededException), "cloud_save_too_large", 10485760, null),
                (409, "{\"error\":\"This account already keeps saves for 50 games.\",\"code\":\"cloud_save_slots_exhausted\",\"limit\":50}",
                    typeof(StarhermitConflictException), "cloud_save_slots_exhausted", 50, null),
                (507, "{\"error\":\"This account's saves may total at most 100 MB.\",\"code\":\"cloud_save_quota_exceeded\",\"limit\":104857600,\"used\":104000000}",
                    typeof(StarhermitQuotaExceededException), "cloud_save_quota_exceeded", 104857600, 104000000)
            };

            foreach (var (status, body, exceptionType, code, limit, used) in cases)
            {
                var transport = new FakeTransport().Always(_ => new FakeResponse(status, body));
                using var client = await TestHarness.SignedInAsync(transport);

                var thrown = (StarhermitApiException)Assert.CatchAsync(() => client.CloudSaves.UploadAsync("chess", new byte[] { 1 }))!;

                Assert.IsInstanceOf(exceptionType, thrown, $"status {status}");
                Assert.AreEqual(code, thrown.ErrorCode);
                Assert.AreEqual(limit, thrown.Limit);
                Assert.AreEqual(used, thrown.Used);
                Assert.AreEqual(1, transport.Requests.Count, $"status {status} is not worth a retry");
            }
        }

        [Test]
        public async Task Sync_UploadsOnlyOverTheVersionItCompared()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T12:00:00Z\",\"etag\":\"\\\"v1\\\"\"}")
                .EnqueueJson(200, "{\"gameKey\":\"chess\",\"sizeBytes\":1,\"updatedAt\":\"2026-08-20T14:00:00Z\",\"etag\":\"\\\"v2\\\"\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var local = new StarhermitLocalSaveState { Exists = true, ModifiedAt = Base.AddHours(2), LastSyncedServerTimestamp = Base };
            var result = await client.CloudSaves.CreateSynchronizer()
                .SynchronizeAsync("chess", local, _ => Task.FromResult(new byte[] { 1 }));

            Assert.AreEqual(StarhermitSyncOutcome.Uploaded, result.Outcome);
            Assert.AreEqual("\"v1\"", transport.Last.Header("If-Match"));
        }

        [Test]
        public async Task Sync_FirstUpload_RefusesToOverwriteASaveThatAppearedMeanwhile()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":false,\"sizeBytes\":0,\"updatedAt\":null,\"etag\":null}")
                .EnqueueJson(412, "{\"error\":\"precondition_failed\",\"etag\":\"\\\"other\\\"\"}")
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":5,\"updatedAt\":\"2026-08-20T12:30:00Z\",\"etag\":\"\\\"other\\\"\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                "chess",
                new StarhermitLocalSaveState { Exists = true, ModifiedAt = Base },
                _ => Task.FromResult(new byte[] { 1 }));

            Assert.AreEqual("*", transport.Requests[1].Header("If-None-Match"));
            Assert.AreEqual(StarhermitSyncOutcome.Conflict, result.Outcome);
            Assert.AreEqual("\"other\"", result.ServerInfo!.ETag, "the conflict reports the server as it is now");
        }

        [Test]
        public async Task Sync_LocalWins_StillRefusesToOverwriteAWriteItNeverCompared()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T13:00:00Z\",\"etag\":\"\\\"v1\\\"\"}")
                .EnqueueJson(412, "{\"error\":\"precondition_failed\",\"etag\":\"\\\"v2\\\"\"}")
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":9,\"updatedAt\":\"2026-08-20T13:05:00Z\",\"etag\":\"\\\"v2\\\"\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var local = new StarhermitLocalSaveState { Exists = true, ModifiedAt = Base.AddHours(2), LastSyncedServerTimestamp = Base };
            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                "chess", local, _ => Task.FromResult(new byte[] { 1 }), StarhermitConflictPolicy.LocalWins);

            Assert.AreEqual(StarhermitSyncOutcome.Conflict, result.Outcome);
            Assert.AreEqual(3, transport.Requests.Count, "nothing is written after the refusal");
        }

        [Test]
        public async Task Sync_Download_ReportsTheMarkerOfTheBytesItReturns()
        {
            // A write lands between the download and the metadata read: the newer marker must not be
            // handed back with the older bytes, so the sync reads again.
            var etagA = new Dictionary<string, string> { ["ETag"] = "\"a\"" };
            var etagB = new Dictionary<string, string> { ["ETag"] = "\"b\"" };
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T13:00:00Z\",\"etag\":\"\\\"a\\\"\"}")
                .Enqueue(_ => new FakeResponse(200, "old", etagA))
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T13:01:00Z\",\"etag\":\"\\\"b\\\"\"}")
                .Enqueue(_ => new FakeResponse(200, "new", etagB))
                .EnqueueJson(200, "{\"exists\":true,\"sizeBytes\":3,\"updatedAt\":\"2026-08-20T13:01:00Z\",\"etag\":\"\\\"b\\\"\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var result = await client.CloudSaves.CreateSynchronizer().SynchronizeAsync(
                "chess",
                new StarhermitLocalSaveState { Exists = false },
                _ => Task.FromResult(Array.Empty<byte>()));

            Assert.AreEqual(StarhermitSyncOutcome.Downloaded, result.Outcome);
            Assert.AreEqual("new", System.Text.Encoding.UTF8.GetString(result.DownloadedArchive!));
            Assert.AreEqual(new DateTimeOffset(2026, 8, 20, 13, 1, 0, TimeSpan.Zero), result.ServerTimestamp);
        }

        [Test]
        public async Task DownloadVersion_ReturnsTheVersionFromTheSameResponse()
        {
            var transport = new FakeTransport()
                .Enqueue(_ => new FakeResponse(200, "zip", new Dictionary<string, string> { ["ETag"] = "\"v9\"" }));
            using var client = await TestHarness.SignedInAsync(transport);

            var save = await client.CloudSaves.DownloadVersionAsync("chess");

            Assert.AreEqual("\"v9\"", save.ETag);
            Assert.AreEqual(3, save.Archive.Length);
        }

        [Test]
        public async Task DownloadIfChanged_TheHeldVersion_IsNotModifiedWithoutABody()
        {
            var transport = new FakeTransport()
                .Enqueue(_ => new FakeResponse(304, null, new Dictionary<string, string> { ["ETag"] = "\"5f3a\"" }));
            var logger = new RecordingLogger();
            using var client = await TestHarness.SignedInAsync(transport, logger: logger);

            var result = await client.CloudSaves.DownloadIfChangedAsync("chess", "5f3a");

            Assert.AreEqual("\"5f3a\"", transport.Last.Header("If-None-Match"), "bare text is quoted, as the API compares entity tags");
            Assert.IsTrue(result.NotModified);
            Assert.IsNull(result.Archive);
            Assert.AreEqual("\"5f3a\"", result.ETag);
            Assert.IsTrue(logger.NeverLogged("Error:"), "a 304 the caller asked for is an answer, not a failure");
        }

        [Test]
        public async Task DownloadIfChanged_ANewerVersion_ReturnsTheBytesAndTheirVersion()
        {
            var transport = new FakeTransport()
                .Enqueue(_ => new FakeResponse(200, "new-zip", new Dictionary<string, string> { ["ETag"] = "\"6000\"" }))
                .Enqueue(_ => new FakeResponse(200, "any-zip", new Dictionary<string, string> { ["ETag"] = "\"6000\"" }));
            using var client = await TestHarness.SignedInAsync(transport);

            var result = await client.CloudSaves.DownloadIfChangedAsync("chess", "\"5f3a\"");
            Assert.IsFalse(result.NotModified);
            Assert.AreEqual("new-zip", System.Text.Encoding.UTF8.GetString(result.Archive!));
            Assert.AreEqual("\"6000\"", result.ETag);

            await client.CloudSaves.DownloadIfChangedAsync("chess", null);
            Assert.IsNull(transport.Last.Header("If-None-Match"), "holding nothing downloads unconditionally");
        }

        [Test]
        public async Task NotModified_IsStillAFailureForARequestThatSentNoValidator()
        {
            var transport = new FakeTransport().Enqueue(_ => new FakeResponse(304));
            using var client = await TestHarness.SignedInAsync(transport);

            var failure = Assert.CatchAsync<StarhermitApiException>(() => client.CloudSaves.DownloadVersionAsync("chess"));
            Assert.AreEqual(304, failure!.Status, "an unconditional download cannot use a 304, so it must not read as an empty save");
        }

        [Test]
        public void SaveCondition_ForVersion_FollowsWhatWasRead()
        {
            Assert.AreEqual("*", StarhermitSaveCondition.ForVersion(Info("{\"exists\":false}")).IfNoneMatchHeader);
            Assert.AreEqual("\"v1\"", StarhermitSaveCondition.ForVersion(Info("{\"exists\":true,\"etag\":\"\\\"v1\\\"\"}")).IfMatchHeader);

            // A deployment that predates versions sends none; the write stays unconditional as before.
            var legacy = StarhermitSaveCondition.ForVersion(Info("{\"exists\":true,\"updatedAt\":\"2026-08-20T12:00:00Z\"}"));
            Assert.IsNull(legacy.IfMatchHeader);
            Assert.IsNull(legacy.IfNoneMatchHeader);
        }

        private static StarhermitCloudSaveInfo Info(string json) => StarhermitCloudSaveInfo.Read(Starhermit.Json.JsonParser.Parse(json));

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
