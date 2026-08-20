using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit.Tests
{
    /// <summary>A socket driven entirely by the test: nothing leaves the process.</summary>
    public sealed class FakeSocket : IStarhermitSocket
    {
        private readonly Queue<StarhermitSocketMessage> _inbound = new Queue<StarhermitSocketMessage>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly object _gate = new object();

        /// <summary>Everything the SDK sent, in order.</summary>
        public List<SentFrame> Sent { get; } = new List<SentFrame>();

        /// <summary>The address the SDK connected to.</summary>
        public Uri? ConnectedUri { get; private set; }

        /// <summary>Headers the SDK offered for the handshake.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> ConnectHeaders { get; private set; } =
            Array.Empty<KeyValuePair<string, string>>();

        /// <summary>Set to fail the next connect attempt.</summary>
        public Exception? ConnectFailure { get; set; }

        /// <summary>The close code the SDK sent, when it closed.</summary>
        public int? SentCloseStatus { get; private set; }

        /// <inheritdoc />
        public StarhermitConnectionState State { get; private set; } = StarhermitConnectionState.Disconnected;

        /// <summary>Delivers a text message to the SDK.</summary>
        /// <param name="text">Message text.</param>
        public void PushText(string text) => Push(StarhermitSocketMessage.FromText(text));

        /// <summary>Delivers a binary message to the SDK.</summary>
        /// <param name="payload">Message bytes.</param>
        public void PushBinary(byte[] payload) => Push(StarhermitSocketMessage.FromBinary(payload));

        /// <summary>Delivers a close notification to the SDK.</summary>
        /// <param name="closeStatus">Close code.</param>
        /// <param name="description">Close reason.</param>
        public void PushClose(int closeStatus, string? description = null) =>
            Push(StarhermitSocketMessage.FromClose(closeStatus, description));

        private void Push(StarhermitSocketMessage message)
        {
            lock (_gate) _inbound.Enqueue(message);
            _signal.Release();
        }

        /// <inheritdoc />
        public Task ConnectAsync(Uri uri, IReadOnlyList<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
        {
            if (ConnectFailure != null)
            {
                var failure = ConnectFailure;
                ConnectFailure = null;
                State = StarhermitConnectionState.Faulted;
                throw failure;
            }

            ConnectedUri = uri;
            ConnectHeaders = headers;
            State = StarhermitConnectionState.Connected;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SendAsync(ArraySegment<byte> payload, bool isText, CancellationToken cancellationToken)
        {
            var copy = new byte[payload.Count];
            Buffer.BlockCopy(payload.Array!, payload.Offset, copy, 0, payload.Count);
            lock (_gate) Sent.Add(new SentFrame(copy, isText));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<StarhermitSocketMessage> ReceiveAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate) return _inbound.Dequeue();
        }

        /// <inheritdoc />
        public Task CloseAsync(int closeStatus, string? description, CancellationToken cancellationToken)
        {
            SentCloseStatus = closeStatus;
            State = StarhermitConnectionState.Disconnected;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose() => State = StarhermitConnectionState.Disconnected;

        /// <summary>One frame the SDK sent.</summary>
        public readonly struct SentFrame
        {
            internal SentFrame(byte[] payload, bool isText)
            {
                Payload = payload;
                IsText = isText;
            }

            /// <summary>The bytes sent.</summary>
            public byte[] Payload { get; }

            /// <summary>True for a text frame.</summary>
            public bool IsText { get; }

            /// <summary>The frame decoded as UTF-8 text.</summary>
            public string Text => System.Text.Encoding.UTF8.GetString(Payload);
        }
    }

    /// <summary>Hands out fake sockets and remembers them.</summary>
    public sealed class FakeSocketFactory : IStarhermitSocketFactory
    {
        /// <summary>Every socket created, in order.</summary>
        public List<FakeSocket> Created { get; } = new List<FakeSocket>();

        /// <summary>When set, the next socket created refuses its handshake with this failure.</summary>
        public Exception? NextConnectFailure { get; set; }

        /// <summary>The most recently created socket.</summary>
        public FakeSocket Last => Created[Created.Count - 1];

        /// <inheritdoc />
        public IStarhermitSocket Create()
        {
            var socket = new FakeSocket { ConnectFailure = NextConnectFailure };
            NextConnectFailure = null;
            Created.Add(socket);
            return socket;
        }
    }
}
