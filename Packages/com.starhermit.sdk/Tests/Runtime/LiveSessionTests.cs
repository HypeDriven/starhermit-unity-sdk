using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Starhermit.Json;
using Starhermit.Platform;

namespace Starhermit.Tests
{
    /// <summary>
    /// Contract checks against a real deployment, signed in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The account is made the way a player makes one: a key is registered, the deployment emails a
    /// link, the link is redeemed and the terms accepted. That needs the deployment's mail, so these
    /// run only when <c>STARHERMIT_TEST_MAILBOX</c> names the directory it lands in as well as
    /// <c>STARHERMIT_TEST_BASE_URL</c> - see <see cref="LiveDeployment"/>, and <c>tools/live-test.sh</c>,
    /// which stands up a throwaway backend with a mail sink and runs them.
    /// </para>
    /// <para>
    /// The tests share one account and one client, run in turn: the deployment allows sixty sign-in
    /// requests a minute per address, and only the sign-in test spends any.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Live")]
    [Timeout(120000)]
    public class LiveSessionTests
    {
        private readonly RecordingSocketFactory _sockets = new RecordingSocketFactory();
        private LiveDeployment? _deployment;
        private LiveAccount? _player;

        [OneTimeSetUp]
        public async Task RegisterAnAccount()
        {
            _deployment = LiveDeployment.FromEnvironment(requireMailbox: true);
            _player = await LiveAccount.RegisterAsync(_deployment, _sockets);
        }

        [OneTimeTearDown]
        public void SignOut() => _player?.Dispose();

        /// <summary>Handshakes the shared client has made since <paramref name="before"/> of them.</summary>
        private IReadOnlyList<Uri> HandshakesSince(int before) => _sockets.Handshakes.Skip(before).ToArray();

        [Test]
        public async Task PublicKeySignIn_TheChallengeTheSdkSignsVerifies()
        {
            using var client = _deployment!.CreateClient();

            // Several, because the bytes signed depend on the challenge: a nonce carrying a '+' is
            // escaped in the server's form, and about three challenges in ten carry one. One sign-in
            // passing says little; eight passing says the canonical form is right.
            for (var attempt = 1; attempt <= 8; attempt++)
            {
                var session = await client.Auth.SignInWithPublicKeyAsync(_player!.Signer);
                Assert.AreEqual(_player.UserId, session.UserId, "sign-in " + attempt);
            }

            var profile = await client.Me.GetProfileAsync();
            Assert.AreEqual(_player!.UserId, profile.Id);
        }

        [Test]
        public async Task AccountSocket_EveryHandshakePresentsAFreshSingleUseTicket()
        {
            var client = _player!.Client;
            var before = _sockets.Handshakes.Count;
            using var chat = client.CreateChatConnection();

            await chat.ConnectAsync();
            Assert.AreEqual(StarhermitConnectionState.Connected, chat.State);
            await chat.CloseAsync();

            await chat.ConnectAsync();
            Assert.AreEqual(StarhermitConnectionState.Connected, chat.State, "a second handshake needs, and gets, a ticket of its own");
            await chat.CloseAsync();

            var handshakes = HandshakesSince(before);
            Assert.AreEqual(2, handshakes.Count);
            foreach (var handshake in handshakes)
            {
                StringAssert.Contains("ticket=", handshake.Query);
                StringAssert.DoesNotContain("access_token", handshake.Query, "the session token stays out of the URL");
                StringAssert.DoesNotContain(client.Session!.AccessToken, handshake.ToString());
            }

            Assert.AreNotEqual(handshakes[0].Query, handshakes[1].Query);

            // The deployment spends a ticket on the handshake that presents it, which is why the SDK
            // never reuses one: presented again, it is refused.
            using var replay = new ClientWebSocketAdapter();
            Assert.ThrowsAsync<StarhermitTransportException>(() =>
                replay.ConnectAsync(handshakes[0], Array.Empty<KeyValuePair<string, string>>(), default));
        }

