using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Starhermit.Json;

namespace Starhermit.Tests
{
    /// <summary>
    /// Client construction, option validation, disposal, isolation between clients, and the model
    /// rules that let a game survive a deployment shipping something new.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class ClientAndModelTests
    {
        [Test]
        public void Create_PerformsNoIo()
        {
            var transport = new FakeTransport();

            using var client = StarhermitClient.Create(TestHarness.Options(transport));

            Assert.AreEqual(0, transport.Requests.Count, "constructing a client must cost nothing");
            Assert.IsFalse(client.IsAuthenticated);
        }

        [Test]
        public void Create_RefusesAPlainHttpEndpointUnlessDevelopmentIsDeclared()
        {
            var options = TestHarness.Options(new FakeTransport());
            options.ApiBaseUri = new Uri("http://api.test.starhermit.com/api/v1/");

            var error = Assert.Throws<ArgumentException>(() => StarhermitClient.Create(options));
            StringAssert.Contains("AllowInsecureTransport", error!.Message);

            options.AllowInsecureTransport = true;
            Assert.DoesNotThrow(() => StarhermitClient.Create(options).Dispose());
        }

        [Test]
        public void Create_CopiesOptionsSoLaterEditsDoNotLeakIn()
        {
            var options = TestHarness.Options(new FakeTransport());
            using var client = StarhermitClient.Create(options);

            options.GameSlug = "changed-after-creation";

            Assert.IsNull(client.Options.GameSlug);
        }

        [Test]
        public void WebSocketAddress_IsDerivedFromTheApiAddress()
        {
            var options = new StarhermitOptions { ApiBaseUri = new Uri("https://api.starhermit.com/api/v1/") };

            Assert.AreEqual(new Uri("wss://api.starhermit.com/ws/v1/"), options.ResolveWebSocketBaseUri());

            options.ApiBaseUri = new Uri("http://starhermit.test:5050/api/v1/");
            options.AllowInsecureTransport = true;
            Assert.AreEqual(new Uri("ws://starhermit.test:5050/ws/v1/"), options.ResolveWebSocketBaseUri());
        }

        [Test]
        public async Task Initialize_WithNoStoredSession_ReturnsNullWithoutCallingTheApi()
        {
            var transport = new FakeTransport();
            using var client = StarhermitClient.Create(TestHarness.Options(transport));

            var session = await client.InitializeAsync();

            Assert.IsNull(session);
            Assert.AreEqual(0, transport.Requests.Count);
        }

        [Test]
        public async Task TwoClients_KeepSeparateSessions()
        {
            // No static state anywhere: a test suite, and a game with a second environment open, both
            // depend on this.
            var first = await TestHarness.SignedInAsync(new FakeTransport());
            using var second = StarhermitClient.Create(TestHarness.Options(new FakeTransport()));

            Assert.IsTrue(first.IsAuthenticated);
            Assert.IsFalse(second.IsAuthenticated);

            first.Dispose();
            Assert.IsFalse(second.IsAuthenticated);
        }

        [Test]
        public async Task Dispose_IsIdempotentAndStopsFurtherWork()
        {
            var client = await TestHarness.SignedInAsync(new FakeTransport());

            client.Dispose();
            Assert.DoesNotThrow(() => client.Dispose());
            Assert.Throws<ObjectDisposedException>(() => client.CreateChatConnection());
        }

        [Test]
        public async Task Diagnostics_AreSafeToDisplay()
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(500, "{\"error\":\"boom\"}"));
            using var client = await TestHarness.SignedInAsync(transport);

            Assert.ThrowsAsync<StarhermitServerException>(() => client.Me.GetProfileAsync());
            var snapshot = client.GetDiagnostics();

