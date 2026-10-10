using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Starhermit.Json;

namespace Starhermit.Tests
{
    /// <summary>
    /// The operations a game client and a game's owner reach beyond play itself: queues, recovery,
    /// reports, diagnostics, webhooks, room discovery, owner content and server output. Each test pins
    /// the wire shape - route, credential, body - because that is what drifts silently.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class OperationSurfaceTests
    {
        private static readonly Guid GameId = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid OtherId = new Guid("bbbbbbbb-0000-0000-0000-000000000002");

        [Test]
        public async Task Queues_AreListed_AndMatchmakingCanNameASubset()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "[{\"key\":\"1v1\",\"teams\":2,\"teamSize\":1,\"players\":2},{\"key\":\"2v2\",\"teams\":2,\"teamSize\":2,\"players\":4}]")
                .EnqueueJson(200, "{\"ticketId\":\"" + OtherId + "\",\"status\":\"queued\",\"sessionId\":null,\"waitedSeconds\":4,\"searchEloBand\":150,\"maxWaitSeconds\":120}");
            using var client = await TestHarness.SignedInAsync(transport);
            var game = client.Games.ForSlug("chess");

            var queues = await game.GetQueuesAsync();
            var ticket = await game.EnqueueMatchmakingAsync(new[] { "1v1", "2v2" });

