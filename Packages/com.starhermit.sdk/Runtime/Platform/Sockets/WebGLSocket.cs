#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit.Platform
{
    /// <summary>
    /// Socket adapter for WebGL, where the browser owns the connection and the managed
    /// <c>ClientWebSocket</c> does not exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The JavaScript half lives in <c>Plugins/WebGL/StarhermitWebSocket.jslib</c>. It keeps one
    /// browser <c>WebSocket</c> per handle and queues whole messages for the managed side to drain,
    /// so fragmentation is the browser's problem rather than the SDK's.
    /// </para>
    /// <para>
    /// A browser cannot set handshake headers, which is why the SDK also puts the access token in the
    /// query string. It is redacted from every log the SDK writes.
    /// </para>
    /// </remarks>
    public sealed class WebGLSocket : IStarhermitSocket
    {
        [DllImport("__Internal")]
        private static extern int StarhermitSocketCreate(string url);

        [DllImport("__Internal")]
        private static extern int StarhermitSocketState(int handle);

        [DllImport("__Internal")]
        private static extern void StarhermitSocketSend(int handle, byte[] payload, int length, int isText);

        [DllImport("__Internal")]
        private static extern int StarhermitSocketReceiveLength(int handle);

        [DllImport("__Internal")]
        private static extern int StarhermitSocketReceive(int handle, byte[] buffer, int capacity, out int isText);

        [DllImport("__Internal")]
        private static extern int StarhermitSocketCloseCode(int handle);

        [DllImport("__Internal")]
        private static extern void StarhermitSocketClose(int handle, int code, string reason);

        [DllImport("__Internal")]
        private static extern void StarhermitSocketDestroy(int handle);

        private readonly int _maxIncomingBytes;
        private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(10);
        private int _handle = -1;
        private bool _disposed;

        /// <summary>Creates the adapter.</summary>
        /// <param name="maxIncomingBytes">Largest message accepted before the connection is closed.</param>
        public WebGLSocket(int maxIncomingBytes = 4 * 1024 * 1024)
        {
            _maxIncomingBytes = maxIncomingBytes;
        }

        /// <inheritdoc />
        public StarhermitConnectionState State { get; private set; } = StarhermitConnectionState.Disconnected;

        /// <inheritdoc />
        public async Task ConnectAsync(
            Uri uri,
            IReadOnlyList<KeyValuePair<string, string>> headers,
            CancellationToken cancellationToken)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            State = StarhermitConnectionState.Connecting;
            _handle = StarhermitSocketCreate(uri.ToString());

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = StarhermitSocketState(_handle);
                if (state == 1)
                {
                    State = StarhermitConnectionState.Connected;
                    return;
                }

                if (state >= 2)
                {
                    State = StarhermitConnectionState.Faulted;
                    throw new StarhermitTransportException(
                        $"The browser refused the connection to {uri.Host} (close code {StarhermitSocketCloseCode(_handle)}).");
                }

                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public Task SendAsync(ArraySegment<byte> payload, bool isText, CancellationToken cancellationToken)
        {
            if (_handle < 0) throw new StarhermitProtocolException("The socket is not connected.");

            var buffer = payload.Array!;
            if (payload.Offset != 0 || payload.Count != buffer.Length)
            {
                buffer = new byte[payload.Count];
                Buffer.BlockCopy(payload.Array!, payload.Offset, buffer, 0, payload.Count);
            }

            StarhermitSocketSend(_handle, buffer, buffer.Length, isText ? 1 : 0);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<StarhermitSocketMessage> ReceiveAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var length = StarhermitSocketReceiveLength(_handle);
                if (length > 0)
                {
                    if (length > _maxIncomingBytes)
                    {
                        await CloseAsync(StarhermitCloseCodes.MessageTooBig, "Message exceeded the configured limit.", CancellationToken.None)
                            .ConfigureAwait(false);
                        throw new StarhermitProtocolException(
                            $"An inbound message exceeded the {_maxIncomingBytes}-byte limit and the connection was closed.");
                    }

                    var buffer = new byte[length];
                    var read = StarhermitSocketReceive(_handle, buffer, buffer.Length, out var isText);
                    if (read > 0)
                    {
                        if (isText != 0)
                            return StarhermitSocketMessage.FromText(Encoding.UTF8.GetString(buffer, 0, read));
                        if (read != buffer.Length) Array.Resize(ref buffer, read);
                        return StarhermitSocketMessage.FromBinary(buffer);
                    }
                }

                if (StarhermitSocketState(_handle) >= 2)
                {
                    State = StarhermitConnectionState.Disconnected;
                    return StarhermitSocketMessage.FromClose(StarhermitSocketCloseCode(_handle), null);
                }

                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public Task CloseAsync(int closeStatus, string? description, CancellationToken cancellationToken)
        {
            if (_handle >= 0)
            {
                State = StarhermitConnectionState.Closing;
                StarhermitSocketClose(_handle, closeStatus, description ?? string.Empty);
                State = StarhermitConnectionState.Disconnected;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle >= 0) StarhermitSocketDestroy(_handle);
            _handle = -1;
            State = StarhermitConnectionState.Disconnected;
        }
    }

    /// <summary>Creates browser-backed sockets for WebGL builds.</summary>
    public sealed class WebGLSocketFactory : IStarhermitSocketFactory
    {
        private readonly int _maxIncomingBytes;

        /// <summary>Creates the factory.</summary>
        /// <param name="maxIncomingBytes">Largest message its sockets accept.</param>
        public WebGLSocketFactory(int maxIncomingBytes = 4 * 1024 * 1024)
        {
            _maxIncomingBytes = maxIncomingBytes;
        }

        /// <inheritdoc />
        public IStarhermitSocket Create() => new WebGLSocket(_maxIncomingBytes);
    }
}
#endif
