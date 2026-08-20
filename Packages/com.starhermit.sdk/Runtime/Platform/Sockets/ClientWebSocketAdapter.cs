using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit.Platform
{
    /// <summary>
    /// Socket adapter over <see cref="ClientWebSocket"/>.
    /// </summary>
    /// <remarks>
    /// The default everywhere a managed socket is available: desktop, mobile, console with a standard
    /// stack, and headless servers. WebGL has no managed socket at all and uses the browser bridge
    /// instead.
    /// </remarks>
    public sealed class ClientWebSocketAdapter : IStarhermitSocket
    {
        private readonly int _maxIncomingBytes;
        private readonly int _receiveBufferSize;
        private ClientWebSocket? _socket;
        private StarhermitConnectionState _state = StarhermitConnectionState.Disconnected;
        private bool _disposed;

        /// <summary>Creates the adapter.</summary>
        /// <param name="maxIncomingBytes">Largest message accepted before the connection is closed.</param>
        /// <param name="receiveBufferSize">Size of each receive chunk.</param>
        public ClientWebSocketAdapter(int maxIncomingBytes = 4 * 1024 * 1024, int receiveBufferSize = 16 * 1024)
        {
            _maxIncomingBytes = maxIncomingBytes;
            _receiveBufferSize = receiveBufferSize;
        }

        /// <inheritdoc />
        public StarhermitConnectionState State => _state;

        /// <inheritdoc />
        public async Task ConnectAsync(
            Uri uri,
            IReadOnlyList<KeyValuePair<string, string>> headers,
            CancellationToken cancellationToken)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (_disposed) throw new ObjectDisposedException(nameof(ClientWebSocketAdapter));

            _state = StarhermitConnectionState.Connecting;
            var socket = new ClientWebSocket();

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    try
                    {
                        socket.Options.SetRequestHeader(header.Key, header.Value);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // Some platforms forbid custom handshake headers. The caller has already put
                        // the credential in the query string for exactly this case, so this is not
                        // fatal - and the header is never logged either way.
                    }
                }
            }

            try
            {
                await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException exception)
            {
                _state = StarhermitConnectionState.Faulted;
                socket.Dispose();
                throw new StarhermitTransportException(
                    $"The connection to {uri.AbsolutePath} could not be opened: {exception.Message}",
                    exception);
            }
            catch (OperationCanceledException)
            {
                _state = StarhermitConnectionState.Disconnected;
                socket.Dispose();
                throw;
            }

            _socket = socket;
            _state = StarhermitConnectionState.Connected;
        }

        /// <inheritdoc />
        public async Task SendAsync(ArraySegment<byte> payload, bool isText, CancellationToken cancellationToken)
        {
            var socket = _socket ?? throw new StarhermitProtocolException("The socket is not connected.");
            try
            {
                await socket
                    .SendAsync(payload, isText ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException exception)
            {
                _state = StarhermitConnectionState.Faulted;
                throw new StarhermitTransportException($"The socket send failed: {exception.Message}", exception);
            }
        }

        /// <inheritdoc />
        public async Task<StarhermitSocketMessage> ReceiveAsync(CancellationToken cancellationToken)
        {
            var socket = _socket ?? throw new StarhermitProtocolException("The socket is not connected.");
            var buffer = new byte[_receiveBufferSize];
            using var assembled = new MemoryStream();
            var isText = false;

            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException exception)
                {
                    _state = StarhermitConnectionState.Faulted;
                    throw new StarhermitTransportException($"The socket receive failed: {exception.Message}", exception);
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _state = StarhermitConnectionState.Disconnected;
                    return StarhermitSocketMessage.FromClose(
                        result.CloseStatus.HasValue ? (int)result.CloseStatus.Value : (int?)null,
                        result.CloseStatusDescription);
                }

                isText = result.MessageType == WebSocketMessageType.Text;

                // The cap is enforced while reassembling, not after: a peer that keeps sending
                // continuation frames must not be able to grow this buffer without limit.
                if (assembled.Length + result.Count > _maxIncomingBytes)
                {
                    await CloseAsync(StarhermitCloseCodes.MessageTooBig, "Message exceeded the configured limit.", CancellationToken.None)
                        .ConfigureAwait(false);
                    throw new StarhermitProtocolException(
                        $"An inbound message exceeded the {_maxIncomingBytes}-byte limit and the connection was closed.");
                }

                assembled.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) break;
            }

            var payload = assembled.ToArray();
            return isText
                ? StarhermitSocketMessage.FromText(System.Text.Encoding.UTF8.GetString(payload))
                : StarhermitSocketMessage.FromBinary(payload);
        }

        /// <inheritdoc />
        public async Task CloseAsync(int closeStatus, string? description, CancellationToken cancellationToken)
        {
            var socket = _socket;
            if (socket == null) return;

            _state = StarhermitConnectionState.Closing;
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket
                        .CloseAsync((WebSocketCloseStatus)closeStatus, description ?? string.Empty, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is WebSocketException || exception is OperationCanceledException)
            {
                // A peer that vanished cannot complete a close handshake. The socket is closed either
                // way; the point of the handshake is politeness, not correctness.
            }
            finally
            {
                _state = StarhermitConnectionState.Disconnected;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _socket?.Dispose();
            _socket = null;
            _state = StarhermitConnectionState.Disconnected;
        }
    }

    /// <summary>Creates <see cref="ClientWebSocketAdapter"/> instances.</summary>
    public sealed class ClientWebSocketFactory : IStarhermitSocketFactory
    {
        private readonly int _maxIncomingBytes;

        /// <summary>Creates the factory.</summary>
        /// <param name="maxIncomingBytes">Largest message its sockets accept.</param>
        public ClientWebSocketFactory(int maxIncomingBytes = 4 * 1024 * 1024)
        {
            _maxIncomingBytes = maxIncomingBytes;
        }

        /// <inheritdoc />
        public IStarhermitSocket Create() => new ClientWebSocketAdapter(_maxIncomingBytes);
    }
}
