using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Starhermit.Json;

namespace Starhermit.Tests
{
    /// <summary>
    /// The six socket protocols, frame by frame. These pin the wire formats the deployment actually
    /// speaks - a change on either side should break a test here rather than a game in the field.
    /// </summary>
    [TestFixture]
    [Timeout(20000)]
    public class ProtocolTests
    {
        [Test]
        public async Task Chat_MapsEveryDocumentedEvent()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var chat = client.CreateChatConnection();

            StarhermitMessage? received = null;
            StarhermitMessage? edited = null;
            StarhermitMessage? deleted = null;
            StarhermitConversation? renamed = null;
            Guid readConversation = Guid.Empty;
            Guid removedFrom = Guid.Empty, removedUser = Guid.Empty;
            StarhermitChatInvite? invite = null;
            StarhermitInviteNotification? gameInvite = null;
            string? unknownType = null;

            chat.MessageReceived += m => received = m;
            chat.MessageUpdated += m => edited = m;
            chat.MessageDeleted += m => deleted = m;
            chat.ConversationRenamed += c => renamed = c;
            chat.ConversationRead += id => readConversation = id;
            chat.ParticipantRemoved += (conversation, user) => { removedFrom = conversation; removedUser = user; };
            chat.ChatInviteReceived += i => invite = i;
            chat.GameInviteReceived += n => gameInvite = n;
            chat.UnknownEventReceived += (type, _) => unknownType = type;

            await chat.ConnectAsync();

            var conversationId = Guid.NewGuid();
            var messageId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            sockets.Last.PushText("{\"type\":\"new_message\",\"payload\":{\"id\":\"" + messageId + "\",\"conversationId\":\"" + conversationId + "\",\"content\":\"hi\",\"kind\":\"text\"}}");
            sockets.Last.PushText("{\"type\":\"message_updated\",\"payload\":{\"id\":\"" + messageId + "\",\"content\":\"edited\"}}");
            sockets.Last.PushText("{\"type\":\"message_deleted\",\"payload\":{\"id\":\"" + messageId + "\",\"isDeleted\":true}}");
            sockets.Last.PushText("{\"type\":\"conversation_renamed\",\"payload\":{\"id\":\"" + conversationId + "\",\"name\":\"new name\"}}");
            sockets.Last.PushText("{\"type\":\"conversation_read\",\"payload\":{\"conversationId\":\"" + conversationId + "\"}}");
            sockets.Last.PushText("{\"type\":\"participant_removed\",\"payload\":{\"conversationId\":\"" + conversationId + "\",\"userId\":\"" + userId + "\"}}");
            sockets.Last.PushText("{\"type\":\"chat_invite\",\"payload\":{\"id\":\"" + Guid.NewGuid() + "\",\"conversationId\":\"" + conversationId + "\",\"status\":\"pending\"}}");
            sockets.Last.PushText("{\"type\":\"game_invite\",\"payload\":{\"inviteId\":\"" + Guid.NewGuid() + "\",\"kind\":\"session\",\"gameSlug\":\"chess\",\"acceptPath\":\"/api/v1/games/chess/invites/x/accept\"}}");
            sockets.Last.PushText("{\"type\":\"something_new\",\"payload\":{\"a\":1}}");

            await TestHarness.WaitForAsync(() => unknownType != null, "every frame to be handled");

            Assert.AreEqual("hi", received!.Content);
            Assert.AreEqual("edited", edited!.Content);
            Assert.IsTrue(deleted!.IsDeleted);
            Assert.AreEqual("new name", renamed!.Name);
            Assert.AreEqual(conversationId, readConversation);
            Assert.AreEqual(conversationId, removedFrom);
            Assert.AreEqual(userId, removedUser);
            Assert.AreEqual("pending", invite!.Status);
            Assert.AreEqual("chess", gameInvite!.GameSlug);
            Assert.AreEqual("something_new", unknownType, "an event this SDK does not know is surfaced, not dropped");
        }

