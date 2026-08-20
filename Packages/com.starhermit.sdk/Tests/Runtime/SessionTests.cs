using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Starhermit.Tests
{
    /// <summary>
    /// Refresh coordination, rotation persistence, and how the SDK tells "your session is over" from
    /// "the network is down" - the difference between asking a player to sign in again and not.
    /// </summary>
    [TestFixture]
    public class SessionTests
    {
        [Test]
        public async Task ExpiredToken_IsRefreshedBeforeTheRequestIsSent()
        {
            var clock = new TestClock();
            var store = new InMemoryTokenStore(new StarhermitStoredSession(
                TestHarness.Jwt(clock.UtcNow.AddSeconds(-1)),
                "refresh-1",
                TestHarness.TestUserId));

            var transport = new FakeTransport()
                .EnqueueJson(200, Tokens(clock.UtcNow.AddMinutes(15), "access-2", "refresh-2"))
                .EnqueueJson(200, "{\"id\":\"" + TestHarness.TestUserId + "\",\"username\":\"ada\"}");

            using var client = StarhermitClient.Create(TestHarness.Options(clock: clock, tokenStore: store, transport: transport));
            await client.InitializeAsync();

            await client.Me.GetProfileAsync();

            Assert.AreEqual(2, transport.Requests.Count);
            Assert.AreEqual("/api/v1/auth/refresh", transport.Requests[0].Path);
            Assert.AreEqual("access-2", transport.Requests[1].BearerToken, "the request carries the refreshed token");
        }

        [Test]
        public async Task ConcurrentCallers_ShareOneRefresh()
        {
            var clock = new TestClock();
            // The session starts valid so initialisation does not refresh; the clock is moved past
            // expiry afterwards, which is what makes all eight calls discover a spent token at once.
            var store = new InMemoryTokenStore(new StarhermitStoredSession(
                TestHarness.Jwt(clock.UtcNow.AddMinutes(15)),
                "refresh-1",
                TestHarness.TestUserId));

            var refreshGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshCount = 0;
            var transport = new FakeTransport().AlwaysAsync(async request =>
            {
                if (request.Path.EndsWith("/auth/refresh", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref refreshCount);
                    await refreshGate.Task.ConfigureAwait(false);
                    return new FakeResponse(200, Tokens(clock.UtcNow.AddMinutes(15), "access-2", "refresh-2"));
                }

                return new FakeResponse(200, "{}");
            });

            using var client = StarhermitClient.Create(TestHarness.Options(clock: clock, tokenStore: store, transport: transport));
            await client.InitializeAsync();
            clock.Advance(TimeSpan.FromMinutes(20));

            var calls = new List<Task>();
            for (var i = 0; i < 8; i++) calls.Add(Task.Run(() => client.Me.GetProfileAsync()));
            await Task.Delay(50);
            refreshGate.SetResult(true);
            await Task.WhenAll(calls);

            Assert.AreEqual(1, refreshCount,
                "a rotating refresh token means a second concurrent exchange would revoke the family");
        }

        [Test]
        public async Task RotatedPair_IsStoredBeforeWaitersResume()
        {
            var clock = new TestClock();
            var store = new InMemoryTokenStore(new StarhermitStoredSession(
                TestHarness.Jwt(clock.UtcNow.AddSeconds(-1)),
                "refresh-1",
                TestHarness.TestUserId));

            var transport = new FakeTransport()
                .EnqueueJson(200, Tokens(clock.UtcNow.AddMinutes(15), "access-2", "refresh-2"))
                .EnqueueJson(200, "{}");

            using var client = StarhermitClient.Create(TestHarness.Options(clock: clock, tokenStore: store, transport: transport));
            await client.InitializeAsync();

            await client.Me.GetProfileAsync();

            var stored = await store.LoadAsync();
            Assert.AreEqual("access-2", stored!.AccessToken);
            Assert.AreEqual("refresh-2", stored.RefreshToken,
                "the store must already hold the rotated token when the waiting call resumes");
        }

        [Test]
        public async Task Unauthorized_TriggersOneRefreshAndOneReplay()
        {
            var clock = new TestClock();
            var attempts = 0;
            var transport = new FakeTransport().Always(request =>
            {
                if (request.Path.EndsWith("/auth/refresh", StringComparison.Ordinal))
                    return new FakeResponse(200, Tokens(clock.UtcNow.AddMinutes(15), "access-2", "refresh-2"));

                attempts++;
                return new FakeResponse(401, "{\"error\":\"expired\"}");
            });

            using var client = await TestHarness.SignedInAsync(transport, clock);

            Assert.ThrowsAsync<StarhermitAuthenticationException>(() => client.Me.GetProfileAsync());
            Assert.AreEqual(2, attempts, "the request is replayed exactly once after a successful refresh");
        }

        [Test]
        public async Task DefinitiveRefusal_EndsTheSessionAndRaisesSessionExpiredOnce()
        {
            var expiries = 0;
            var transport = new FakeTransport().Always(_ => new FakeResponse(401, "{\"error\":\"revoked\"}"));
            using var client = await TestHarness.SignedInAsync(transport);
            client.Auth.SessionExpired += () => expiries++;

            Assert.ThrowsAsync<StarhermitAuthenticationException>(() => client.Me.GetProfileAsync());
            Assert.ThrowsAsync<StarhermitAuthenticationException>(() => client.Me.GetProfileAsync());

            Assert.IsFalse(client.IsAuthenticated);
            Assert.AreEqual(1, expiries, "one ended session raises one event, however many calls notice it");
        }

        [Test]
        public async Task TransientRefreshFailure_KeepsTheSession()
        {
            var clock = new TestClock();
            var store = new InMemoryTokenStore(new StarhermitStoredSession(
                TestHarness.Jwt(clock.UtcNow.AddSeconds(-1)),
                "refresh-1",
                TestHarness.TestUserId));

            var transport = new FakeTransport().Always(request =>
                request.Path.EndsWith("/auth/refresh", StringComparison.Ordinal)
                    ? FakeResponse.TransportFailure()
                    : new FakeResponse(200, "{}"));

            using var client = StarhermitClient.Create(TestHarness.Options(clock: clock, tokenStore: store, transport: transport));
            await client.InitializeAsync();

            try
            {
                await client.Me.GetProfileAsync();
            }
            catch (StarhermitTransportException)
            {
                // The refresh could not be completed; that is the network's fault, not the session's.
            }

            Assert.IsTrue(client.IsAuthenticated, "a tunnel going down must not sign the player out");
            Assert.IsNotNull(await store.LoadAsync());
        }

        [Test]
        public async Task SignOut_ClearsTheSessionEvenWhenTheServerCannotBeReached()
        {
            var transport = new FakeTransport().Always(_ => FakeResponse.TransportFailure());
            using var client = await TestHarness.SignedInAsync(transport);

            await client.Auth.SignOutAsync();

            Assert.IsFalse(client.IsAuthenticated);
        }

        [Test]
        public async Task SignOut_RevokesTheRefreshTokenServerSide()
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(200));
            using var client = await TestHarness.SignedInAsync(transport);
            var refreshToken = client.Session!.RefreshToken;

            await client.Auth.SignOutAsync();

            Assert.AreEqual("/api/v1/auth/logout", transport.Last.Path);
            StringAssert.Contains(refreshToken, transport.Last.Body);
        }

        [Test]
        public void Session_ReadsItsOwnClaimsWithoutVerifyingThem()
        {
            var expiry = new DateTimeOffset(2026, 8, 20, 12, 15, 0, TimeSpan.Zero);
            var session = new StarhermitSession(TestHarness.Jwt(expiry), "refresh");

            Assert.AreEqual(TestHarness.TestUserId, session.UserId);
            Assert.AreEqual(expiry, session.AccessTokenExpiresAt);
            Assert.AreEqual("oauth", session.AuthenticationMethod);
            CollectionAssert.Contains(session.Permissions, "user.profile.read");
        }

        [Test]
        public void Session_WithUnreadableToken_HasNoClaimsRatherThanThrowing()
        {
            var session = new StarhermitSession("not-a-jwt", "refresh");

            Assert.IsNull(session.UserId);
            Assert.IsNull(session.AccessTokenExpiresAt);
            Assert.AreEqual(0, session.Permissions.Count);
        }

        private static string Tokens(DateTimeOffset expiry, string accessLabel, string refreshToken) =>
            "{\"accessToken\":\"" + accessLabel + "\",\"refreshToken\":\"" + refreshToken + "\"}";
    }
}