            Assert.AreEqual(TestHarness.TestUserId, snapshot.UserId);
            Assert.AreEqual(0, snapshot.InFlightRequests);
            Assert.IsNotNull(snapshot.LastError);
            StringAssert.DoesNotContain(client.Session!.AccessToken, snapshot.LastError!);
        }

        [Test]
        public void ServerClock_CorrectsForRoundTripAndReportsFreshness()
        {
            var device = new TestClock();
            var clock = new StarhermitServerClock(device);

            Assert.AreEqual(TimeSpan.Zero, clock.Offset);
            Assert.IsNull(clock.Age);

            var sentAt = device.UtcNow;
            var receivedAt = sentAt.AddMilliseconds(200);
            var serverTime = sentAt.AddMinutes(5);
            device.Advance(TimeSpan.FromMilliseconds(200));

            clock.Synchronize(serverTime, sentAt, receivedAt);

            Assert.AreEqual(300d, clock.Offset.TotalSeconds, 1d, "half the round trip is credited to the reading");
            Assert.AreEqual(TimeSpan.FromMilliseconds(200), clock.RoundTrip);
            Assert.IsNotNull(clock.Age);
        }

        [Test]
        public void PrivacyLevels_ReadUnknownValuesAsThePrivateOne()
        {
            var json = JsonParser.Parse("{\"onlineStatus\":2,\"currentlyPlaying\":99,\"hoursPlayed\":\"FriendsOnly\"}");

            var privacy = StarhermitPrivacySettings.Read(json);

            Assert.AreEqual(StarhermitPrivacyLevel.Public, privacy.OnlineStatus);
            Assert.AreEqual(StarhermitPrivacyLevel.Private, privacy.CurrentlyPlaying,
                "a level from a newer deployment must not accidentally read as Public");
            Assert.AreEqual(StarhermitPrivacyLevel.FriendsOnly, privacy.HoursPlayed);
        }

        [Test]
        public void Models_KeepUnknownEnumStringsAsTheyArrived()
        {
            var json = JsonParser.Parse("{\"id\":\"" + Guid.NewGuid() + "\",\"type\":\"broadcast\",\"joinPolicy\":\"invite_only\"}");

            var conversation = StarhermitConversation.Read(json);

            Assert.AreEqual("broadcast", conversation.Type, "an unknown kind is preserved, not coerced");
            Assert.AreEqual("invite_only", conversation.JoinPolicy);
        }

        [Test]
        public void Models_ExposeGameDefinedDocumentsUntouched()
        {
            var json = JsonParser.Parse(
                "{\"sessionId\":\"" + Guid.NewGuid() + "\",\"status\":\"finished\"," +
                "\"result\":{\"winner\":\"white\",\"moves\":41}}");

            var session = StarhermitGameSession.Read(json);

            Assert.AreEqual("white", session.Result["winner"].AsString());
            Assert.AreEqual(41, session.Result["moves"].AsInt32());
        }

        [Test]
        public void Page_ReadsABareArrayAsOneCompletePage()
        {
            var json = JsonParser.Parse("[{\"id\":\"" + Guid.NewGuid() + "\"},{\"id\":\"" + Guid.NewGuid() + "\"}]");

            var page = StarhermitPage<StarhermitEntitlement>.Read(json, StarhermitEntitlement.Read);

            Assert.AreEqual(2, page.Count);
            Assert.AreEqual(2, page.TotalCount);
            Assert.IsFalse(page.HasMore);
        }

        [Test]
        public void FileStore_RefusesAPathThatEscapesItsRoot()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "starhermit-tests-" + Guid.NewGuid().ToString("N"));
            var store = new SystemFileStore(root);

            Assert.Throws<StarhermitPathEscapeException>(() => store.Resolve("../escaped.txt"));
            Assert.DoesNotThrow(() => store.Resolve("saves/slot1.zip"));
            System.IO.Directory.Delete(root, recursive: true);
        }

        [Test]
        public async Task FileStore_PromotesOnlyOnCommit()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "starhermit-tests-" + Guid.NewGuid().ToString("N"));
            var store = new SystemFileStore(root);

            using (var abandoned = await store.BeginWriteAsync("save.bin"))
            {
                await abandoned.Stream.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3);
            }

            Assert.IsFalse(await store.ExistsAsync("save.bin"), "an abandoned write leaves nothing behind");

            using (var committed = await store.BeginWriteAsync("save.bin"))
            {
                await committed.Stream.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3);
                await committed.CommitAsync();
            }

            Assert.IsTrue(await store.ExistsAsync("save.bin"));
            System.IO.Directory.Delete(root, recursive: true);
        }

        [Test]
        public void OAuthResult_ParsesQueryAndFragmentCallbacks()
        {
            var fragment = StarhermitOAuthResult.Parse("myapp://callback#access_token=a&refresh_token=b&token_type=Bearer");
            Assert.AreEqual("a", fragment.AccessToken);
            Assert.AreEqual("b", fragment.RefreshToken);

            var query = StarhermitOAuthResult.Parse("https://game.test/cb?code=xyz&state=st");
            Assert.AreEqual("xyz", query.Code);
            Assert.AreEqual("st", query.State);

            var refused = StarhermitOAuthResult.Parse("https://game.test/cb?error=access_denied&error_description=No");
            Assert.AreEqual("access_denied", refused.Error);
        }

        [Test]
        public void Challenge_RebuildsTheExactBytesTheServerVerifies()
        {
            // The server verifies against its own .NET serialisation of the payload, which uses
            // property names rather than the camel case the payload arrives in.
            var json = JsonParser.Parse(
                "{\"challengeId\":\"7b8d4a52-0000-4000-8000-000000000001\",\"expiresIn\":300," +
                "\"payload\":{\"challengeId\":\"7b8d4a52-0000-4000-8000-000000000001\",\"fingerprint\":\"fp\"," +
                "\"issuer\":\"starhermit\",\"audience\":\"starhermit\",\"expiry\":\"2026-08-20T12:05:00+00:00\"," +
                "\"nonce\":\"n0nce\",\"clientTimestamp\":\"2026-08-20T12:00:00+00:00\"}}");

            var challenge = StarhermitChallenge.Read(json);
            var canonical = System.Text.Encoding.UTF8.GetString(challenge.CanonicalPayload);

            Assert.AreEqual(
                "{\"ChallengeId\":\"7b8d4a52-0000-4000-8000-000000000001\",\"Fingerprint\":\"fp\"," +
                "\"Issuer\":\"starhermit\",\"Audience\":\"starhermit\",\"Expiry\":\"2026-08-20T12:05:00+00:00\"," +
                "\"Nonce\":\"n0nce\",\"ClientTimestamp\":\"2026-08-20T12:00:00+00:00\"}",
                canonical);
            Assert.AreEqual(TimeSpan.FromSeconds(300), challenge.ExpiresIn);
        }
    }
}
