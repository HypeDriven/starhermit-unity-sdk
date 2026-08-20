using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Starhermit.Tests
{
    /// <summary>
    /// What is worth sending again, and what is a decision that will not change. A retry that cannot
    /// succeed only costs the player battery and, when it is a rate limit, digs the hole deeper.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class RetryTests
    {
        [Test]
        public async Task TransientServerError_OnAGet_IsRetried()
        {
            var transport = new FakeTransport()
                .EnqueueJson(503, "{\"error\":\"restarting\"}")
                .EnqueueJson(200, "{\"id\":\"" + Guid.Empty + "\",\"username\":\"ada\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var profile = await client.Me.GetProfileAsync();

            Assert.AreEqual("ada", profile.Username);
            Assert.AreEqual(2, transport.Requests.Count);
        }

        [Test]
        public async Task ConnectionFailure_OnAGet_IsRetried()
        {
            var transport = new FakeTransport()
                .Enqueue(_ => FakeResponse.TransportFailure())
                .EnqueueJson(200, "{}");
            using var client = await TestHarness.SignedInAsync(transport);

            await client.Me.GetProfileAsync();

            Assert.AreEqual(2, transport.Requests.Count);
        }

        [Test]
        public async Task Timeout_OnAGet_IsRetried()
        {
            var transport = new FakeTransport()
                .Enqueue(_ => FakeResponse.Timeout())
                .EnqueueJson(200, "{}");
            using var client = await TestHarness.SignedInAsync(transport);

            await client.Me.GetProfileAsync();

            Assert.AreEqual(2, transport.Requests.Count);
        }

        [Test]
        public async Task Post_IsNotRetriedByDefault()
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(503, "{\"error\":\"restarting\"}"));
            using var client = await TestHarness.SignedInAsync(transport);

            Assert.ThrowsAsync<StarhermitServerException>(() => client.Chat.SendMessageAsync(Guid.NewGuid(), "hello"));
            Assert.AreEqual(1, transport.Requests.Count,
                "repeating a POST could post twice; the endpoint has to opt in");
        }

        [Test]
        public async Task Post_MarkedIdempotent_IsRetried()
        {
            var transport = new FakeTransport()
                .EnqueueJson(503, "{\"error\":\"restarting\"}")
                .EnqueueJson(200, "{\"ok\":true}");
            using var client = await TestHarness.SignedInAsync(transport);

            var request = StarhermitRawClient.Request("POST", "safe/endpoint").WithJson("{}").AsIdempotent("key-1");
            await client.Raw.SendForJsonAsync(request);

            Assert.AreEqual(2, transport.Requests.Count);
            Assert.AreEqual("key-1", transport.Last.Header("Idempotency-Key"));
        }

        [Test]
        public async Task DecisionStatuses_AreNeverRetried()
        {
            foreach (var status in new[] { 400, 403, 404, 409, 422 })
            {
                var transport = new FakeTransport().Always(_ => new FakeResponse(status, "{\"error\":\"no\"}"));
                using var client = await TestHarness.SignedInAsync(transport);

                Assert.CatchAsync(() => client.Me.GetProfileAsync());
                Assert.AreEqual(1, transport.Requests.Count, $"status {status} will answer identically forever");
            }
        }

        [Test]
        public async Task RetryAfter_IsHonouredInsteadOfTheComputedBackoff()
        {
            var headers = new Dictionary<string, string> { ["Retry-After"] = "0" };
            var transport = new FakeTransport()
                .Enqueue(_ => new FakeResponse(429, "{\"error\":\"slow\"}", headers))
                .EnqueueJson(200, "{}");
            using var client = await TestHarness.SignedInAsync(transport);

            await client.Me.GetProfileAsync();

            Assert.AreEqual(2, transport.Requests.Count);
        }

        [Test]
        public async Task RetryAfter_LongerThanTheCap_EndsTheAttempt()
        {
            var headers = new Dictionary<string, string> { ["Retry-After"] = "600" };
            var transport = new FakeTransport().Always(_ => new FakeResponse(429, "{\"error\":\"slow\"}", headers));
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitRateLimitException>(() => client.Me.GetProfileAsync());

            Assert.AreEqual(1, transport.Requests.Count, "the SDK will not block a game for ten minutes inside one call");
            Assert.AreEqual(TimeSpan.FromSeconds(600), error!.RetryAfter, "the caller is told how long to wait");
        }

        [Test]
        public async Task NonReplayableBody_IsNeverRetried()
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(503, "{\"error\":\"restarting\"}"));
            using var client = await TestHarness.SignedInAsync(transport);

            var source = new System.IO.MemoryStream(new byte[] { 1, 2, 3 });
            var request = StarhermitRawClient.Request("PUT", "upload/once")
                .WithContent(StarhermitContent.SingleUseStream(source, 3));

            Assert.ThrowsAsync<StarhermitServerException>(() => client.Raw.SendForJsonAsync(request));
            Assert.AreEqual(1, transport.Requests.Count,
                "replaying a consumed stream would send a truncated body and call it a success");
        }

        [Test]
        public async Task RetriesStopAtTheConfiguredAttemptLimit()
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(503, "{\"error\":\"restarting\"}"));
            using var client = await TestHarness.SignedInAsync(transport);

            Assert.ThrowsAsync<StarhermitServerException>(() => client.Me.GetProfileAsync());

            Assert.AreEqual(3, transport.Requests.Count, "three attempts, as the policy says");
        }

        [Test]
        public void Backoff_GrowsAndStaysWithinTheJitterBand()
        {
            var policy = new StarhermitRetryPolicy
            {
                MaxAttempts = 10,
                BaseDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromSeconds(10),
                JitterFactor = 0.25,
                Budget = StarhermitRetryBudget.Unlimited
            };

            var outcome = new StarhermitAttemptOutcome(503, false, false, true, null);

            for (var attempt = 1; attempt <= 5; attempt++)
            {
                Assert.IsTrue(policy.ShouldRetry(attempt, outcome, out var delay));
                var nominal = Math.Min(100 * Math.Pow(2, attempt - 1), 10000);
                Assert.GreaterOrEqual(delay.TotalMilliseconds, nominal * 0.75 - 1);
                Assert.LessOrEqual(delay.TotalMilliseconds, nominal * 1.25 + 1);
            }
        }

        [Test]
        public void SharedBudget_StopsARetryStorm()
        {
            var clock = new TestClock();
            var budget = new StarhermitRetryBudget(capacity: 2, tokensPerSecond: 0, clock);
            var policy = new StarhermitRetryPolicy { MaxAttempts = 10, Budget = budget, JitterFactor = 0 };
            var outcome = new StarhermitAttemptOutcome(503, false, false, true, null);

            Assert.IsTrue(policy.ShouldRetry(1, outcome, out _));
            Assert.IsTrue(policy.ShouldRetry(1, outcome, out _));
            Assert.IsFalse(policy.ShouldRetry(1, outcome, out _),
                "several clients sharing one outage must not multiply it");
        }

        [Test]
        public void RetryAfter_ParsesSecondsAndHttpDates()
        {
            var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

            Assert.AreEqual(TimeSpan.FromSeconds(30), StarhermitRetryPolicy.ParseRetryAfter("30", now));
            Assert.AreEqual(TimeSpan.FromSeconds(60), StarhermitRetryPolicy.ParseRetryAfter("Thu, 20 Aug 2026 12:01:00 GMT", now));
            Assert.AreEqual(TimeSpan.Zero, StarhermitRetryPolicy.ParseRetryAfter("Thu, 20 Aug 2026 11:59:00 GMT", now));
            Assert.IsNull(StarhermitRetryPolicy.ParseRetryAfter("nonsense", now));
            Assert.IsNull(StarhermitRetryPolicy.ParseRetryAfter(null, now));
        }

        [Test]
        public async Task Telemetry_RecordsRetriesAndOutcomeWithoutPayloads()
        {
            var telemetry = new RecordingTelemetry();
            var transport = new FakeTransport()
                .EnqueueJson(503, "{\"error\":\"restarting\"}")
                .EnqueueJson(200, "{}");

            var options = TestHarness.Options(transport);
            options.Telemetry = telemetry;
            options.TokenStore = new InMemoryTokenStore(new StarhermitStoredSession(
                TestHarness.Jwt(new TestClock().UtcNow.AddMinutes(15)), "refresh", TestHarness.TestUserId));

            using var client = StarhermitClient.Create(options);
            await client.InitializeAsync();
            await client.Me.GetProfileAsync();

            Assert.AreEqual(1, telemetry.Events.Count);
            Assert.AreEqual("me.getProfile", telemetry.Events[0].OperationId);
            Assert.AreEqual(1, telemetry.Events[0].RetryCount);
            Assert.AreEqual(2, telemetry.Events[0].StatusFamily);
            Assert.AreEqual(StarhermitOperationOutcome.Success, telemetry.Events[0].Outcome);
        }
    }
}