        [Test]
        public async Task LaunchToken_ItsTicketOpensItsOwnGamesRoomAndNoOther()
        {
            var client = _player!.Client;
            var before = _sockets.Handshakes.Count;

            // A game to scope a launch token to. Uploading one is itself a ticketed handshake.
            var archive = Tarball.Gzip(new KeyValuePair<string, byte[]>(
                "client/index.html",
                Encoding.UTF8.GetBytes("<!doctype html><title>SDK live</title><p>SDK live test game</p>")));
            var upload = client.CreateGameUploadConnection("SDK live " + Guid.NewGuid().ToString("N").Substring(0, 8), "index.html");
            StarhermitUploadOutcome? outcome = null;
            try
            {
                outcome = await upload.UploadAsync(new MemoryStream(archive));
            }
            catch (StarhermitApiException refused) when (refused.Status == 503)
            {
                Assert.Ignore("The deployment has no game hosting (Games:HostRoot), so there is no game to scope a launch token to.");
            }
            finally
            {
                upload.Dispose();
            }

            var slug = outcome?.Game?.GameSlug;
            Assert.IsNotNull(slug, "an uploaded game is addressed by a slug from creation");
            StringAssert.Contains("ticket=", HandshakesSince(before)[0].Query);

            await client.Games.ForSlug(slug!).AcquireLaunchTokenAsync();

            var own = await client.RealtimeRooms.CreateRoomAsync(slug, new StarhermitRoomSettings { TeamCount = 1, SeatsPerTeam = 2 });
            try
            {
                using var room = client.CreateRealtimeConnection(own.Id, slug, useLaunchToken: true);
                await room.ConnectAsync();
                Assert.AreEqual(StarhermitConnectionState.Connected, room.State, "the launch token's ticket opens its own game's room");
                var handshake = HandshakesSince(before).Last();
                StringAssert.Contains("ticket=", handshake.Query);
                StringAssert.DoesNotContain(client.Games.ForSlug(slug!).LaunchToken!.Value.Token, handshake.ToString());
                await room.CloseAsync();
            }
            finally
            {
                await client.RealtimeRooms.LeaveRoomAsync(own.Id);
            }

            // The ticket copied the launch token's game_scope rather than the account's reach: the same
            // player, holding a seat, is refused a room of another game through it.
            var elsewhere = await client.RealtimeRooms.CreateRoomAsync(slug + "-elsewhere", new StarhermitRoomSettings { TeamCount = 1, SeatsPerTeam = 2 });
            try
            {
                using var fenced = client.CreateRealtimeConnection(elsewhere.Id, slug, useLaunchToken: true);
                Assert.CatchAsync<StarhermitException>(() => fenced.ConnectAsync());

                using var unfenced = client.CreateRealtimeConnection(elsewhere.Id, slug + "-elsewhere");
                await unfenced.ConnectAsync();
                Assert.AreEqual(StarhermitConnectionState.Connected, unfenced.State, "the account's own ticket reaches it");
                await unfenced.CloseAsync();
            }
            finally
            {
                await client.RealtimeRooms.LeaveRoomAsync(elsewhere.Id);
            }
        }

