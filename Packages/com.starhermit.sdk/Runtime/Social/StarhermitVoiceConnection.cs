using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// The voice socket: opaque audio frames in both directions, plus JSON control messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Received binary frames are <c>[16-byte sender account id][audio]</c>. The platform stamps that
    /// prefix server-side, so a client cannot claim to be someone else, and the SDK never relays an
    /// identity taken from client input.
    /// </para>
    /// <para>
    /// The SDK relays protocol frames and provides the PCM convention; codecs and WebRTC are optional
    /// adapters because Unity's support for them varies by target.
    /// </para>
    /// </remarks>
    public sealed class StarhermitVoiceConnection : StarhermitConnection
    {
        private readonly Guid _roomId;

        internal StarhermitVoiceConnection(StarhermitClient client, Guid roomId) : base(client, "voice")
        {
            _roomId = roomId;
        }

        /// <summary>The voice room this connection is attached to.</summary>
        public Guid RoomId => _roomId;

        /// <inheritdoc />
        protected override string Path => "voice";

        /// <inheritdoc />
        protected override void BuildQuery(IList<KeyValuePair<string, string>> query) =>
            query.Add(new KeyValuePair<string, string>("roomId", _roomId.ToString("D")));

        /// <summary>Raised for each audio frame, with the sender the platform stamped on it.</summary>
        public event Action<Guid, byte[]>? AudioReceived;

        /// <summary>Raised when a participant's mute state changes.</summary>
        public event Action<Guid, bool>? MuteChanged;

        /// <summary>Raised when a participant starts or stops speaking.</summary>
        public event Action<Guid, bool>? SpeakingChanged;

        /// <summary>Raised for WebRTC signalling, which the SDK passes through untouched.</summary>
        public event Action<JsonValue>? SignalingReceived;

        /// <summary>Raised for any control frame this SDK version does not recognise.</summary>
        public event Action<string, JsonValue>? UnknownControlReceived;

        /// <summary>Sends one encoded audio frame.</summary>
        /// <param name="audio">The encoded frame, in the room's codec or the PCM fallback.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the frame is sent.</returns>
        public Task SendAudioAsync(byte[] audio, CancellationToken cancellationToken = default) =>
            SendBinaryAsync(audio, cancellationToken);

        /// <summary>Sends one frame of 16-bit PCM using the platform's fallback convention.</summary>
        /// <param name="samples">Interleaved signed 16-bit samples.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the frame is sent.</returns>
        public Task SendPcmAsync(ArraySegment<short> samples, CancellationToken cancellationToken = default)
        {
            if (samples.Array == null) throw new ArgumentException("The sample buffer is empty.", nameof(samples));
            var bytes = new byte[samples.Count * 2];
            Buffer.BlockCopy(samples.Array, samples.Offset * 2, bytes, 0, bytes.Length);
            return SendBinaryAsync(bytes, cancellationToken);
        }

        /// <summary>Announces a mute change. Server state, not local playback volume.</summary>
        /// <param name="muted">True to mute.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the control frame is sent.</returns>
        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default) =>
            SendTextAsync(JsonWriter.SerializeObject(writer =>
            {
                writer.Write("type", "mute");
                writer.Write("muted", muted);
            }), cancellationToken);

        /// <summary>Announces that the local player has started or stopped speaking.</summary>
        /// <param name="speaking">True while speaking.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the control frame is sent.</returns>
        public Task SetSpeakingAsync(bool speaking, CancellationToken cancellationToken = default) =>
            SendTextAsync(JsonWriter.SerializeObject(writer =>
            {
                writer.Write("type", "speaking");
                writer.Write("speaking", speaking);
            }), cancellationToken);

        /// <summary>Sends a WebRTC signalling frame for an optional WebRTC adapter.</summary>
        /// <param name="writeMembers">Writes the frame's members, excluding <c>type</c>.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the frame is sent.</returns>
        public Task SendSignalingAsync(Action<JsonWriter> writeMembers, CancellationToken cancellationToken = default)
        {
            if (writeMembers == null) throw new ArgumentNullException(nameof(writeMembers));
            return SendTextAsync(JsonWriter.SerializeObject(writer =>
            {
                writer.Write("type", "rtc");
                writeMembers(writer);
            }), cancellationToken);
        }

        /// <inheritdoc />
        protected override void HandleBinary(byte[] payload)
        {
            if (payload.Length < 16)
            {
                // Too short to carry the platform's sender stamp: nothing can be attributed, so it is
                // dropped rather than guessed at.
                return;
            }

            var idBytes = new byte[16];
            Buffer.BlockCopy(payload, 0, idBytes, 0, 16);
            var sender = new Guid(idBytes);

            var audio = new byte[payload.Length - 16];
            Buffer.BlockCopy(payload, 16, audio, 0, audio.Length);

            Raise(() => AudioReceived?.Invoke(sender, audio));
        }

        /// <inheritdoc />
        protected override void HandleText(string text)
        {
            if (!JsonParser.TryParse(text, out var frame) || !frame.IsObject) return;
            var type = frame["type"].AsStringOrNull();
            if (type == null) return;

            var userId = frame["userId"].AsGuidOrNull() ?? Guid.Empty;
            switch (type)
            {
                case "mute":
                    Raise(() => MuteChanged?.Invoke(userId, frame["muted"].AsBooleanOrDefault()));
                    break;
                case "speaking":
                    Raise(() => SpeakingChanged?.Invoke(userId, frame["speaking"].AsBooleanOrDefault()));
                    break;
                case "rtc":
                    Raise(() => SignalingReceived?.Invoke(frame));
                    break;
                default:
                    Raise(() => UnknownControlReceived?.Invoke(type, frame));
                    break;
            }
        }

        /// <summary>
        /// Rejoins the room over REST before the socket is reported connected again, because a
        /// reconnect cannot assume the participant row survived.
        /// </summary>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>A task that completes once membership is confirmed.</returns>
        protected override async Task OnReconnectedAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Client.Voice.JoinRoomAsync(_roomId, cancellationToken).ConfigureAwait(false);
            }
            catch (StarhermitApiException)
            {
                // The room may be closed or the caller removed; the socket's own close will report it.
            }
        }
    }
}
