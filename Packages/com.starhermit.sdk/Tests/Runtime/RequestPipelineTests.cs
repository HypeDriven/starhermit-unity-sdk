using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Starhermit.Tests
{
    /// <summary>
    /// Route, verb, query, body, headers, response mapping, cancellation, documented errors and
    /// authorization - the checks every operation shares, exercised through real service calls.
    /// </summary>
    [TestFixture]
    public class RequestPipelineTests
    {
        [Test]
        public async Task Get_BuildsVersionedRouteAndQuery()
        {
            var transport = new FakeTransport().EnqueueJson(200, "{\"items\":[],\"totalCount\":0,\"page\":1,\"pageSize\":20}");
            using var client = await TestHarness.SignedInAsync(transport);

            await client.Software.GetTitlesAsync(new StarhermitCatalogQuery { Search = "space game", Category = "action" }, page: 2, pageSize: 50);

            Assert.AreEqual("GET", transport.Last.Method);
            Assert.AreEqual("/api/v1/software", transport.Last.Path);
            StringAssert.Contains("q=space%20game", transport.Last.Query);
            StringAssert.Contains("category=action", transport.Last.Query);
            StringAssert.Contains("page=2", transport.Last.Query);
            StringAssert.Contains("pageSize=50", transport.Last.Query);
        }

        [Test]
        public async Task Post_SendsCamelCaseJsonBody()
        {
            var transport = new FakeTransport().EnqueueJson(200, "{\"id\":\"" + Guid.Empty + "\",\"type\":\"direct\"}");
            using var client = await TestHarness.SignedInAsync(transport);
            var friend = Guid.NewGuid();

            await client.Chat.CreateDirectConversationAsync(friend);

            Assert.AreEqual("POST", transport.Last.Method);
            Assert.AreEqual("/api/v1/chat/conversations", transport.Last.Path);
            StringAssert.Contains("\"friendUserId\":\"" + friend.ToString("D") + "\"", transport.Last.Body);
            StringAssert.Contains("application/json", transport.Last.ContentType ?? string.Empty);
        }

        [Test]
        public async Task EveryRequest_CarriesAcceptAndSdkVersionHeaders()
        {
            var transport = new FakeTransport().EnqueueJson(200, "{}");
            using var client = await TestHarness.SignedInAsync(transport);

            await client.Me.GetProfileAsync();

            Assert.AreEqual("application/json", transport.Last.Header("Accept"));
            Assert.AreEqual(StarhermitSdk.Version, transport.Last.Header(StarhermitSdk.VersionHeader));
            StringAssert.StartsWith("StarhermitUnitySDK/", transport.Last.Header("User-Agent"));
        }

        [Test]
        public async Task AuthenticatedRequest_CarriesTheSessionBearerToken()
        {
            var transport = new FakeTransport().EnqueueJson(200, "{}");
            using var client = await TestHarness.SignedInAsync(transport);

            await client.Me.GetProfileAsync();

            Assert.IsNotNull(transport.Last.BearerToken);
            Assert.AreEqual(client.Session!.AccessToken, transport.Last.BearerToken);
        }

        [Test]
        public async Task AnonymousRequest_SendsNoAuthorizationHeader()
        {
            var transport = new FakeTransport().EnqueueJson(200, "{\"serverTime\":0,\"serverTimeIso\":\"2026-08-20T12:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            await client.Time.SynchronizeAsync();

            Assert.IsNull(transport.Last.Header("Authorization"),
                "the auth handshake and public routes must not carry a session");
        }

        [Test]
        public void AccountRequest_WithoutSession_FailsWithAuthenticationError()
        {
            var transport = new FakeTransport();
            using var client = StarhermitClient.Create(TestHarness.Options(transport));

            Assert.ThrowsAsync<StarhermitAuthenticationException>(() => client.Me.GetProfileAsync());
            Assert.AreEqual(0, transport.Requests.Count, "no request is sent when there is no credential to send");
        }

        [Test]
        public async Task GameScopedRequest_WithoutLaunchToken_ReportsTheMissingCapability()
        {
            var transport = new FakeTransport();
            using var client = await TestHarness.SignedInAsync(transport);
            var game = client.Games.ForSlug("chess").WithLaunchToken();

            var error = Assert.ThrowsAsync<StarhermitFeatureUnavailableException>(() => game.GetInfoAsync());
            StringAssert.Contains("launch token", error!.Message);
        }

        [Test]
        public async Task GameScopedRequest_UsesTheLaunchTokenNotTheSession()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"token\":\"launch-token-xyz\",\"expiresInSeconds\":900}")
                .EnqueueJson(200, "{\"slug\":\"chess\",\"name\":\"Chess\",\"enabled\":true}");

            using var client = await TestHarness.SignedInAsync(transport);
            var game = client.Games.ForSlug("chess");
            await game.AcquireLaunchTokenAsync();

            await game.WithLaunchToken().GetInfoAsync();

            Assert.AreEqual("launch-token-xyz", transport.Last.BearerToken);
            Assert.AreNotEqual(client.Session!.AccessToken, transport.Last.BearerToken);
            Assert.IsNotNull(client.Session, "minting a launch token must not replace the account session");
            Assert.IsNull(transport.Last.Header(StarhermitHeaders.GameSlug),
                "the slug header steers the pipeline and must not reach the wire");
        }

        [Test]
        public async Task ErrorStatuses_MapToTypedExceptions()
        {
            var cases = new (int Status, Type Exception)[]
            {
                (400, typeof(StarhermitBadRequestException)),
                (402, typeof(StarhermitEntitlementException)),
                (403, typeof(StarhermitAuthorizationException)),
                (404, typeof(StarhermitNotFoundException)),
                (409, typeof(StarhermitConflictException)),
                (429, typeof(StarhermitRateLimitException)),
                (500, typeof(StarhermitServerException))
            };

            foreach (var (status, exceptionType) in cases)
            {
                var transport = new FakeTransport().Always(_ => new FakeResponse(status, "{\"error\":\"nope\"}"));
                using var client = await TestHarness.SignedInAsync(transport);

                var thrown = Assert.CatchAsync(() => client.Me.GetProfileAsync());
                Assert.IsInstanceOf(exceptionType, thrown, $"status {status}");
                Assert.AreEqual(status, ((StarhermitApiException)thrown!).Status);
                Assert.AreEqual("nope", ((StarhermitApiException)thrown).ServerMessage);
            }
        }

        [Test]
        public async Task Unauthorized_AfterAFailedRefresh_KeepsTheServersOwnMessage()
        {
            // 401 on the call, 401 on the refresh: the session is over, and the caller should see what
            // the API said rather than a message the SDK invented.
            var transport = new FakeTransport().Always(_ => new FakeResponse(401, "{\"error\":\"nope\"}"));
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitAuthenticationException>(() => client.Me.GetProfileAsync());

            Assert.AreEqual(401, error!.Status);
            Assert.AreEqual("nope", error.ServerMessage);
            Assert.IsFalse(client.IsAuthenticated, "a definitive refusal ends the session");
        }

        [Test]
        public async Task ValidationProblemDetails_MapToFieldErrors()
        {
            var body = "{\"title\":\"One or more validation errors occurred.\",\"status\":400," +
                       "\"errors\":{\"version\":[\"The Version field is required.\"]},\"traceId\":\"trace-77\"}";
            var transport = new FakeTransport().Always(_ => new FakeResponse(400, body));
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitValidationException>(() => client.Me.GetProfileAsync());

            Assert.IsTrue(error!.Errors.ContainsKey("version"));
            Assert.AreEqual("The Version field is required.", error.Errors["version"][0]);
            Assert.AreEqual("trace-77", error.RequestId);
        }

        [Test]
        public async Task RateLimited_CarriesRetryAfter()
        {
            var headers = new System.Collections.Generic.Dictionary<string, string> { ["Retry-After"] = "42" };
            var transport = new FakeTransport().Always(_ => new FakeResponse(429, "{\"error\":\"slow down\"}", headers));
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitRateLimitException>(() => client.Me.GetProfileAsync());

            Assert.AreEqual(TimeSpan.FromSeconds(42), error!.RetryAfter);
        }

        [Test]
        public async Task Cancellation_SurfacesAsOperationCanceled()
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(200, "{}"));
            using var client = await TestHarness.SignedInAsync(transport);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(() => client.Me.GetProfileAsync(cancellation.Token));
        }

        [Test]
        public async Task NoContentResponse_IsNotAnError()
        {
            var transport = new FakeTransport().Enqueue(_ => new FakeResponse(204));
            using var client = await TestHarness.SignedInAsync(transport);

            Assert.DoesNotThrowAsync(() => client.Me.SendHeartbeatAsync());
        }

        [Test]
        public async Task PageMetadata_ComesFromTheServer()
        {
            var transport = new FakeTransport().EnqueueJson(
                200,
                "{\"items\":[{\"id\":\"" + Guid.NewGuid() + "\"}],\"totalCount\":97,\"page\":3,\"pageSize\":25}");
            using var client = await TestHarness.SignedInAsync(transport);

            var page = await client.Software.GetTitlesAsync(page: 3, pageSize: 25);

            Assert.AreEqual(97, page.TotalCount);
            Assert.AreEqual(3, page.Page);
            Assert.AreEqual(25, page.PageSize);
            Assert.AreEqual(4, page.TotalPages);
            Assert.IsTrue(page.HasMore);
        }

        [Test]
        public async Task PageMetadata_AlsoReadsTheLeaderboardSpellingOfTotal()
        {
            var transport = new FakeTransport().EnqueueJson(
                200,
                "{\"items\":[],\"total\":12,\"page\":1,\"pageSize\":20}");
            using var client = await TestHarness.SignedInAsync(transport);

            var page = await client.Leaderboards.GetEntriesAsync(Guid.NewGuid());

            Assert.AreEqual(12, page.TotalCount, "the deployment spells this 'total' on leaderboard routes");
        }

        [Test]
        public async Task EnumeratePages_StopsWhenTheServerRunsOut()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"items\":[{\"id\":\"" + Guid.NewGuid() + "\"}],\"totalCount\":2,\"page\":1,\"pageSize\":1}")
                .EnqueueJson(200, "{\"items\":[{\"id\":\"" + Guid.NewGuid() + "\"}],\"totalCount\":2,\"page\":2,\"pageSize\":1}");
            using var client = await TestHarness.SignedInAsync(transport);

            var count = 0;
            await foreach (var _ in client.Software.EnumerateTitlesAsync(pageSize: 1)) count++;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, transport.Requests.Count, "pages are fetched lazily, one per page of results");
        }

        [Test]
        public async Task UnknownResponseMembers_StayReachableThroughRawJson()
        {
            var transport = new FakeTransport().EnqueueJson(
                200,
                "{\"id\":\"" + Guid.Empty + "\",\"username\":\"ada\",\"shippedAfterThisSdk\":\"value\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var profile = await client.Me.GetProfileAsync();

            Assert.AreEqual("ada", profile.Username);
            Assert.AreEqual("value", profile.RawJson["shippedAfterThisSdk"].AsString());
        }

        [Test]
        public async Task RawClient_ReachesAnEndpointTheSdkDoesNotType()
        {
            var transport = new FakeTransport().EnqueueJson(200, "{\"ok\":true}");
            using var client = await TestHarness.SignedInAsync(transport);

            var request = StarhermitRawClient.Request("POST", "some/future/endpoint").WithJson("{\"a\":1}");
            var json = await client.Raw.SendForJsonAsync(request);

            Assert.IsTrue(json["ok"].AsBoolean());
            Assert.AreEqual("/api/v1/some/future/endpoint", transport.Last.Path);
            Assert.IsNotNull(transport.Last.BearerToken, "the escape hatch still authenticates like a typed call");
        }
    }
}