        [Test]
        public void Chat_DeduplicatesBySeverIdWithoutInventingOne()
        {
            var deduplicator = new StarhermitMessageDeduplicator();
            var id = Guid.NewGuid();

            Assert.IsTrue(deduplicator.TryAdd(id), "first sighting is new");
            Assert.IsFalse(deduplicator.TryAdd(id), "the same server id seen again is a duplicate");
            Assert.IsTrue(deduplicator.TryAdd(Guid.NewGuid()));
        }

        [Test]
        public void Chat_DeduplicatorForgetsOldestFirstSoMemoryIsBounded()
        {
            var deduplicator = new StarhermitMessageDeduplicator(capacity: 2);
            var first = Guid.NewGuid();

            deduplicator.TryAdd(first);
            deduplicator.TryAdd(Guid.NewGuid());
            deduplicator.TryAdd(Guid.NewGuid());

            Assert.AreEqual(2, deduplicator.Count);
            Assert.IsTrue(deduplicator.TryAdd(first), "the oldest id was forgotten to bound memory");
        }

        [Test]
        public async Task Voice_SplitsTheSenderStampFromTheAudio()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var voice = client.CreateVoiceConnection(Guid.NewGuid());

            Guid speaker = Guid.Empty;
            byte[]? audio = null;
            voice.AudioReceived += (id, bytes) => { speaker = id; audio = bytes; };

            await voice.ConnectAsync();

            var sender = Guid.NewGuid();
            var frame = new byte[16 + 3];
            sender.ToByteArray().CopyTo(frame, 0);
            frame[16] = 7;
            frame[17] = 8;
            frame[18] = 9;
            sockets.Last.PushBinary(frame);