        [Test]
        public async Task Rooms_KeepTheirSettings_DeclineAnInvite_AndQuickJoinFindsThemByFilter()
        {
            var host = _player!.Client;
            using var guestAccount = await LiveAccount.RegisterAsync(_deployment!);
            var guest = guestAccount.Client;

            var tag = Guid.NewGuid().ToString("N").Substring(0, 10);
            var slug = "sdk-live-" + tag;
            var room = await host.RealtimeRooms.CreateRoomAsync(slug, new StarhermitRoomSettings
            {
                TeamCount = 2,
                SeatsPerTeam = 2,
                Name = "SDK live " + tag,
                IsVisible = true,
                JoinInProgress = true,
                BackfillAiPlayers = 0,
                Metadata = JsonParser.Parse("{\"map\":\"dock-" + tag + "\",\"mode\":\"casual\"}")
            });

            try
            {
                Assert.AreEqual("SDK live " + tag, room.Name);
                Assert.IsTrue(room.IsVisible);
                Assert.IsTrue(room.Config!.JoinInProgress);
                Assert.AreEqual(0, room.Config.BackfillAiPlayers, "0 is a cap the deployment kept, not an unset value");
                Assert.AreEqual(2, room.Config.SeatsPerTeam);
                Assert.AreEqual("dock-" + tag, room.Config.Metadata["map"].AsString());
                Assert.IsNotEmpty(room.JoinCode, "every room is created with a join code");

                // An invitation goes to a friend, and declining one answers 204 - no invitation to read
                // back. Answering it twice is refused, which is how the decline is seen to have landed.
                await host.Friends.SendRequestAsync(guestAccount.UserId);
                var request = (await guest.Friends.GetRequestsAsync()).Single(r => r.SenderUserId == _player.UserId);
                await guest.Friends.AcceptRequestAsync(request.Id);
                var invite = await host.RealtimeRooms.CreateInviteAsync(room.Id, guestAccount.UserId);
                await guest.RealtimeRooms.DeclineInviteAsync(invite.Id);
                Assert.IsFalse((await guest.RealtimeRooms.GetInvitesAsync()).Any(i => i.Id == invite.Id), "a declined invitation is no longer pending");
                Assert.ThrowsAsync<StarhermitConflictException>(() => guest.RealtimeRooms.DeclineInviteAsync(invite.Id));

                await host.RealtimeRooms.OpenRoomAsync(room.Id);

                var listed = await guest.RealtimeRooms.BrowseRoomsAsync(slug);
                Assert.IsTrue(listed.Any(r => r.Id == room.Id), "a visible room is listed");

                var miss = Assert.ThrowsAsync<StarhermitNotFoundException>(() => guest.RealtimeRooms.QuickJoinAsync(
                    slug,
                    new StarhermitQuickJoinFilter { Metadata = JsonParser.Parse("{\"map\":\"nowhere-" + tag + "\"}") }));
                Assert.IsNotNull(miss!.ServerMessage);

                var joined = await guest.RealtimeRooms.QuickJoinAsync(slug, new StarhermitQuickJoinFilter
                {
                    TeamCount = 2,
                    SeatsPerTeam = 2,
                    Metadata = JsonParser.Parse("{\"map\":\"dock-" + tag + "\"}")
                });
                Assert.AreEqual(room.Id, joined.Id, "the metadata filter matched as a subset");
                Assert.AreEqual(2, joined.Participants.Count(p => p.LeftAt == null));

                await guest.RealtimeRooms.LeaveRoomAsync(room.Id);
            }
            finally
            {
                await host.RealtimeRooms.LeaveRoomAsync(room.Id);
            }
        }

        [Test]
        public async Task CloudSave_DownloadIfChanged_TransfersOnlyWhatTheCallerLacks()
        {
            var client = _player!.Client;
            var key = "sdk-live-" + Guid.NewGuid().ToString("N");
            var first = new byte[] { 1, 2, 3, 4 };
            var second = new byte[] { 5, 6, 7, 8, 9 };

            var stored = await client.CloudSaves.UploadAsync(key, first, StarhermitSaveCondition.IfNoSaveExists);
            Assert.IsNotNull(stored.ETag);

            var unchanged = await client.CloudSaves.DownloadIfChangedAsync(key, stored.ETag);
            Assert.IsTrue(unchanged.NotModified, "the deployment answered 304 for the version already held");
            Assert.IsNull(unchanged.Archive);
            Assert.AreEqual(stored.ETag, unchanged.ETag);

            var replaced = await client.CloudSaves.UploadAsync(key, second, StarhermitSaveCondition.IfMatch(stored.ETag!));
            var changed = await client.CloudSaves.DownloadIfChangedAsync(key, stored.ETag);
            Assert.IsFalse(changed.NotModified);
            CollectionAssert.AreEqual(second, changed.Archive);
            Assert.AreEqual(replaced.ETag, changed.ETag);

            Assert.ThrowsAsync<StarhermitPreconditionFailedException>(
                () => client.CloudSaves.UploadAsync(key, first, StarhermitSaveCondition.IfMatch(stored.ETag!)),
                "a write over a version that has been replaced is refused, not applied");
        }
    }
}
