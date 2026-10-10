using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Starhermit.Tests
{
    /// <summary>
    /// The machinery every socket shares: handshake, credentials, ordering, backpressure, reconnection
    /// and the rule that one bad frame or one throwing handler must not end a healthy connection.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class SocketConnectionTests
    {
        [Test]
        public async Task Connect_AddressesTheProtocolPathWithItsQuery()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var roomId = Guid.NewGuid();

            var voice = client.CreateVoiceConnection(roomId);
            await voice.ConnectAsync();

            var uri = sockets.Last.ConnectedUri!;
            Assert.AreEqual("wss", uri.Scheme, "the socket scheme is derived from the API address");
            Assert.AreEqual("/ws/v1/voice", uri.AbsolutePath);
            StringAssert.Contains("roomId=" + roomId.ToString("D"), uri.Query);
        }

        [Test]
        public async Task Connect_PresentsATicketBoughtWithTheSessionAndNoToken()
        {
            var sockets = new FakeSocketFactory();
            var transport = new FakeTransport();
            using var client = await TestHarness.SignedInAsync(transport, socketFactory: sockets);

            var chat = client.CreateChatConnection();
            await chat.ConnectAsync();

            var query = sockets.Last.ConnectedUri!.Query;
            Assert.AreEqual(1, transport.TicketRequests.Count, "one ticket per handshake");
            Assert.AreEqual(client.Session!.AccessToken, transport.TicketRequests[0].BearerToken, "the ticket is bought with the session");
            StringAssert.Contains("ticket=ticket-1", query);
            StringAssert.DoesNotContain("access_token", query, "the token stays out of the URL");
            StringAssert.DoesNotContain(client.Session.AccessToken, sockets.Last.ConnectedUri.ToString());
            Assert.AreEqual(0, sockets.Last.ConnectHeaders.Count, "the ticket is the handshake's only credential");
        }

        [Test]
        public async Task Reconnect_FetchesAFreshTicketForEveryHandshake()
        {
            var sockets = new FakeSocketFactory();
            var transport = new FakeTransport().Always(_ => new FakeResponse(200, "{\"id\":\"" + Guid.NewGuid() + "\"}"));
            using var client = await TestHarness.SignedInAsync(transport, socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());
            await relay.ConnectAsync();

            sockets.Last.PushClose(StarhermitCloseCodes.Abnormal, "dropped");

            await TestHarness.WaitForAsync(() => sockets.Created.Count == 2, "a second socket to be opened");
            StringAssert.Contains("ticket=ticket-1", sockets.Created[0].ConnectedUri!.Query);
            StringAssert.Contains("ticket=ticket-2", sockets.Created[1].ConnectedUri!.Query,
                "a ticket is spent by the handshake that presents it, so a reconnect needs its own");
            Assert.AreEqual(2, transport.TicketRequests.Count);
        }

        [Test]
        public async Task DeploymentWithoutTickets_FallsBackToTheToken()
        {
            var sockets = new FakeSocketFactory();
            var transport = new FakeTransport().WithoutConnectionTickets();
            using var client = await TestHarness.SignedInAsync(transport, socketFactory: sockets);

            var chat = client.CreateChatConnection();
            await chat.ConnectAsync();

            Assert.AreEqual(StarhermitConnectionState.Connected, chat.State);
            var authorization = sockets.Last.ConnectHeaders[0];
            Assert.AreEqual("Authorization", authorization.Key);
            Assert.AreEqual("Bearer " + client.Session!.AccessToken, authorization.Value);
            StringAssert.Contains("access_token=", sockets.Last.ConnectedUri!.Query,
                "browsers cannot set a handshake header, so an older deployment reads the token from the query");
            StringAssert.DoesNotContain("ticket=", sockets.Last.ConnectedUri.Query);
        }

        [Test]
        public async Task TicketRefusal_IsReportedRatherThanFallingBackToTheToken()
        {
            var sockets = new FakeSocketFactory();
            var transport = new FakeTransport();
            var issue = transport.ConnectionTickets;
            transport.ConnectionTickets = _ => new FakeResponse(403, "{\"error\":\"This token is scoped to the game 'other'.\"}");
            using var client = await TestHarness.SignedInAsync(transport, socketFactory: sockets);
            var chat = client.CreateChatConnection();

            Assert.ThrowsAsync<StarhermitAuthorizationException>(() => chat.ConnectAsync());
            Assert.AreEqual(0, sockets.Created.Count, "only a deployment without tickets earns the token in the URL");
            Assert.AreEqual(StarhermitConnectionState.Faulted, chat.State);

            // A failed ticket fetch must not leave the connection reading as Connecting, where every
            // later ConnectAsync would return without connecting.
            transport.ConnectionTickets = issue;
            await chat.ConnectAsync();
            Assert.AreEqual(StarhermitConnectionState.Connected, chat.State);
        }

        [Test]
        public async Task LaunchScopedSocket_BuysItsTicketWithTheLaunchToken()
        {
            var sockets = new FakeSocketFactory();
            var transport = new FakeTransport().EnqueueJson(200, "{\"token\":\"launch-token-xyz\",\"expiresInSeconds\":3600}");
            using var client = await TestHarness.SignedInAsync(transport, socketFactory: sockets);
            await client.Games.ForSlug("chess").AcquireLaunchTokenAsync();

            var room = client.CreateRealtimeConnection(Guid.NewGuid(), "chess", useLaunchToken: true);
            await room.ConnectAsync();

            Assert.AreEqual("launch-token-xyz", transport.TicketRequests.Single().BearerToken,
                "a ticket copies the claims of the token that bought it, so it must be the launch token");
            Assert.IsNull(transport.TicketRequests.Single().Header(StarhermitHeaders.GameSlug), "the slug header never leaves the process");
            StringAssert.Contains("ticket=ticket-1", sockets.Last.ConnectedUri!.Query);
            StringAssert.DoesNotContain("launch-token-xyz", sockets.Last.ConnectedUri.ToString());
        }

        [Test]
        public async Task Tickets_AreNeverLogged()
        {
            var sockets = new FakeSocketFactory();
            var logger = new RecordingLogger();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets, logger: logger);

            var chat = client.CreateChatConnection();
            await chat.ConnectAsync();
            await chat.CloseAsync();

            Assert.IsNotEmpty(logger.Messages, "the connection logged its lifecycle at Debug");
            Assert.IsTrue(logger.NeverLogged("ticket-1"));
            Assert.IsTrue(logger.NeverLogged(client.Session!.AccessToken));
        }

        [Test]
        public async Task Sends_ArriveInTheOrderTheyWereQueued()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());
            await relay.ConnectAsync();

            await relay.SendAsync(new byte[] { 1 });
            await relay.SendAsync(new byte[] { 2 });
            await relay.SendAsync(new byte[] { 3 });

            await TestHarness.WaitForAsync(() => sockets.Last.Sent.Count == 3, "three frames to be sent");
            Assert.AreEqual(1, sockets.Last.Sent[0].Payload[0]);
            Assert.AreEqual(2, sockets.Last.Sent[1].Payload[0]);
            Assert.AreEqual(3, sockets.Last.Sent[2].Payload[0]);
        }

        [Test]
        public async Task OversizeMessage_IsRefusedBeforeItIsQueued()
        {
            var sockets = new FakeSocketFactory();
            var options = TestHarness.Options(new FakeTransport(), sockets);
            options.MaxOutgoingMessageBytes = 64;
            options.TokenStore = new InMemoryTokenStore(new StarhermitStoredSession(
                TestHarness.Jwt(new TestClock().UtcNow.AddMinutes(15)), "refresh", TestHarness.TestUserId));

            using var client = StarhermitClient.Create(options);
            await client.InitializeAsync();
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());
            await relay.ConnectAsync();

            var error = Assert.ThrowsAsync<StarhermitProtocolException>(() => relay.SendAsync(new byte[128]));
            StringAssert.Contains("outgoing limit", error!.Message);
        }

        [Test]
        public async Task SendingWhileDisconnected_ReportsItRatherThanQueueingForever()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());

            Assert.ThrowsAsync<StarhermitProtocolException>(() => relay.SendAsync(new byte[] { 1 }));
        }

        [Test]
        public async Task AbnormalClose_Reconnects()
        {
            var sockets = new FakeSocketFactory();
            var transport = new FakeTransport().Always(_ => new FakeResponse(200, "{\"id\":\"" + Guid.NewGuid() + "\"}"));
            using var client = await TestHarness.SignedInAsync(transport, socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());
            await relay.ConnectAsync();

            sockets.Last.PushClose(StarhermitCloseCodes.Abnormal, "dropped");

            await TestHarness.WaitForAsync(() => sockets.Created.Count == 2, "a second socket to be opened");
            await TestHarness.WaitForAsync(() => relay.State == StarhermitConnectionState.Connected, "the reconnect to settle");
        }

        [Test]
        public async Task PolicyClose_StopsReconnecting()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());
            await relay.ConnectAsync();

            sockets.Last.PushClose(StarhermitCloseCodes.PolicyViolation, "rate limit exceeded");

            await TestHarness.WaitForAsync(() => relay.State == StarhermitConnectionState.Faulted, "the connection to fault");
            Assert.AreEqual(1, sockets.Created.Count, "a policy refusal does not get better by reconnecting into it");
        }

        [Test]
        public async Task Reconnect_RefetchesRoomStateBeforeReportingHealthy()
        {
            var sockets = new FakeSocketFactory();
            var roomId = Guid.NewGuid();
            var transport = new FakeTransport().Always(_ => new FakeResponse(
                200,
                "{\"id\":\"" + roomId + "\",\"gameSlug\":\"chess\",\"status\":\"lobby\",\"participants\":[]}"));

            using var client = await TestHarness.SignedInAsync(transport, socketFactory: sockets);
            var room = client.CreateRealtimeConnection(roomId, "chess");
            await room.ConnectAsync();

            sockets.Last.PushClose(StarhermitCloseCodes.Abnormal, "dropped");

            await TestHarness.WaitForAsync(() => room.Room != null, "the room to be refetched after reconnecting");
            Assert.AreEqual(roomId, room.Room!.Id);
        }

        [Test]
        public async Task ConcurrentConnects_OpenExactlyOneSocket()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());

            var attempts = new List<Task>();
            for (var i = 0; i < 8; i++) attempts.Add(Task.Run(() => relay.ConnectAsync()));
            await Task.WhenAll(attempts);

            Assert.AreEqual(1, sockets.Created.Count, "a second socket would leak the first");
            Assert.AreEqual(StarhermitConnectionState.Connected, relay.State);
        }

        [Test]
        public async Task Close_SendsANormalCloseAndStopsReconnecting()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());
            await relay.ConnectAsync();

            await relay.CloseAsync();

            Assert.AreEqual(StarhermitCloseCodes.Normal, sockets.Last.SentCloseStatus);
            Assert.AreEqual(StarhermitConnectionState.Disconnected, relay.State);
            Assert.IsFalse(relay.AutoReconnect);
        }

        [Test]
        public async Task AThrowingHandler_DoesNotStopTheReceiveLoop()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());

            var delivered = 0;
            relay.PayloadReceived += _ =>
            {
                delivered++;
                throw new InvalidOperationException("a game handler threw");
            };

            await relay.ConnectAsync();
            sockets.Last.PushBinary(new byte[] { 1 });
            sockets.Last.PushBinary(new byte[] { 2 });

            await TestHarness.WaitForAsync(() => delivered == 2, "both frames to be delivered");
            Assert.AreEqual(StarhermitConnectionState.Connected, relay.State);
        }

        [Test]
        public async Task MalformedFrame_IsSkippedRatherThanKillingTheConnection()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var chat = client.CreateChatConnection();

            var messages = 0;
            chat.MessageReceived += _ => messages++;

            await chat.ConnectAsync();
            sockets.Last.PushText("this is not json at all");
            sockets.Last.PushText("{\"type\":\"new_message\",\"payload\":{\"id\":\"" + Guid.NewGuid() + "\"}}");

            await TestHarness.WaitForAsync(() => messages == 1, "the valid frame to be delivered");
            Assert.AreEqual(StarhermitConnectionState.Connected, chat.State);
        }

        [Test]
        public async Task Diagnostics_ReportTheConnectionsState()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var chat = client.CreateChatConnection();
            await chat.ConnectAsync();

            var snapshot = client.GetDiagnostics();

            Assert.AreEqual(1, snapshot.Connections.Count);
            Assert.AreEqual("chat", snapshot.Connections[0].Name);
            Assert.AreEqual(StarhermitConnectionState.Connected, snapshot.Connections[0].State);
            Assert.IsTrue(snapshot.HasSession);
            Assert.IsNotNull(snapshot.AccessTokenExpiresAt);
        }

        [Test]
        public async Task DisposingTheClient_ClosesItsConnections()
        {
            var sockets = new FakeSocketFactory();
            var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var chat = client.CreateChatConnection();
            await chat.ConnectAsync();

            client.Dispose();

            Assert.AreEqual(StarhermitConnectionState.Disconnected, chat.State);
            Assert.AreEqual(StarhermitConnectionState.Disconnected, sockets.Last.State);
        }

        [Test]
        public async Task GameSocket_WithoutALaunchToken_ReportsTheMissingCredential()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var game = client.CreateGameConnection(Guid.NewGuid(), "chess", useLaunchToken: true);

            Assert.ThrowsAsync<StarhermitFeatureUnavailableException>(() => game.ConnectAsync());
        }

        [Test]
        public async Task RefusedHandshake_SurfacesAsATransportFailure()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());
            sockets.NextConnectFailure = new StarhermitTransportException("connection refused");

            Assert.ThrowsAsync<StarhermitTransportException>(() => relay.ConnectAsync());
            Assert.AreEqual(StarhermitConnectionState.Faulted, relay.State);
        }
    }
}