            await TestHarness.WaitForAsync(() => audio != null, "the audio frame to arrive");
            Assert.AreEqual(sender, speaker, "identity comes from the platform's stamp, never from client input");
            Assert.AreEqual(new byte[] { 7, 8, 9 }, audio);
        }

        [Test]
        public async Task Voice_IgnoresAFrameTooShortToCarryASender()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var voice = client.CreateVoiceConnection(Guid.NewGuid());

            var delivered = 0;
            voice.AudioReceived += (_, _) => delivered++;
            await voice.ConnectAsync();

            sockets.Last.PushBinary(new byte[8]);
            sockets.Last.PushText("{\"type\":\"speaking\",\"userId\":\"" + Guid.NewGuid() + "\",\"speaking\":true}");

            await TestHarness.WaitForAsync(() => sockets.Last.Sent.Count >= 0, "frames to be processed");
            await Task.Delay(50);
            Assert.AreEqual(0, delivered, "a frame that cannot be attributed is dropped rather than guessed at");
        }

        [Test]
        public async Task Voice_ControlFramesUseTheDocumentedShape()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var voice = client.CreateVoiceConnection(Guid.NewGuid());
            await voice.ConnectAsync();

            await voice.SetMutedAsync(true);
            await voice.SetSpeakingAsync(false);

            await TestHarness.WaitForAsync(() => sockets.Last.Sent.Count == 2, "both control frames to be sent");
            var mute = JsonParser.Parse(sockets.Last.Sent[0].Text);
            var speaking = JsonParser.Parse(sockets.Last.Sent[1].Text);

            Assert.AreEqual("mute", mute["type"].AsString());
            Assert.IsTrue(mute["muted"].AsBoolean());
            Assert.AreEqual("speaking", speaking["type"].AsString());
            Assert.IsFalse(speaking["speaking"].AsBoolean());
        }

        [Test]
        public async Task Voice_PcmHelperSendsLittleEndianSamples()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var voice = client.CreateVoiceConnection(Guid.NewGuid());
            await voice.ConnectAsync();

            await voice.SendPcmAsync(new ArraySegment<short>(new short[] { 1, -1 }));

            await TestHarness.WaitForAsync(() => sockets.Last.Sent.Count == 1, "the PCM frame to be sent");
            var payload = sockets.Last.Sent[0].Payload;
            Assert.AreEqual(4, payload.Length);
            Assert.IsFalse(sockets.Last.Sent[0].IsText, "audio travels as binary");
        }

        [Test]
        public async Task Game_WrapsCommandsInTheCmdEnvelope()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var game = client.CreateGameConnection(Guid.NewGuid());
            await game.ConnectAsync();

            await game.SendCommandAsync(writer =>
            {
                writer.Write("type", "move");
                writer.Write("from", "e2");
                writer.Write("to", "e4");
            });

            await TestHarness.WaitForAsync(() => sockets.Last.Sent.Count == 1, "the command to be sent");
            var sent = JsonParser.Parse(sockets.Last.Sent[0].Text);

            Assert.AreEqual("cmd", sent["type"].AsString());
            Assert.AreEqual("move", sent["data"]["type"].AsString());
            Assert.AreEqual("e4", sent["data"]["to"].AsString());
            Assert.IsTrue(sockets.Last.Sent[0].IsText, "the game socket accepts text frames only");
        }

        [Test]
        public async Task Game_RealtimeInputCarriesTheRealtimeFlag()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var game = client.CreateGameConnection(Guid.NewGuid());
            await game.ConnectAsync();

            await game.SendRealtimeInputAsync(writer => writer.Write("axis", 0.5d));

            await TestHarness.WaitForAsync(() => sockets.Last.Sent.Count == 1, "the input to be sent");
            var data = JsonParser.Parse(sockets.Last.Sent[0].Text)["data"];

            Assert.AreEqual("input", data["type"].AsString());
            Assert.IsTrue(data["realtime"].AsBoolean(), "the server buffers and paces these rather than applying each one");
        }

        [Test]
        public async Task Game_MapsFrameAchievementPresenceAndError()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var game = client.CreateGameConnection(Guid.NewGuid());

            JsonValue? frame = null;
            JsonValue? achievement = null;
            Guid presenceUser = Guid.Empty;
            var online = false;
            string? error = null;

            game.FrameReceived += f => frame = f;
            game.AchievementUnlocked += a => achievement = a;
            game.PresenceChanged += (id, isOnline) => { presenceUser = id; online = isOnline; };
            game.ErrorReceived += e => error = e;

            await game.ConnectAsync();

            var userId = Guid.NewGuid();
            sockets.Last.PushText("{\"type\":\"game\",\"data\":{\"board\":\"rnbq\"}}");
            sockets.Last.PushText("{\"type\":\"achievement\",\"data\":{\"key\":\"first_win\"}}");
            sockets.Last.PushText("{\"type\":\"presence\",\"userId\":\"" + userId + "\",\"online\":true}");
            sockets.Last.PushText("{\"type\":\"error\",\"error\":\"Malformed JSON.\"}");

            await TestHarness.WaitForAsync(() => error != null, "every frame to be handled");

            Assert.AreEqual("rnbq", frame!["board"].AsString());
            Assert.AreEqual("first_win", achievement!["key"].AsString());
            Assert.AreEqual(userId, presenceUser);
            Assert.IsTrue(online);
            Assert.AreEqual("Malformed JSON.", error);
        }

        [Test]
        public async Task Realtime_SplitsTheParticipantStampFromThePayload()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var room = client.CreateRealtimeConnection(Guid.NewGuid(), "chess");

            Guid sender = Guid.Empty;
            byte[]? payload = null;
            room.BinaryReceived += (id, bytes) => { sender = id; payload = bytes; };

            await room.ConnectAsync();

            var participant = Guid.NewGuid();
            var frame = new byte[16 + 2];
            participant.ToByteArray().CopyTo(frame, 0);
            frame[16] = 0xAB;
            frame[17] = 0xCD;
            sockets.Last.PushBinary(frame);

            await TestHarness.WaitForAsync(() => payload != null, "the room frame to arrive");
            Assert.AreEqual(participant, sender);
            Assert.AreEqual(new byte[] { 0xAB, 0xCD }, payload, "payload bytes are preserved exactly");
        }

        [Test]
        public async Task Realtime_ControlFramesCarryOnlyTheTypeTheSenderChose()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var room = client.CreateRealtimeConnection(Guid.NewGuid(), "chess");
            await room.ConnectAsync();

            await room.SendReadyAsync(true);
            await room.SendChatAsync(writer => writer.Write("text", "hello"));

            await TestHarness.WaitForAsync(() => sockets.Last.Sent.Count == 2, "both control frames to be sent");
            var ready = JsonParser.Parse(sockets.Last.Sent[0].Text);
            var chat = JsonParser.Parse(sockets.Last.Sent[1].Text);

            Assert.AreEqual("ready", ready["type"].AsString());
            Assert.IsTrue(ready["ready"].AsBoolean());
            Assert.AreEqual("chat", chat["type"].AsString());
            Assert.IsTrue(chat["from"].IsMissing, "the server tags the sender; a client-supplied 'from' is discarded");
        }

        [Test]
        public async Task Relay_ForwardsPayloadsVerbatim()
        {
            var sockets = new FakeSocketFactory();
            var transport = new FakeTransport().Always(_ => new FakeResponse(200, "{\"id\":\"" + Guid.NewGuid() + "\"}"));
            using var client = await TestHarness.SignedInAsync(transport, socketFactory: sockets);
            var relay = client.CreateRelayConnection(Guid.NewGuid(), Guid.NewGuid());

            byte[]? received = null;
            relay.PayloadReceived += bytes => received = bytes;

            await relay.ConnectAsync();
            sockets.Last.PushBinary(new byte[] { 9, 9, 9, 0, 1 });

            await TestHarness.WaitForAsync(() => received != null, "the relayed payload to arrive");
            Assert.AreEqual(new byte[] { 9, 9, 9, 0, 1 }, received,
                "the relay adds no prefix of its own, so nothing may be stripped");
        }

        [Test]
        public async Task Upload_StreamsChunksThenCompletesAndReturnsTheResult()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var upload = client.CreateBundleUploadConnection(Guid.NewGuid());

            await upload.ConnectAsync();
            sockets.Last.PushText("{\"type\":\"ready\",\"mode\":\"bundle\",\"limitBytes\":1048576,\"heartbeatSeconds\":10}");

            var archive = new MemoryStream(new byte[5000]);
            var task = upload.UploadAsync(archive, chunkSize: 2048);

            await TestHarness.WaitForAsync(
                () => sockets.Last.Sent.Count == 4,
                "three binary chunks and one complete frame");

            Assert.IsFalse(sockets.Last.Sent[0].IsText);
            Assert.AreEqual(2048, sockets.Last.Sent[0].Payload.Length);
            Assert.AreEqual(904, sockets.Last.Sent[2].Payload.Length);
            Assert.IsTrue(sockets.Last.Sent[3].IsText);
            Assert.AreEqual("complete", JsonParser.Parse(sockets.Last.Sent[3].Text)["type"].AsString());

            sockets.Last.PushText("{\"type\":\"result\",\"status\":200,\"clientPublished\":true,\"bytesReceived\":5000}");
            var outcome = await task;

            Assert.IsTrue(outcome.ClientPublished);
            Assert.AreEqual(5000, outcome.BytesReceived);
        }

        [Test]
        public async Task Upload_RefusesAnArchiveLargerThanTheServersAllowance()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var upload = client.CreateBundleUploadConnection(Guid.NewGuid());

            await upload.ConnectAsync();
            sockets.Last.PushText("{\"type\":\"ready\",\"mode\":\"bundle\",\"limitBytes\":100,\"heartbeatSeconds\":10}");

            var error = Assert.ThrowsAsync<StarhermitProtocolException>(
                () => upload.UploadAsync(new MemoryStream(new byte[500])));

            StringAssert.Contains("allowance", error!.Message);
            var abort = JsonParser.Parse(sockets.Last.Sent[0].Text);
            Assert.AreEqual("abort", abort["type"].AsString(),
                "the server is told to discard rather than left to time out");
        }

        [Test]
        public async Task Upload_ServerErrorSurfacesAsATypedApiFailure()
        {
            var sockets = new FakeSocketFactory();
            using var client = await TestHarness.SignedInAsync(new FakeTransport(), socketFactory: sockets);
            var upload = client.CreateBundleUploadConnection(Guid.NewGuid());

            await upload.ConnectAsync();
            sockets.Last.PushText("{\"type\":\"error\",\"status\":413,\"error\":\"too big\",\"limitBytes\":100}");

            var error = Assert.CatchAsync<StarhermitApiException>(() => upload.WaitForReadyAsync());
            Assert.AreEqual(413, error!.Status);
            Assert.AreEqual("too big", error.ServerMessage);
        }
    }
}