            Assert.AreEqual("/api/v1/games/chess/queues", transport.Requests[0].Path);
            Assert.AreEqual(2, queues[1].TeamSize);
            Assert.AreEqual("POST", transport.Last.Method);
            Assert.AreEqual("?queues=1v1&queues=2v2", transport.Last.Query);
            Assert.AreEqual(StarhermitMatchmakingStatuses.Queued, ticket.Status);
            Assert.AreEqual(150m, ticket.SearchEloBand);
            Assert.AreEqual(4, ticket.WaitedSeconds);
        }

        [Test]
        public async Task SessionRecovery_And_Leave_UseTheSessionRoutes()
        {
            var body = "{\"sessionId\":\"" + OtherId + "\",\"status\":\"active\",\"runtime\":\"container\",\"persistent\":true," +
                       "\"pausedAt\":null,\"pausedMillis\":1200,\"snapshotBytes\":2048,\"stateBudgetBytes\":65536," +
                       "\"lastSnapshotAt\":\"2026-08-20T12:00:00Z\",\"snapshotIntervalSeconds\":7.5,\"maxSnapshotPushHz\":2," +
                       "\"maxRestoreStalenessSeconds\":120,\"maxRestores\":3,\"restoreWindowMinutes\":10,\"restoreCount\":1," +
                       "\"retentionDays\":30,\"recoveryDataExpiresAt\":null," +
                       "\"transport\":{\"maxMessageBytes\":4096,\"maxRealtimeInputsPerSecond\":60,\"tickRateHz\":0.25,\"pacingRateHz\":20}}";
            var transport = new FakeTransport().EnqueueJson(200, body).Enqueue(_ => new FakeResponse(204));
            using var client = await TestHarness.SignedInAsync(transport);
            var game = client.Games.ForSlug("world");

            var recovery = await game.GetSessionRecoveryAsync(OtherId);
            await game.LeaveSessionAsync(OtherId);

            Assert.AreEqual($"/api/v1/games/world/sessions/{OtherId}/recovery", transport.Requests[0].Path);
            Assert.IsTrue(recovery.IsPersistent);
            Assert.AreEqual(7.5, recovery.SnapshotIntervalSeconds);
            Assert.AreEqual(0.25, recovery.Transport!.TickRateHz, "sub-Hz rates survive the trip");
            Assert.AreEqual($"/api/v1/games/world/sessions/{OtherId}/leave", transport.Last.Path);
            Assert.AreEqual("POST", transport.Last.Method);
        }

        [Test]
        public async Task FileReport_SendsTheReportWithBase64Attachments_UnderTheLaunchToken()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"token\":\"launch-token-xyz\",\"expiresInSeconds\":900}")
                .EnqueueJson(201, "{\"id\":\"" + OtherId + "\",\"kind\":\"crash\",\"status\":\"open\",\"createdAt\":\"2026-08-20T12:00:00Z\"}");
            using var client = await TestHarness.SignedInAsync(transport);
            var game = client.Games.ForSlug("chess");
            await game.AcquireLaunchTokenAsync();

            var report = new StarhermitPlayerReport(StarhermitReportKinds.Crash, "Froze on load")
            {
                Description = "Black screen after the logo.",
                BuildId = "b-42"
            }.Attach("player.log", "text/plain", Encoding.UTF8.GetBytes("hi"));
            var receipt = await game.WithLaunchToken().FileReportAsync(report);

            Assert.AreEqual("/api/v1/games/chess/reports", transport.Last.Path);
            Assert.AreEqual("launch-token-xyz", transport.Last.BearerToken);
            var sent = JsonParser.Parse(transport.Last.Body!);
            Assert.AreEqual("crash", sent["kind"].AsString());
            Assert.AreEqual("b-42", sent["buildId"].AsString());
            Assert.AreEqual("aGk=", sent["attachments"].Items.Single()["dataBase64"].AsString());
            Assert.IsTrue(sent["sessionId"].IsMissing, "unset members are not sent");
            Assert.AreEqual(OtherId, receipt.Id);
        }

        [Test]
        public async Task ReportRateLimit_CarriesRetryAfter()
        {
            var headers = new System.Collections.Generic.Dictionary<string, string> { ["Retry-After"] = "3600" };
            var transport = new FakeTransport().Always(_ => new FakeResponse(429, "{\"error\":\"You have filed 20 reports for this game today.\"}", headers));
            using var client = await TestHarness.SignedInAsync(transport);

            var error = Assert.ThrowsAsync<StarhermitRateLimitException>(() =>
                client.Games.ForSlug("chess").FileReportAsync(new StarhermitPlayerReport(StarhermitReportKinds.Bug, "x")));

            Assert.AreEqual(TimeSpan.FromHours(1), error!.RetryAfter);
        }

        [Test]
        public async Task LinkedAchievements_ReportPrivacyAsHiddenNotEmpty()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"game\":\"chess-classic\",\"hidden\":true,\"unlocked\":[]}");
            using var client = await TestHarness.SignedInAsync(transport);

            var linked = await client.Games.ForSlug("chess").GetLinkedAchievementsAsync("chess-classic");

            Assert.AreEqual("/api/v1/games/chess/linked-achievements/chess-classic", transport.Last.Path);
            Assert.IsTrue(linked.IsHidden);
            Assert.AreEqual(0, linked.Unlocked.Count);
        }

        [Test]
        public async Task OwnerOperationsOnTheGameRoute_UseTheAccountEvenFromALaunchScopedClient()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"slug\":\"chess\",\"runtime\":\"script\",\"enabled\":true,\"tickRateHz\":1," +
                                  "\"sessions\":{\"active\":3,\"finished\":10,\"liveConnections\":5,\"withLiveConnection\":2,\"oldestActiveSince\":null}," +
                                  "\"script\":{\"invocations\":100,\"averageMillis\":1.5,\"peakMillis\":9,\"lastInvokedAt\":null,\"cpuMillisBudget\":50,\"memoryBudgetBytes\":1000,\"maxStatements\":5000}," +
                                  "\"matchmaking\":{\"queued\":1,\"waitingLongestSeconds\":12,\"widestSearchBand\":200,\"maxWaitSeconds\":120}," +
                                  "\"webhooks\":{\"endpoints\":1,\"disabledEndpoints\":0,\"pending\":2,\"dead\":0}," +
                                  "\"writeBuffer\":{\"enabled\":true,\"pendingSessions\":1,\"pendingBytes\":10,\"maxPendingBytes\":100}," +
                                  "\"observedAt\":\"2026-08-20T12:00:00Z\"}")
                .EnqueueJson(201, "{\"id\":\"" + OtherId + "\",\"url\":\"https://hooks.example.com/sh\",\"events\":[\"session.finished\"]," +
                                  "\"enabled\":true,\"consecutiveFailures\":0,\"disabledReason\":null,\"createdAt\":\"2026-08-20T12:00:00Z\",\"secret\":\"whsec-shown-once\"}")
                .EnqueueJson(200, "{\"id\":\"" + OtherId + "\",\"url\":\"https://hooks.example.com/sh\",\"events\":[],\"enabled\":true}");
            using var client = await TestHarness.SignedInAsync(transport);
            var game = client.Games.ForSlug("chess").WithLaunchToken();

            var diagnostics = await game.GetDiagnosticsAsync();
            var endpoint = await game.CreateWebhookAsync("https://hooks.example.com/sh", new[] { "session.finished" });
            await game.ResumeWebhookAsync(OtherId);

            Assert.AreEqual(3, diagnostics.ActiveSessions);
            Assert.AreEqual(1.5, diagnostics.ScriptAverageMilliseconds);
            Assert.AreEqual(200m, diagnostics.MatchmakingWidestSearchBand);
            Assert.AreEqual("whsec-shown-once", endpoint.Secret);
            Assert.AreEqual("session.finished", JsonParser.Parse(transport.Requests[1].Body!)["events"].Items.Single().AsString());
            Assert.AreEqual($"/api/v1/games/chess/webhooks/{OtherId}/resume", transport.Last.Path);
            foreach (var request in transport.Requests)
                Assert.AreEqual(client.Session!.AccessToken, request.BearerToken, "owner tools authenticate as the account");
        }

        [Test]
        public async Task ConnectionTickets_CarryTheCredentialOfTheClientThatAsked()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"ticket\":\"account-ticket\",\"expiresIn\":30}")
                .EnqueueJson(200, "{\"token\":\"launch-token-xyz\",\"expiresInSeconds\":900}")
                .EnqueueJson(200, "{\"ticket\":\"game-ticket\",\"expiresIn\":30}");
            using var client = await TestHarness.SignedInAsync(transport);

            var accountTicket = await client.Auth.IssueConnectionTicketAsync();
            Assert.AreEqual(client.Session!.AccessToken, transport.Last.BearerToken);

            var game = client.Games.ForSlug("chess");
            await game.AcquireLaunchTokenAsync();
            var gameTicket = await game.WithLaunchToken().IssueConnectionTicketAsync();

            Assert.AreEqual("/api/v1/realtime/connection-tickets", transport.Last.Path);
            Assert.AreEqual("launch-token-xyz", transport.Last.BearerToken);
            Assert.AreEqual("account-ticket", accountTicket.Ticket);
            Assert.AreEqual("game-ticket", gameTicket.Ticket);
            Assert.AreEqual(30, gameTicket.ExpiresInSeconds);
            StringAssert.DoesNotContain("game-ticket", gameTicket.ToString());
        }

        [Test]
        public void Tickets_And_JoinCodes_AreRedactedByName()
        {
            Assert.IsTrue(StarhermitRedactor.IsSecretQueryParameter("ticket"));
            var text = StarhermitRedactor.RedactJson(JsonParser.Parse("{\"ticket\":\"t1\",\"joinCode\":\"ABCD\",\"name\":\"keep\"}")).ToJson();
            StringAssert.DoesNotContain("t1", text);
            StringAssert.DoesNotContain("ABCD", text);
            StringAssert.Contains("keep", text);
        }

        [Test]
        public async Task Rooms_CanBeBrowsedJoinedByCodeAndReconfigured()
        {
            var room = "{\"id\":\"" + OtherId + "\",\"gameSlug\":\"brawl\",\"status\":\"lobby\",\"name\":\"Fri night\",\"joinCode\":\"K7QX\"," +
                       "\"isVisible\":true,\"revision\":4,\"config\":{\"teamCount\":2,\"seatsPerTeam\":2,\"backfillAiPlayers\":0,\"joinInProgress\":true},\"participants\":[]}";
            var transport = new FakeTransport()
                .EnqueueJson(200, "[{\"id\":\"" + OtherId + "\",\"gameSlug\":\"brawl\",\"name\":\"Fri night\",\"hostUsername\":\"ada\",\"status\":\"lobby\"," +
                                  "\"players\":1,\"capacity\":4,\"freeSeats\":3,\"metadata\":{\"map\":\"dock\"},\"createdAt\":\"2026-08-20T12:00:00Z\"}]")
                .EnqueueJson(200, room)
                .EnqueueJson(200, room)
                .EnqueueJson(200, "{\"ticketId\":\"" + GameId + "\",\"status\":\"queued\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var listed = await client.RealtimeRooms.BrowseRoomsAsync("brawl", limit: 10, includeInProgress: true);
            StringAssert.Contains("gameSlug=brawl", transport.Last.Query);
            StringAssert.Contains("includeInProgress=true", transport.Last.Query);
            Assert.AreEqual(3, listed[0].FreeSeats);
            Assert.AreEqual("dock", listed[0].Metadata["map"].AsString());

            var joined = await client.RealtimeRooms.JoinByCodeAsync("k7 qx");
            Assert.AreEqual("k7 qx", JsonParser.Parse(transport.Last.Body!)["joinCode"].AsString(), "the server normalizes, not the SDK");
            Assert.AreEqual("K7QX", joined.JoinCode);
            Assert.AreEqual(0, joined.Config!.BackfillAiPlayers);
            Assert.IsTrue(joined.Config.JoinInProgress);

            await client.RealtimeRooms.UpdateRoomAsync(OtherId, new StarhermitRoomUpdate
            {
                IsVisible = false,
                ExpectedRevision = joined.Revision
            });
            Assert.AreEqual("PATCH", transport.Last.Method);
            var sent = JsonParser.Parse(transport.Last.Body!);
            Assert.IsFalse(sent["isVisible"].AsBoolean());
            Assert.AreEqual(4, sent["expectedRevision"].AsInt32());
            Assert.IsTrue(sent["name"].IsMissing, "an unset member is left alone");

            await client.RealtimeRooms.MatchmakeRoomAsync(OtherId, new[] { "2v2" });
            Assert.AreEqual($"/api/v1/realtime/rooms/{OtherId}/matchmake", transport.Last.Path);
            Assert.AreEqual("?queues=2v2", transport.Last.Query);
        }

        [Test]
        public async Task OwnerReports_PageReadAndMoveAlong()
        {
            var detail = "{\"id\":\"" + OtherId + "\",\"kind\":\"bug\",\"status\":\"acknowledged\",\"title\":\"t\",\"description\":\"d\"," +
                         "\"reporterUserId\":\"" + GameId + "\",\"sizeBytes\":12,\"attachments\":[{\"id\":\"" + GameId + "\",\"fileName\":\"a.log\",\"contentType\":\"text/plain\",\"sizeBytes\":2}]}";
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"items\":[{\"id\":\"" + OtherId + "\",\"kind\":\"bug\",\"status\":\"open\",\"title\":\"t\",\"reporterUserId\":\"" + GameId + "\"," +
                                  "\"attachmentCount\":1,\"sizeBytes\":12}],\"total\":1,\"page\":1,\"pageSize\":20}")
                .EnqueueJson(200, detail)
                .Enqueue(_ => new FakeResponse(200, "hi", new System.Collections.Generic.Dictionary<string, string> { ["Content-Type"] = "text/plain" }));
            using var client = await TestHarness.SignedInAsync(transport);
            var games = client.BrowserGames;

            var page = await games.GetReportsAsync(GameId, kind: StarhermitReportKinds.Bug, status: StarhermitReportStatuses.Open);
            StringAssert.Contains("kind=bug", transport.Last.Query);
            StringAssert.Contains("status=open", transport.Last.Query);
            Assert.AreEqual(1, page.Items.Single().AttachmentCount);

            var report = await games.SetReportStatusAsync(GameId, OtherId, StarhermitReportStatuses.Acknowledged);
            Assert.AreEqual("PATCH", transport.Last.Method);
            Assert.AreEqual("acknowledged", JsonParser.Parse(transport.Last.Body!)["status"].AsString());
            Assert.AreEqual("a.log", report.Attachments.Single().FileName);

            var file = await games.GetReportAttachmentAsync(GameId, OtherId, GameId);
            Assert.AreEqual($"/api/v1/me/github-games/{GameId}/reports/{OtherId}/attachments/{GameId}", transport.Last.Path);
            Assert.AreEqual("text/plain", file.ContentType);
            Assert.AreEqual(2, file.Length);
        }

        [Test]
        public async Task ContainerOutput_LogsAndCrashes()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"source\":\"live\",\"deploymentStatus\":\"running\",\"capturedAt\":\"2026-08-20T12:00:00Z\",\"truncated\":true,\"logs\":\"line\"}")
                .EnqueueJson(200, "{\"items\":[{\"id\":\"" + OtherId + "\",\"occurredAt\":\"2026-08-20T11:00:00Z\",\"kind\":\"exited\",\"exitCode\":137," +
                                  "\"detail\":\"OOM\",\"imageDigest\":\"sha256:abc\",\"restartCount\":2,\"affectedSessions\":4,\"logLength\":900,\"logsTruncated\":false}]," +
                                  "\"total\":1,\"page\":1,\"pageSize\":20}")
                .EnqueueJson(200, "{\"id\":\"" + OtherId + "\",\"kind\":\"exited\",\"exitCode\":137,\"detail\":\"OOM\",\"imageDigest\":\"sha256:abc\",\"logs\":\"panic\"}");
            using var client = await TestHarness.SignedInAsync(transport);
            var games = client.BrowserGames;

            var logs = await games.GetContainerLogsAsync(GameId, tail: 200);
            Assert.AreEqual("?tail=200", transport.Last.Query);
            Assert.IsTrue(logs.IsTruncated);

            var crashes = await games.GetContainerCrashesAsync(GameId);
            Assert.AreEqual(137, crashes.Items.Single().ExitCode);
            Assert.AreEqual(900, crashes.Items.Single().LogLength);
            Assert.IsNull(crashes.Items.Single().Logs, "a listing carries no output");

            var crash = await games.GetContainerCrashAsync(GameId, OtherId);
            Assert.AreEqual("panic", crash.Logs);
        }

        [Test]
        public async Task ContainerLogs_ForAScriptGame_IsAConflict()
        {
            var transport = new FakeTransport().Always(_ => new FakeResponse(409, "{\"error\":\"This game's backend is not a container.\"}"));
            using var client = await TestHarness.SignedInAsync(transport);

            Assert.ThrowsAsync<StarhermitConflictException>(() => client.BrowserGames.GetContainerLogsAsync(GameId));
        }

        [Test]
        public async Task OwnerContent_WritesOnlyWhatWasSet()
        {
            var transport = new FakeTransport()
                .EnqueueJson(201, "{\"id\":\"" + OtherId + "\",\"key\":\"first-win\",\"name\":\"First win\",\"description\":\"\",\"secret\":false,\"points\":10,\"origin\":\"owner\",\"unlocks\":0}")
                .EnqueueJson(200, "{\"id\":\"" + OtherId + "\",\"key\":\"weekly\",\"name\":\"Weekly\",\"scoreType\":\"points\",\"sortDirection\":\"desc\",\"isActive\":true," +
                                  "\"resetSchedule\":\"weekly\",\"gameDefinitionId\":\"" + GameId + "\",\"nextResetAt\":\"2026-08-24T00:00:00Z\"}")
                .EnqueueJson(200, "{\"userId\":\"" + OtherId + "\",\"elo\":1200}");
            using var client = await TestHarness.SignedInAsync(transport);
            var games = client.BrowserGames;

            var achievement = await games.CreateAchievementAsync(GameId, new StarhermitOwnedAchievementDraft { Key = "first-win", Name = "First win", Points = 10 });
            var created = JsonParser.Parse(transport.Last.Body!);
            Assert.AreEqual(10, created["points"].AsInt32());
            Assert.IsTrue(created["secret"].IsMissing);
            Assert.AreEqual("owner", achievement.Origin);

            var board = await games.UpdateLeaderboardAsync(GameId, OtherId, new StarhermitOwnedLeaderboardDraft { ResetSchedule = "weekly", MaxScore = 1000m });
            Assert.AreEqual("PUT", transport.Last.Method);
            Assert.AreEqual("{\"maxScore\":1000,\"resetSchedule\":\"weekly\"}", transport.Last.Body);
            Assert.AreEqual("weekly", board.Key);
            Assert.AreEqual(GameId, board.GameDefinitionId);
            Assert.IsNotNull(board.NextResetAt);

            var reset = await games.ResetPlayerEloAsync(GameId, OtherId);
            Assert.AreEqual("DELETE", transport.Last.Method);
            Assert.AreEqual($"/api/v1/me/github-games/{GameId}/players/{OtherId}/elo", transport.Last.Path);
            Assert.AreEqual(1200m, reset.Elo);
        }

        [Test]
        public async Task MovingAndRestoringAGame()
        {
            var game = "{\"id\":\"" + GameId + "\",\"repoUrl\":\"https://github.com/ada/brawl\",\"displayName\":\"Brawl\",\"serverRuntime\":\"container\"}";
            var transport = new FakeTransport()
                .EnqueueJson(200, game)
                .EnqueueJson(200, "[{\"id\":\"" + GameId + "\",\"displayName\":\"Brawl\",\"repoUrl\":\"\",\"removedAt\":\"2026-08-20T12:00:00Z\",\"restorable\":true,\"filesKept\":true}]")
                .EnqueueJson(200, game)
                .EnqueueJson(404, "{\"error\":\"No release notes.\"}");
            using var client = await TestHarness.SignedInAsync(transport);
            var games = client.BrowserGames;

            var moved = await games.ChangeUrlAsync(GameId, "https://github.com/ada/brawl");
            Assert.AreEqual("PUT", transport.Last.Method);
            Assert.AreEqual("{\"repoUrl\":\"https://github.com/ada/brawl\"}", transport.Last.Body);
            Assert.AreEqual("container", moved.ServerRuntime);

            var removed = await games.ListRemovedAsync();
            Assert.IsTrue(removed.Single().IsRestorable);

            await games.RestoreAsync(GameId);
            Assert.AreEqual($"/api/v1/me/github-games/{GameId}/restore", transport.Last.Path);

            Assert.IsNull(await games.GetReleaseNotesAsync(GameId), "no notes is a state, not a failure");
        }

        [Test]
        public async Task OwnerSessions_CanBeListedAndEnded()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "[{\"sessionId\":\"" + OtherId + "\",\"status\":\"active\",\"players\":[],\"pausedAt\":\"2026-08-20T12:00:00Z\"}]")
                .Enqueue(_ => new FakeResponse(204));
            using var client = await TestHarness.SignedInAsync(transport);

            var sessions = await client.BrowserGames.GetSessionsAsync(GameId);
            await client.BrowserGames.EndSessionAsync(GameId, OtherId);

            Assert.IsNotNull(sessions.Single().PausedAt);
            Assert.AreEqual("DELETE", transport.Last.Method);
            Assert.AreEqual($"/api/v1/me/github-games/{GameId}/sessions/{OtherId}", transport.Last.Path);
        }

        [Test]
        public async Task AnonymousReads_SendNoCredential()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"providers\":[{\"provider\":\"github\",\"displayName\":\"GitHub\",\"authorizeUrl\":\"/api/v1/auth/oauth/github/authorize\",\"linksExistingAccountsByEmail\":true}]}")
                .EnqueueJson(200, "{\"hash\":\"abc123\",\"text\":\"Be kind.\"}");
            using var client = await TestHarness.SignedInAsync(transport);

            var providers = await client.Auth.GetOAuthProvidersAsync();
            Assert.IsNull(transport.Last.BearerToken);
            var terms = await client.Me.GetTermsAsync();
            Assert.IsNull(transport.Last.BearerToken);

            Assert.AreEqual("github", providers.Single().Provider);
            Assert.IsTrue(providers.Single().LinksExistingAccountsByEmail);
            Assert.AreEqual("abc123", terms.Hash);
            Assert.AreEqual("/api/v1/terms", transport.Last.Path);
        }

        [Test]
        public async Task RevokingTheCurrentKey_AndServerMute()
        {
            var transport = new FakeTransport()
                .EnqueueJson(200, "{\"revoked\":[{\"id\":\"" + OtherId + "\",\"keyType\":\"ed25519\"}],\"sessionsEnded\":2}")
                .Enqueue(_ => new FakeResponse(204));
            using var client = await TestHarness.SignedInAsync(transport);

            var revoked = await client.Me.RevokeCurrentPublicKeyAsync();
            Assert.AreEqual("DELETE", transport.Last.Method);
            Assert.AreEqual("/api/v1/me/public-keys/current", transport.Last.Path);
            Assert.AreEqual(2, revoked.SessionsEnded);
            Assert.AreEqual(OtherId, revoked.RevokedKeys.Single().Id);

            await client.Voice.SetServerMuteAsync(GameId, OtherId, true);
            Assert.AreEqual($"/api/v1/voice/rooms/{GameId}/participants/{OtherId}/server-mute", transport.Last.Path);
            Assert.AreEqual("{\"muted\":true}", transport.Last.Body);
        }
    }
}
