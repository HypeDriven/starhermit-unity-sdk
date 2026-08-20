using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Starhermit.Json;

namespace Starhermit.Tests
{
    /// <summary>
    /// Credentials must not reach a log, a diagnostic, an exception message or a telemetry event -
    /// including the ones the SDK has never seen before, which is why redaction is by name and not by
    /// value.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class RedactionTests
    {
        [Test]
        public void RedactUri_RemovesTheBrowserHandshakeToken()
        {
            var uri = new Uri("wss://api.test/ws/v1/chat?roomId=42&access_token=super-secret-token");

            var safe = StarhermitRedactor.RedactUri(uri);

            StringAssert.DoesNotContain("super-secret-token", safe);
            StringAssert.Contains("roomId=42", safe);
            StringAssert.Contains("access_token=***", safe);
        }

        [Test]
        public void RedactUri_DropsFragmentsEntirely()
        {
            var safe = StarhermitRedactor.RedactUri(
                new Uri("https://game.test/callback?state=abc#access_token=leaked&refresh_token=alsoleaked"));

            StringAssert.DoesNotContain("leaked", safe);
        }

        [Test]
        public void RedactUri_RemovesSignedStorageParameters()
        {
            var safe = StarhermitRedactor.RedactUri(
                new Uri("https://cdn.test/build.zip?X-Amz-Signature=deadbeef&X-Amz-Credential=abc&file=build.zip"));

            StringAssert.DoesNotContain("deadbeef", safe);
            StringAssert.Contains("file=build.zip", safe);
        }

        [Test]
        public void RedactJson_ReplacesCredentialMembersAtEveryDepth()
        {
            var json = JsonParser.Parse(
                "{\"accessToken\":\"a\",\"nested\":{\"refreshToken\":\"b\",\"keep\":\"visible\"}," +
                "\"list\":[{\"signedUrl\":\"c\"}],\"invokeKey\":\"d\"}");

            var text = StarhermitRedactor.RedactJson(json).ToJson();

            StringAssert.DoesNotContain("\"a\"", text);
            StringAssert.DoesNotContain("\"b\"", text);
            StringAssert.DoesNotContain("\"c\"", text);
            StringAssert.DoesNotContain("\"d\"", text);
            StringAssert.Contains("visible", text, "redaction removes credentials, not the shape");
        }

        [Test]
        public void RedactBody_TruncatesToTheDiagnosticCap()
        {
            var long_body = "{\"note\":\"" + new string('x', 5000) + "\"}";

            var redacted = StarhermitRedactor.RedactBody(long_body, 100);

            Assert.LessOrEqual(redacted.Length, 160);
            StringAssert.Contains("more characters", redacted);
        }

        [Test]
        public void RedactHeader_HidesAuthorizationButNotOrdinaryHeaders()
        {
            Assert.AreEqual("***", StarhermitRedactor.RedactHeader("Authorization", "Bearer secret"));
            Assert.AreEqual("***", StarhermitRedactor.RedactHeader("X-Starhermit-Invoke-Key", "invoke-secret"));
            Assert.AreEqual("application/json", StarhermitRedactor.RedactHeader("Accept", "application/json"));
        }

        [Test]
        public async Task DebugLogging_NeverContainsTheAccessToken()
        {
            var logger = new RecordingLogger();
            var transport = new FakeTransport().Always(_ => new FakeResponse(500, "{\"error\":\"boom\"}"));
            using var client = await TestHarness.SignedInAsync(transport, logger: logger);
            var token = client.Session!.AccessToken;

            Assert.ThrowsAsync<StarhermitServerException>(() => client.Me.GetProfileAsync());

            Assert.Greater(logger.Messages.Count, 0, "the failure was logged at all");
            Assert.IsTrue(logger.NeverLogged(token), "the bearer token must never appear in a log");
            Assert.IsTrue(logger.NeverLogged("refresh-token-1"));
        }

        [Test]
        public async Task ApiException_CarriesARedactedBody()
        {
            var body = "{\"error\":\"nope\",\"accessToken\":\"should-not-appear\"}";
            var transport = new FakeTransport().Always(_ => new FakeResponse(400, body));
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitBadRequestException>(() => client.Me.GetProfileAsync());

            StringAssert.DoesNotContain("should-not-appear", error!.RawBody);
            StringAssert.Contains("nope", error.RawBody);
        }

        [Test]
        public async Task ApiException_HeadersAreRedacted()
        {
            var headers = new Dictionary<string, string> { ["Set-Cookie"] = "session=secret-cookie" };
            var transport = new FakeTransport().Always(_ => new FakeResponse(400, "{\"error\":\"nope\"}", headers));
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitBadRequestException>(() => client.Me.GetProfileAsync());

            Assert.AreEqual("***", error!.Headers["Set-Cookie"]);
        }

        [Test]
        public void SessionToString_ContainsNoTokens()
        {
            var session = new StarhermitSession("access-token-value", "refresh-token-value");

            var text = session.ToString();

            StringAssert.DoesNotContain("access-token-value", text);
            StringAssert.DoesNotContain("refresh-token-value", text);
        }

        [Test]
        public void StoredSessionToString_ContainsNoTokens()
        {
            var stored = new StarhermitStoredSession("access", "refresh", Guid.NewGuid());

            StringAssert.DoesNotContain("access", stored.ToString());
            StringAssert.DoesNotContain("refresh", stored.ToString());
        }

        [Test]
        public void ScopedTokenToString_ContainsNoToken()
        {
            var token = new StarhermitScopedToken("launch-secret", DateTimeOffset.UtcNow);

            StringAssert.DoesNotContain("launch-secret", token.ToString());
        }

        [Test]
        public async Task Telemetry_NeverReceivesAUrlOrABody()
        {
            var telemetry = new RecordingTelemetry();
            var options = TestHarness.Options(new FakeTransport().Always(_ => new FakeResponse(200, "{}")));
            options.Telemetry = telemetry;
            options.TokenStore = new InMemoryTokenStore(new StarhermitStoredSession(
                TestHarness.Jwt(new TestClock().UtcNow.AddMinutes(15)), "refresh", TestHarness.TestUserId));

            using var client = StarhermitClient.Create(options);
            await client.InitializeAsync();
            await client.Me.GetProfileAsync();

            var recorded = telemetry.Events[0];
            StringAssert.DoesNotContain("http", recorded.Name);
            StringAssert.DoesNotContain("api.test", recorded.OperationId);
        }
    }
}
