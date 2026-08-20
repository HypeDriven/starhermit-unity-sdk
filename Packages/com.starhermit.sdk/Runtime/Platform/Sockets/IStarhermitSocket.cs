using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>Connection state of a socket, as reported to the application.</summary>
    public enum StarhermitConnectionState
    {
        /// <summary>Not connected and not trying to be.</summary>
        Disconnected = 0,

        /// <summary>A connection attempt is in flight.</summary>
        Connecting = 1,

        /// <summary>Connected and able to carry traffic.</summary>
        Connected = 2,

        /// <summary>Dropped, and a reconnect attempt is scheduled or running.</summary>
        Reconnecting = 3,

        /// <summary>A graceful close is in progress.</summary>
        Closing = 4,

        /// <summary>Stopped because of an error that reconnecting cannot fix.</summary>
        Faulted = 5
    }

    /// <summary>
    /// The WebSocket primitive the realtime modules are built on.
    /// </summary>
    /// <remarks>
    /// WebGL cannot use a managed socket at all - the browser owns the connection - so every realtime
    /// feature talks to this interface and the platform supplies the implementation. Adapters are
    /// responsible for reassembling fragmented frames, so a message handed to the SDK is always whole.
    /// </remarks>
    public interface IStarhermitSocket : IDisposable
    {
        /// <summary>The socket's current state.</summary>
        StarhermitConnectionState State { get; }

        /// <summary>Opens the connection.</summary>
        /// <param name="uri">The <c>wss</c> address to connect to.</param>
        /// <param name="headers">Headers to send with the handshake, where the platform permits them.</param>
        /// <param name="cancellationToken">Cancels the attempt.</param>
        /// <returns>A task that completes once the socket is open.</returns>
        Task ConnectAsync(
            Uri uri,
            IReadOnlyList<KeyValuePair<string, string>> headers,
            CancellationToken cancellationToken);

        /// <summary>Sends one whole message.</summary>
        /// <param name="payload">Message bytes.</param>
        /// <param name="isText">True for a UTF-8 text frame, false for binary.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the message is handed to the transport.</returns>
        Task SendAsync(ArraySegment<byte> payload, bool isText, CancellationToken cancellationToken);

        /// <summary>Waits for the next whole message.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The message, or a close notification.</returns>
        Task<StarhermitSocketMessage> ReceiveAsync(CancellationToken cancellationToken);

        /// <summary>Closes the connection gracefully.</summary>
        /// <param name="closeStatus">WebSocket close code to send.</param>
        /// <param name="description">Optional close reason.</param>
        /// <param name="cancellationToken">Cancels waiting for the close handshake.</param>
        /// <returns>A task that completes once the close has been sent.</returns>
        Task CloseAsync(int closeStatus, string? description, CancellationToken cancellationToken);
    }

    /// <summary>Creates sockets. Injected so a platform or a test can supply its own implementation.</summary>
    public interface IStarhermitSocketFactory
    {
        /// <summary>Creates a socket that has not yet connected.</summary>
        /// <returns>A new socket.</returns>
        IStarhermitSocket Create();
    }

    /// <summary>One whole message received from a socket.</summary>
    public readonly struct StarhermitSocketMessage
    {
        private StarhermitSocketMessage(bool isText, byte[]? payload, string? text, bool isClose, int? closeStatus, string? closeDescription)
        {
            IsText = isText;
            Payload = payload;
            Text = text;
            IsClose = isClose;
            CloseStatus = closeStatus;
            CloseDescription = closeDescription;
        }

        /// <summary>True when this is a text message.</summary>
        public bool IsText { get; }

        /// <summary>Binary payload, for a binary message.</summary>
        public byte[]? Payload { get; }

        /// <summary>Decoded text, for a text message.</summary>
        public string? Text { get; }

        /// <summary>True when the peer closed the connection.</summary>
        public bool IsClose { get; }

        /// <summary>Close code sent by the peer.</summary>
        public int? CloseStatus { get; }

        /// <summary>Close reason sent by the peer.</summary>
        public string? CloseDescription { get; }

        /// <summary>Creates a text message.</summary>
        /// <param name="text">Message text.</param>
        /// <returns>The message.</returns>
        public static StarhermitSocketMessage FromText(string text) =>
            new StarhermitSocketMessage(true, null, text, false, null, null);

        /// <summary>Creates a binary message.</summary>
        /// <param name="payload">Message bytes.</param>
        /// <returns>The message.</returns>
        public static StarhermitSocketMessage FromBinary(byte[] payload) =>
            new StarhermitSocketMessage(false, payload, null, false, null, null);

        /// <summary>Creates a close notification.</summary>
        /// <param name="closeStatus">Close code from the peer.</param>
        /// <param name="description">Close reason from the peer.</param>
        /// <returns>The message.</returns>
        public static StarhermitSocketMessage FromClose(int? closeStatus, string? description) =>
            new StarhermitSocketMessage(false, null, null, true, closeStatus, description);
    }

    /// <summary>WebSocket close codes the SDK sends or recognises.</summary>
    public static class StarhermitCloseCodes
    {
        /// <summary>Normal, deliberate closure.</summary>
        public const int Normal = 1000;

        /// <summary>The endpoint is going away, for example an application quitting.</summary>
        public const int GoingAway = 1001;

        /// <summary>A protocol error.</summary>
        public const int ProtocolError = 1002;

        /// <summary>A message violated the connection's policy, including size limits.</summary>
        public const int PolicyViolation = 1008;

        /// <summary>A message exceeded the negotiated maximum size.</summary>
        public const int MessageTooBig = 1009;

        /// <summary>The connection was closed abnormally, without a close frame.</summary>
        public const int Abnormal = 1006;
    }
}
