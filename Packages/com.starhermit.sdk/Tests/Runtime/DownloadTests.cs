using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Starhermit.Tests
{
    /// <summary>
    /// Downloads of entitled titles: signed URLs, resume negotiation, and the rule that a partial file
    /// is never promoted.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class DownloadTests
    {
        [Test]
        public async Task RequestDownloadUrl_UsesTheSignedUrlWithoutForwardingTheSession()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"downloadUrl\":\"https://cdn.test/build.zip?X-Amz-Signature=abc\"}")
                .Enqueue(_ => new FakeResponse(200, "archive-bytes"));

            using var client = await TestHarness.SignedInAsync(transport);

            using var download = await client.Software.OpenDownloadAsync(Guid.NewGuid());

            Assert.AreEqual("cdn.test", transport.Last.Uri.Host);
            Assert.IsNull(transport.Last.Header("Authorization"),
                "a signed URL carries its own credential; forwarding the player's would hand it to that host");
            Assert.AreEqual(200, download.Status);
        }

        [Test]
        public async Task Resume_SendsARangeHeaderAndReportsAContinuedTransfer()
        {
            var headers = new Dictionary<string, string>
            {
                ["Content-Range"] = "bytes 1024-4095/4096",
                ["Accept-Ranges"] = "bytes"
            };

            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"downloadUrl\":\"https://cdn.test/build.zip\"}")
                .Enqueue(_ => new FakeResponse(206, "tail", headers));

            using var client = await TestHarness.SignedInAsync(transport);

            using var download = await client.Software.OpenDownloadAsync(Guid.NewGuid(), resumeFromBytes: 1024);

            Assert.AreEqual("bytes=1024-", transport.Last.Header("Range"));
            Assert.IsTrue(download.IsResumed);
            Assert.IsTrue(download.SupportsResume);
            Assert.AreEqual(4096, download.TotalLength);
        }

        [Test]
        public async Task Resume_AgainstAnOriginThatIgnoresRanges_ReportsAWholeFile()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"downloadUrl\":\"https://cdn.test/build.zip\"}")
                .Enqueue(_ => new FakeResponse(200, "whole-file"));

            using var client = await TestHarness.SignedInAsync(transport);

            using var download = await client.Software.OpenDownloadAsync(Guid.NewGuid(), resumeFromBytes: 1024);

            Assert.IsFalse(download.IsResumed,
                "appending a whole file to a partial one would corrupt it while passing every length check");
            Assert.AreEqual(1024, download.RequestedOffset);
        }

        [Test]
        public async Task ExpiredSignedUrl_SurfacesAsATypedApiFailure()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"downloadUrl\":\"https://cdn.test/build.zip\"}")
                .Enqueue(_ => new FakeResponse(403, "expired"));

            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitAuthorizationException>(
                () => client.Software.OpenDownloadAsync(Guid.NewGuid()));
            StringAssert.Contains("expired", error!.Message);
        }

        [Test]
        public async Task DownloadToFile_RejectsAChecksumMismatchAndPromotesNothing()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "starhermit-dl-" + Guid.NewGuid().ToString("N"));
            var store = new SystemFileStore(root);

            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"downloadUrl\":\"https://cdn.test/build.zip\"}")
                .Enqueue(_ => new FakeResponse(200, "not-the-expected-bytes"));

            var options = TestHarness.Options(transport);
            options.FileStore = store;
            options.TokenStore = new InMemoryTokenStore(new StarhermitStoredSession(
                TestHarness.Jwt(new TestClock().UtcNow.AddMinutes(15)), "refresh", TestHarness.TestUserId));

            using var client = StarhermitClient.Create(options);
            await client.InitializeAsync();

            Assert.ThrowsAsync<StarhermitProtocolException>(() => client.Software.DownloadTitleAsync(
                Guid.NewGuid(),
                "build.zip",
                expectedSha256: new string('0', 64)));

            Assert.IsFalse(await store.ExistsAsync("build.zip"),
                "a file that failed its checksum is never promoted");
            System.IO.Directory.Delete(root, recursive: true);
        }

        [Test]
        public async Task DownloadToFile_WithoutAFileStore_ReportsTheMissingAdapter()
        {
            var transport = new FakeTransport();
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitFeatureUnavailableException>(
                () => client.Software.DownloadTitleAsync(Guid.NewGuid(), "build.zip"));

            Assert.AreEqual(StarhermitFeatureReasons.AdapterNotConfigured, error!.Reason);
        }
    }
}
