using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// The realtime-room socket: binary room traffic and JSON control frames.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Binary payloads arrive as <c>[16-byte sender participant id][payload]</c>; the server stamps the
    /// prefix, and routing follows the room's topology - a host's frame reaches everyone, a guest's
    /// reaches the host. Payload bytes are preserved exactly.
    /// </para>
    /// <para>
    /// Control frames are tagged server-side with the sender's participant id, and a <c>from</c> a
    /// client tries to set is discarded. Guests may send <c>chat</c> and <c>ready</c>; only the host
    /// may send <c>event</c>. Sending anything else closes the connection with a policy violation.
    /// </para>
    /// </remarks>
    public sealed class StarhermitRealtimeConnection : StarhermitConnection
    {
        private readonly Guid _roomId;
        private readonly string? _gameSlug;
        private readonly bool _useLaunchToken;

        internal StarhermitRealtimeConnection(StarhermitClient client, Guid roomId, string? gameSlug, bool useLaunchToken)
            : base(client, "realtime")
        {
            _roomId = roomId;
            _gameSlug = gameSlug;
            _useLaunchToken = useLaunchToken;
        }

        /// <summary>The room this connection is attached to.</summary>
        public Guid RoomId => _roomId;

        /// <summary>The room as it stood at the last connect or reconnect.</summary>
        public StarhermitRoom? Room { get; private set; }

        /// <inheritdoc />
        protected override string Path => "realtime";

        /// <inheritdoc />
        protected override StarhermitCredential Credential =>
            _useLaunchToken ? StarhermitCredential.Launch : StarhermitCredential.Account;

        /// <inheritdoc />
        protected override string? GameSlug => _gameSlug;

        /// <inheritdoc />
        protected override void BuildQuery(IList<KeyValuePair<string, string>> query) =>
            query.Add(new KeyValuePair<string, string>("roomId", _roomId.ToString("D")));

        /// <summary>Raised for each binary frame, with the participant the server attributed it to.</summary>
        public event Action<Guid, byte[]>? BinaryReceived;

        /// <summary>Raised for each control frame, with its type and the frame itself.</summary>
        public event Action<string, JsonValue>? ControlReceived;

        /// <summary>Raised when a participant connects or disconnects.</summary>
        public event Action<Guid, bool>? PresenceChanged;

        /// <summary>Raised when the server sends the room roster.</summary>
        public event Action<JsonValue>? RosterReceived;

        /// <summary>Sends a binary frame into the room.</summary>
        /// <param name="payload">The bytes to send, preserved exactly.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the frame is sent.</returns>
        public Task SendAsync(byte[] payload, CancellationToken cancellationToken = default) =>
            SendBinaryAsync(payload, cancellationToken);

        /// <summary>Sends a chat control frame.</summary>
        /// <param name="writeMembers">Writes the frame's members, excluding <c>type</c> and <c>from</c>.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the frame is sent.</returns>
        public Task SendChatAsync(Action<JsonWriter> writeMembers, CancellationToken cancellationToken = default) =>
            SendControlAsync("chat", writeMembers, cancellationToken);

        /// <summary>Sends a ready control frame.</summary>
        /// <param name="ready">Whether the local player is ready.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the frame is sent.</returns>
        public Task SendReadyAsync(bool ready, CancellationToken cancellationToken = default) =>
            SendControlAsync("ready", writer => writer.Write("ready", ready), cancellationToken);

        /// <summary>Sends an event control frame. Host only; a guest is closed for policy violation.</summary>
        /// <param name="writeMembers">Writes the frame's members, excluding <c>type</c> and <c>from</c>.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the frame is sent.</returns>
        public Task SendEventAsync(Action<JsonWriter> writeMembers, CancellationToken cancellationToken = default) =>
            SendControlAsync("event", writeMembers, cancellationToken);

        private Task SendControlAsync(string type, Action<JsonWriter>? writeMembers, CancellationToken cancellationToken)
        {
            var json = JsonWriter.SerializeObject(writer =>
            {
                writer.Write("type", type);
                writeMembers?.Invoke(writer);
            });

            return SendTextAsync(json, cancellationToken);
        }

        /// <inheritdoc />
        protected override void HandleBinary(byte[] payload)
        {
            if (payload.Length < 16) return;

            var idBytes = new byte[16];
            Buffer.BlockCopy(payload, 0, idBytes, 0, 16);
            var participantId = new Guid(idBytes);

            var body = new byte[payload.Length - 16];
            Buffer.BlockCopy(payload, 16, body, 0, body.Length);

            Raise(() => BinaryReceived?.Invoke(participantId, body));
        }

        /// <inheritdoc />
        protected override void HandleText(string text)
        {
            if (!JsonParser.TryParse(text, out var frame) || !frame.IsObject) return;
            var type = frame["type"].AsStringOrNull();
            if (type == null) return;

            switch (type)
            {
                case "presence":
                {
                    var userId = frame["userId"].AsGuidOrNull() ?? Guid.Empty;
                    var online = frame["online"].AsBooleanOrDefault();
                    Raise(() => PresenceChanged?.Invoke(userId, online));
                    break;
                }

                case "roster":
                    Raise(() => RosterReceived?.Invoke(frame));
                    break;
                default:
                    Raise(() => ControlReceived?.Invoke(type, frame));
                    break;
            }
        }

        /// <summary>
        /// Refetches the room before the connection is reported healthy again. Seats, the host and the
        /// room's very existence can all have changed while the socket was down.
        /// </summary>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>A task that completes once the room has been re-read.</returns>
        protected override async Task OnReconnectedAsync(CancellationToken cancellationToken)
        {
            try
            {
                Room = await Client.RealtimeRooms.GetRoomAsync(_roomId, cancellationToken).ConfigureAwait(false);
            }
            catch (StarhermitApiException)
            {
                // The room may have closed while the socket was down; the close that follows says so.
            }
        }
    }

    /// <summary>
    /// The peer-relay socket: opaque binary frames fanned out to everyone else on the relay.
    /// </summary>
    /// <remarks>
    /// The relay forwards payloads verbatim and adds no sender prefix, so a game that needs to know
    /// who sent a packet puts that in its own payload. Exceeding the pacing the game declared closes
    /// the connection with a policy violation rather than silently dropping frames.
    /// </remarks>
    public sealed class StarhermitRelayConnection : StarhermitConnection
    {
        private readonly Guid _sessionId;
        private readonly Guid _titleId;

        internal StarhermitRelayConnection(StarhermitClient client, Guid sessionId, Guid titleId)
            : base(client, "relay")
        {
            _sessionId = sessionId;
            _titleId = titleId;
        }

        /// <summary>The relay this connection is attached to.</summary>
        public Guid SessionId => _sessionId;

        /// <summary>The catalog title the relay belongs to.</summary>
        public Guid TitleId => _titleId;

        /// <inheritdoc />
        protected override string Path => "relay";

        /// <inheritdoc />
        protected override void BuildQuery(IList<KeyValuePair<string, string>> query)
        {
            query.Add(new KeyValuePair<string, string>("sessionId", _sessionId.ToString("D")));
            query.Add(new KeyValuePair<string, string>("titleId", _titleId.ToString("D")));
        }

        /// <summary>Raised for each frame from another peer, bytes preserved exactly.</summary>
        public event Action<byte[]>? PayloadReceived;

        /// <summary>Sends a frame to every other peer on the relay.</summary>
        /// <param name="payload">The bytes to send.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the frame is sent.</returns>
        public Task SendAsync(byte[] payload, CancellationToken cancellationToken = default) =>
            SendBinaryAsync(payload, cancellationToken);

        /// <inheritdoc />
        protected override void HandleBinary(byte[] payload) => Raise(() => PayloadReceived?.Invoke(payload));

        /// <inheritdoc />
        protected override void HandleText(string text)
        {
            // The relay protocol is binary only. A text frame is not something this SDK can
            // meaningfully interpret, and inventing a meaning for it would be worse than ignoring it.
        }

        /// <summary>Rejoins the relay roster after a reconnect.</summary>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>A task that completes once membership is confirmed.</returns>
        protected override async Task OnReconnectedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Client.Relay.JoinSessionAsync(_sessionId, cancellationToken).ConfigureAwait(false);
            }
            catch (StarhermitApiException)
            {
                // Closed relay or revoked roster; the socket close reports it.
            }
        }
    }
}
