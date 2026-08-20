using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Starhermit.Platform;

namespace Starhermit.Tests
{
    /// <summary>
    /// Contract checks against a real deployment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rest of the suite pins wire formats through recorded shapes, which catches SDK regressions
    /// but cannot notice the day the deployment renames a field. These tests read the live API, so a
    /// contract drift shows up as a failure rather than as a bug report from a player.
    /// </para>
    /// <para>
    /// They are skipped unless <c>STARHERMIT_TEST_BASE_URL</c> names a deployment, so an ordinary
    /// build - and CI without a backend - stays hermetic. Point it at a development or staging
    /// deployment:
    /// </para>
    /// <code>
    /// STARHERMIT_TEST_BASE_URL=http://starhermit.test:5050/api/v1/ dotnet test
    /// </code>
    /// <para>
    /// Only the anonymous surface is exercised. Authenticated routes need a session this suite has no
    /// safe way to obtain: it will not mint one from a signing secret, because a test that forges
    /// credentials stops testing the thing it claims to test.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Live")]
    [Timeout(60000)]
    public class LiveContractTests
    {
        private StarhermitClient? _client;

        [SetUp]
        public void SetUp()
        {
            var baseUrl = Environment.GetEnvironmentVariable("STARHERMIT_TEST_BASE_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Assert.Ignore("Set STARHERMIT_TEST_BASE_URL to run the live contract tests.");
                return;
            }

            var uri = new Uri(baseUrl!, UriKind.Absolute);
            _client = StarhermitClient.Create(new StarhermitOptions
            {
                ApiBaseUri = uri,
                Transport = new HttpClientTransport(),
                CallbackDispatcher = ImmediateCallbackDispatcher.Instance,

                // A development deployment is served over plain HTTP; production would refuse this.
                AllowInsecureTransport = !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase),
                RequestTimeout = TimeSpan.FromSeconds(20)
            });
        }

        [TearDown]
        public void TearDown() => _client?.Dispose();

        [Test]
        public async Task ServerTime_ParsesAndProducesAUsableClockOffset()
        {
            var reading = await _client!.Time.SynchronizeAsync();

            Assert.Greater(reading.ServerTime.Year, 2020, "the server reported a real timestamp");
            Assert.IsNotNull(reading.ReportedSkewMilliseconds, "the deployment echoes the skew it measured");
            Assert.IsNotNull(_client.ServerClock.Age, "the reading updated the client's clock");
            Assert.Less(reading.RoundTrip, TimeSpan.FromSeconds(20));
        }

        [Test]
        public async Task Catalog_PagesWithTheServersOwnMetadata()
        {
            var page = await _client!.Software.GetTitlesAsync(page: 1, pageSize: 2);

            Assert.AreEqual(1, page.Page);
            Assert.AreEqual(2, page.PageSize);
            Assert.GreaterOrEqual(page.TotalCount, 0);
            Assert.LessOrEqual(page.Count, 2);

            foreach (var title in page.Items)
            {
                Assert.AreNotEqual(Guid.Empty, title.Id, "every title carries an id");
                Assert.IsNotNull(title.Name);
            }
        }

        [Test]
        public async Task Leaderboards_MapEveryDefinitionField()
        {
            var boards = await _client!.Leaderboards.GetLeaderboardsAsync();

            foreach (var board in boards)
            {
                Assert.AreNotEqual(Guid.Empty, board.Id);
                Assert.IsNotEmpty(board.Name);
                Assert.IsNotEmpty(board.ScoreType);
                Assert.IsNotEmpty(board.SortDirection);
                Assert.IsNotEmpty(board.Scope);
                Assert.IsNotNull(board.CreatedAt);

                // Anything the deployment added since this SDK version is still readable, which is the
                // property that lets a shipped game survive an API release.
                Assert.IsTrue(board.RawJson.IsObject);
            }
        }

        [Test]
        public void AuthenticatedRoute_WithoutASession_RefusesLocally()
        {
            // The pipeline refuses before sending: there is no credential to send, and asking the
            // deployment to say so would be a wasted round trip.
            Assert.ThrowsAsync<StarhermitAuthenticationException>(() => _client!.Me.GetProfileAsync());
        }

        [Test]
        public async Task UnknownEndpoint_MapsToATypedNotFound()
        {
            var request = StarhermitRawClient.GetRequest("no/such/endpoint")
                .WithCredential(StarhermitCredential.None);

            try
            {
                await _client!.Raw.SendForJsonAsync(request);
                Assert.Fail("the deployment answered a route that should not exist");
            }
            catch (StarhermitApiException failure)
            {
                Assert.AreEqual(404, failure.Status);
            }
        }
    }
}
