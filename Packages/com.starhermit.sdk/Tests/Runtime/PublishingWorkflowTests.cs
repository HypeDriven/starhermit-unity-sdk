using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Starhermit.Publishing;

namespace Starhermit.Tests
{
    /// <summary>
    /// The optional publishing assembly's multi-step flow: upload every asset to its signed target,
    /// then finalise. An interrupted publish must leave the live build alone.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class PublishingWorkflowTests
    {
        [Test]
        public async Task Publish_UploadsEveryAssetThenFinalises()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "[{\"type\":\"installer\",\"uploadUrl\":\"https://cdn.test/upload/installer?sig=a\",\"fieldKey\":\"k1\"}," +
                                  "{\"type\":\"manifest\",\"uploadUrl\":\"https://cdn.test/upload/manifest?sig=b\",\"fieldKey\":\"k2\"}]")
                .Enqueue(_ => new FakeResponse(200))
                .Enqueue(_ => new FakeResponse(200))
                .Enqueue(_ => new FakeResponse(204));

            using var client = await TestHarness.SignedInAsync(transport);
            var titleId = Guid.NewGuid();

            var result = await new StarhermitBuildPublisher(client).PublishAsync(
                titleId,
                "1.4.0",
                "Fixes the thing.",
                new[]
                {
                    new StarhermitBuildAsset("installer", () => new MemoryStream(Encoding.UTF8.GetBytes("installer-bytes"))),
                    new StarhermitBuildAsset("manifest", () => new MemoryStream(Encoding.UTF8.GetBytes("manifest-bytes")))
                });

            Assert.AreEqual(new[] { "installer", "manifest" }, result.UploadedTypes);
            // targets, two uploads, finalise.
            Assert.AreEqual(4, transport.Requests.Count);

            Assert.AreEqual("cdn.test", transport.Requests[1].Uri.Host);
            Assert.IsNull(transport.Requests[1].Header("Authorization"),
                "the player's session must not be handed to a storage host");

            var finalize = transport.Requests[3];
            Assert.AreEqual("/api/v1/publisher/software/build/finalize", finalize.Path);
            StringAssert.Contains("\"version\":\"1.4.0\"", finalize.Body);
            StringAssert.Contains("\"fieldKey\":\"k1\"", finalize.Body);

            // The checksum is of the actual bytes, computed from a second read of the same source.
            StringAssert.Contains("\"checksum\":\"", finalize.Body);
        }

        [Test]
        public async Task Publish_WithAMissingAsset_FinalisesNothing()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "[{\"type\":\"installer\",\"uploadUrl\":\"https://cdn.test/u\",\"fieldKey\":\"k1\"}]");

            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<InvalidOperationException>(() =>
                new StarhermitBuildPublisher(client).PublishAsync(
                    Guid.NewGuid(),
                    "1.0.0",
                    "notes",
                    Array.Empty<StarhermitBuildAsset>()));

            StringAssert.Contains("still serving players", error!.Message);
            Assert.AreEqual(1, transport.Requests.Count, "nothing was uploaded and nothing was finalised");
        }

        [Test]
        public async Task Publish_WhenAnUploadIsRefused_DoesNotFinalise()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "[{\"type\":\"installer\",\"uploadUrl\":\"https://cdn.test/u\",\"fieldKey\":\"k1\"}]")
                .Enqueue(_ => new FakeResponse(403, "signature expired"));

            using var client = await TestHarness.SignedInAsync(transport);

            Assert.ThrowsAsync<StarhermitAuthorizationException>(() =>
                new StarhermitBuildPublisher(client).PublishAsync(
                    Guid.NewGuid(),
                    "1.0.0",
                    "notes",
                    new[] { new StarhermitBuildAsset("installer", () => new MemoryStream(new byte[] { 1 })) }));

            Assert.AreEqual(2, transport.Requests.Count, "the finalise call was never made");
        }
    }
}
