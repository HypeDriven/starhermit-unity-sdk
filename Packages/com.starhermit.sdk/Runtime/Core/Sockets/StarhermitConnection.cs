using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// Shared machinery for every Starhermit socket: connecting, credentials, ordered sends, bounded
    /// queues, reconnection and event dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subclasses describe one protocol - its path, its query parameters, and what to do with a text
    /// or binary message. Everything else is decided once, here, so chat and relay cannot disagree
    /// about what a dropped connection means.
    /// </para>
    /// <para>
    /// Reconnection re-acquires a current access token before each attempt, backs off with jitter, and
    /// stops for good on an authorization or policy close - those do not get better by trying again.
    /// It never assumes membership survived the gap: <see cref="OnReconnectedAsync"/> is where a
    /// protocol refetches or rejoins whatever it was attached to.
    /// </para>
    /// </remarks>
    public abstract class StarhermitConnection : IDisposable, IStarhermitDiagnosticsSource
    {
        private readonly StarhermitClient _client;
        private readonly LevelFilteredLogger _log;
        private readonly Queue<PendingSend> _outbound = new Queue<PendingSend>();
        private readonly SemaphoreSlim _outboundSignal = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private readonly object _gate = new object();

        private IStarhermitSocket? _socket;
        private CancellationTokenSource? _lifetime;
        private Task? _receiveLoop;
        private Task? _sendLoop;
        private StarhermitConnectionState _state = StarhermitConnectionState.Disconnected;
        private int _reconnectAttempts;
        private DateTimeOffset? _lastActivityAt;
        private bool _disposed;
        private readonly Random _jitter = new Random();

        /// <summary>Creates the connection.</summary>
        /// <param name="client">The client that owns it.</param>
        /// <param name="name">Short name used in diagnostics, for example <c>chat</c>.</param>
        protected StarhermitConnection(StarhermitClient client, string name)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _log = new LevelFilteredLogger(client.Options.Logger, client.Options.LogLevel);
            client.TrackConnection(this);
        }

        /// <summary>Short name of this connection, used in diagnostics.</summary>
        public string Name { get; }

        /// <summary>Current connection state.</summary>
        public StarhermitConnectionState State
        {
            get { lock (_gate) return _state; }
        }

        /// <summary>True while the socket is open.</summary>
        public bool IsConnected => State == StarhermitConnectionState.Connected;

        /// <summary>Messages waiting to be sent.</summary>
        public int OutboundQueueDepth
        {
            get { lock (_gate) return _outbound.Count; }
        }

        /// <summary>
        /// Whether a dropped connection is retried automatically. On by default; authorization and
        /// policy closes stop it regardless.
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>Raised whenever the state changes.</summary>
        public event Action<StarhermitConnectionState>? StateChanged;

        /// <summary>Raised when the connection closes, with the peer's close code when there was one.</summary>
        public event Action<int?, string?>? Closed;

        /// <summary>Raised when the connection fails. The receive loop keeps running if it can.</summary>
        public event Action<Exception>? Faulted;

        /// <summary>The client that owns this connection.</summary>
        protected StarhermitClient Client => _client;

        /// <summary>Options the client was created with.</summary>
        protected StarhermitOptions Options => _client.Options;

        /// <summary>The path under the WebSocket base address, for example <c>chat</c>.</summary>
        protected abstract string Path { get; }

        /// <summary>Which credential authorises this socket.</summary>
        protected virtual StarhermitCredential Credential => StarhermitCredential.Account;

        /// <summary>The game slug whose launch token authorises this socket, when it uses one.</summary>
        protected virtual string? GameSlug => null;

        /// <summary>Adds this protocol's query parameters to the handshake address.</summary>
        /// <param name="query">Collect parameters here.</param>
        protected virtual void BuildQuery(IList<KeyValuePair<string, string>> query)
        {
        }

        /// <summary>Handles one text message.</summary>
        /// <param name="text">The message.</param>
        protected abstract void HandleText(string text);

        /// <summary>Handles one binary message.</summary>
        /// <param name="payload">The message bytes.</param>
        protected virtual void HandleBinary(byte[] payload)
        {
        }

        /// <summary>
        /// Runs after a reconnection, before the connection is reported as connected again. Refetch or
        /// rejoin here: nothing about the previous attachment can be assumed to have survived.
        /// </summary>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>A task that completes when the protocol is ready again.</returns>
        protected virtual Task OnReconnectedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>Opens the connection.</summary>
        /// <param name="cancellationToken">Cancels the attempt.</param>
        /// <returns>A task that completes once the socket is open.</returns>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Serialised rather than checked-then-acted: two callers racing here would otherwise open
            // two sockets and leak the first, and "connect it if it isn't already" is exactly the kind
            // of call a game makes from more than one place.
            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_gate)
                {
                    if (_state == StarhermitConnectionState.Connected || _state == StarhermitConnectionState.Connecting)
                        return;
                }

                CancellationTokenSource? previous;
                CancellationTokenSource lifetimeSource;
                lock (_gate)
                {
                    previous = _lifetime;
                    lifetimeSource = new CancellationTokenSource();
                    _lifetime = lifetimeSource;
                }

                // Cancel before disposing: loops still awaiting the old token must be released rather
                // than left to trip over a disposed source.
                previous?.Cancel();
                previous?.Dispose();

                await OpenAsync(cancellationToken).ConfigureAwait(false);

                var lifetime = lifetimeSource.Token;
                _sendLoop = Task.Run(() => SendLoopAsync(lifetime));
                _receiveLoop = Task.Run(() => ReceiveLoopAsync(lifetime));
            }
            finally
            {
                _connectGate.Release();
            }
        }

        /// <summary>Sends a text message, waiting until it has been handed to the transport.</summary>
        /// <param name="text">The message.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the message is sent.</returns>
        public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return EnqueueAsync(Encoding.UTF8.GetBytes(text), isText: true, cancellationToken);
        }

        /// <summary>Sends a binary message, waiting until it has been handed to the transport.</summary>
        /// <param name="payload">The message bytes.</param>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>A task that completes once the message is sent.</returns>
        public Task SendBinaryAsync(byte[] payload, CancellationToken cancellationToken = default)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return EnqueueAsync(payload, isText: false, cancellationToken);
        }

        /// <summary>Closes the connection gracefully and stops reconnecting.</summary>
        /// <param name="cancellationToken">Cancels waiting for the close handshake.</param>
        /// <returns>A task that completes once the socket is closed.</returns>
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            AutoReconnect = false;
            SetState(StarhermitConnectionState.Closing);

            IStarhermitSocket? socket;
            CancellationTokenSource? lifetime;
            lock (_gate)
            {
                socket = _socket;
                lifetime = _lifetime;
            }

            if (socket != null)
            {
                try
                {
                    await socket.CloseAsync(StarhermitCloseCodes.Normal, "Closing", cancellationToken).ConfigureAwait(false);
                }
                catch (StarhermitTransportException)
                {
                    // Already gone. Nothing to close politely.
                }
            }

            lifetime?.Cancel();
            DrainQueue(new OperationCanceledException("The connection was closed."));
            SetState(StarhermitConnectionState.Disconnected);
        }

        /// <inheritdoc />
        public StarhermitConnectionDiagnostics GetDiagnostics()
        {
            lock (_gate)
            {
                return new StarhermitConnectionDiagnostics(Name, _state, _outbound.Count, _reconnectAttempts, _lastActivityAt);
            }
        }

        /// <summary>Builds the handshake address, including this protocol's query parameters.</summary>
        /// <param name="accessToken">Token to place in the query string, when headers are unavailable.</param>
        /// <returns>The absolute socket address.</returns>
        protected Uri BuildUri(string? accessToken)
        {
            var query = new List<KeyValuePair<string, string>>(4);
            BuildQuery(query);

            var builder = new StringBuilder(Path.TrimStart('/'));
            var first = true;
            foreach (var parameter in query)
            {
                builder.Append(first ? '?' : '&');
                first = false;
                builder.Append(Uri.EscapeDataString(parameter.Key)).Append('=').Append(Uri.EscapeDataString(parameter.Value));
            }

            if (accessToken != null)
            {
                builder.Append(first ? '?' : '&');
                builder.Append("access_token=").Append(Uri.EscapeDataString(accessToken));
            }

            return new Uri(Options.ResolveWebSocketBaseUri(), builder.ToString());
        }

        /// <summary>Raises an application callback, containing any exception it throws.</summary>
        /// <param name="raise">The callback to run.</param>
        protected void Raise(Action raise)
        {
            _client.Dispatcher.Post(() =>
            {
                try
                {
                    raise();
                }
                catch (Exception exception)
                {
                    // A handler that throws must not stop the receive loop; the next message still
                    // has to arrive.
                    _log.Log(StarhermitLogLevel.Error, $"A {Name} connection handler threw.", exception);
                }
            });
        }

        private async Task OpenAsync(CancellationToken cancellationToken)
        {
            SetState(StarhermitConnectionState.Connecting);

            var token = await ResolveTokenAsync(cancellationToken).ConfigureAwait(false);
            var socket = _client.SocketFactory.Create();
            var headers = new List<KeyValuePair<string, string>>(2);
            if (token != null) headers.Add(new KeyValuePair<string, string>("Authorization", "Bearer " + token));

            // The token also goes in the query string: browsers cannot set a handshake header, and the
            // deployment accepts ?access_token= on /ws for exactly that reason. It is redacted from
            // every log and never copied into telemetry.
            var uri = BuildUri(token);

            using var timeout = new CancellationTokenSource(Options.ConnectTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            try
            {
                await socket.ConnectAsync(uri, headers, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                SetState(StarhermitConnectionState.Faulted);
                throw new StarhermitTimeoutException(
                    $"Connecting the {Name} socket timed out after {Options.ConnectTimeout.TotalSeconds:0.##}s.",
                    Options.ConnectTimeout);
            }
            catch
            {
                socket.Dispose();
                SetState(StarhermitConnectionState.Faulted);
                throw;
            }

            lock (_gate)
            {
                _socket = socket;
                _lastActivityAt = Options.Clock.UtcNow;
            }

            SetState(StarhermitConnectionState.Connected);
            _log.Log(StarhermitLogLevel.Info, $"The {Name} socket is connected.");
        }

        private async Task<string?> ResolveTokenAsync(CancellationToken cancellationToken)
        {
            switch (Credential)
            {
                case StarhermitCredential.None:
                    return null;
                case StarhermitCredential.Launch:
                {
                    var slug = GameSlug ?? Options.GameSlug;
                    var launch = slug == null ? null : _client.Credentials.GetLaunchToken(slug);
                    if (launch == null)
                    {
                        throw new StarhermitFeatureUnavailableException(
                            "sockets.launchToken",
                            StarhermitFeatureReasons.AdapterNotConfigured,
                            $"The {Name} socket needs a launch token for '{slug ?? "<no slug>"}'. Mint one with Games.ForSlug(slug).AcquireLaunchTokenAsync() first.");
                    }

                    return launch.Value.Token;
                }

                case StarhermitCredential.Server:
                {
                    var server = _client.Credentials.ServerToken;
                    if (server == null)
                    {
                        throw new StarhermitFeatureUnavailableException(
                            "sockets.serverToken",
                            StarhermitFeatureReasons.AdapterNotConfigured,
                            $"The {Name} socket needs a dedicated-server token. Exchange an invoke key first.");
                    }

                    return server.Value.Token;
                }

                default:
                {
                    var token = await _client.Sessions.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                    if (token == null)
                    {
                        throw StarhermitApiException.Create(new StarhermitErrorInfo
                        {
                            Status = 401,
                            Method = "GET",
                            Path = Path,
                            ServerMessage = $"The {Name} socket needs a signed-in account; no session is loaded."
                        });
                    }

                    return token;
                }
            }
        }

        private Task EnqueueAsync(byte[] payload, bool isText, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (payload.Length > Options.MaxOutgoingMessageBytes)
            {
                throw new StarhermitProtocolException(
                    $"A {payload.Length}-byte message exceeds the {Options.MaxOutgoingMessageBytes}-byte outgoing limit.");
            }

            var pending = new PendingSend(payload, isText);
            lock (_gate)
            {
                if (_state == StarhermitConnectionState.Disconnected || _state == StarhermitConnectionState.Faulted)
                    throw new StarhermitProtocolException($"The {Name} socket is not connected.");

                if (_outbound.Count >= Options.MaxOutboundQueuedMessages)
                {
                    // Backpressure is explicit: the caller is told the queue is full rather than the
                    // SDK growing it until the process runs out of memory.
                    throw new StarhermitProtocolException(
                        $"The {Name} socket's outbound queue is full ({Options.MaxOutboundQueuedMessages} messages). Slow down or increase StarhermitOptions.MaxOutboundQueuedMessages.");
                }

                _outbound.Enqueue(pending);
            }

            _outboundSignal.Release();

            if (!cancellationToken.CanBeCanceled) return pending.Completion.Task;
            return WaitWithCancellation(pending, cancellationToken);
        }

        private static async Task WaitWithCancellation(PendingSend pending, CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => pending.Completion.TrySetCanceled(cancellationToken)))
            {
                await pending.Completion.Task.ConfigureAwait(false);
            }
        }

        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _outboundSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                PendingSend? pending = null;
                lock (_gate)
                {
                    if (_outbound.Count > 0) pending = _outbound.Dequeue();
                }

                if (pending == null) continue;

                IStarhermitSocket? socket;
                lock (_gate) socket = _socket;

                if (socket == null || State != StarhermitConnectionState.Connected)
                {
                    pending.Completion.TrySetException(new StarhermitProtocolException($"The {Name} socket is not connected."));
                    continue;
                }

                try
                {
                    // One send at a time, in queue order: per-connection ordering is part of the
                    // contract every one of these protocols relies on.
                    await socket
                        .SendAsync(new ArraySegment<byte>(pending.Payload), pending.IsText, cancellationToken)
                        .ConfigureAwait(false);
                    lock (_gate) _lastActivityAt = Options.Clock.UtcNow;
                    pending.Completion.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    pending.Completion.TrySetCanceled();
                    return;
                }
                catch (Exception exception)
                {
                    pending.Completion.TrySetException(exception);
                }
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IStarhermitSocket? socket;
                lock (_gate) socket = _socket;
                if (socket == null) return;

                StarhermitSocketMessage message;
                try
                {
                    message = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _log.Log(StarhermitLogLevel.Warning, $"The {Name} socket dropped: {exception.Message}");
                    Raise(() => Faulted?.Invoke(exception));
                    if (!await TryReconnectAsync(null, cancellationToken).ConfigureAwait(false)) return;
                    continue;
                }

                lock (_gate) _lastActivityAt = Options.Clock.UtcNow;

                if (message.IsClose)
                {
                    _log.Log(StarhermitLogLevel.Info, $"The {Name} socket was closed by the server ({message.CloseStatus}).");
                    Raise(() => Closed?.Invoke(message.CloseStatus, message.CloseDescription));
                    if (!await TryReconnectAsync(message.CloseStatus, cancellationToken).ConfigureAwait(false)) return;
                    continue;
                }

                try
                {
                    if (message.IsText) HandleText(message.Text!);
                    else HandleBinary(message.Payload!);
                }
                catch (Exception exception)
                {
                    // A protocol frame the SDK could not read is reported and skipped. One bad frame
                    // must not end a connection that is otherwise healthy.
                    _log.Log(StarhermitLogLevel.Error, $"A {Name} frame could not be handled.", exception);
                    Raise(() => Faulted?.Invoke(exception));
                }
            }
        }

        private async Task<bool> TryReconnectAsync(int? closeStatus, CancellationToken cancellationToken)
        {
            if (!AutoReconnect || cancellationToken.IsCancellationRequested)
            {
                SetState(StarhermitConnectionState.Disconnected);
                return false;
            }

            // An authorization or policy refusal is a decision, not a hiccup. Reconnecting into it
            // just burns battery and, with a rate-limit close, digs the hole deeper.
            if (closeStatus == StarhermitCloseCodes.PolicyViolation ||
                closeStatus == StarhermitCloseCodes.MessageTooBig ||
                closeStatus == 1011 ||
                closeStatus == 4001 ||
                closeStatus == 4003)
            {
                _log.Log(StarhermitLogLevel.Warning, $"The {Name} socket will not reconnect after close code {closeStatus}.");
                SetState(StarhermitConnectionState.Faulted);
                return false;
            }

            SetState(StarhermitConnectionState.Reconnecting);

            while (!cancellationToken.IsCancellationRequested)
            {
                int attempt;
                lock (_gate) attempt = ++_reconnectAttempts;

                if (!StarhermitRetryBudget.Shared.TryConsume())
                {
                    _log.Log(StarhermitLogLevel.Warning, $"The {Name} socket stopped reconnecting: the process retry budget is exhausted.");
                    SetState(StarhermitConnectionState.Faulted);
                    return false;
                }

                var delay = ComputeBackoff(attempt);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    SetState(StarhermitConnectionState.Disconnected);
                    return false;
                }

                IStarhermitSocket? previous;
                lock (_gate)
                {
                    previous = _socket;
                    _socket = null;
                }

                previous?.Dispose();

                try
                {
                    await OpenAsync(cancellationToken).ConfigureAwait(false);

                    try
                    {
                        await OnReconnectedAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (!(exception is OperationCanceledException))
                    {
                        // The socket is open; only the rejoin or refetch failed. Reporting the
                        // connection as broken here would hide frames that are already arriving, so
                        // the failure is logged and the server's own close is left to decide.
                        _log.Log(
                            StarhermitLogLevel.Warning,
                            $"The {Name} socket reconnected but its state refresh failed: {exception.Message}");
                    }

                    lock (_gate) _reconnectAttempts = 0;
                    return true;
                }
                catch (StarhermitApiException exception) when (exception.Status == 401 || exception.Status == 403)
                {
                    _log.Log(StarhermitLogLevel.Warning, $"The {Name} socket cannot reauthenticate; giving up.");
                    SetState(StarhermitConnectionState.Faulted);
                    return false;
                }
                catch (StarhermitFeatureUnavailableException)
                {
                    SetState(StarhermitConnectionState.Faulted);
                    return false;
                }
                catch (Exception exception)
                {
                    _log.Log(StarhermitLogLevel.Debug, $"The {Name} socket reconnect attempt {attempt} failed: {exception.Message}");
                    SetState(StarhermitConnectionState.Reconnecting);
                }
            }

            SetState(StarhermitConnectionState.Disconnected);
            return false;
        }

        private TimeSpan ComputeBackoff(int attempt)
        {
            var exponent = Math.Min(attempt - 1, 8);
            var milliseconds = Math.Min(500 * Math.Pow(2, exponent), 30000);
            double jitter;
            lock (_jitter) jitter = _jitter.NextDouble() * 0.4 - 0.2;
            return TimeSpan.FromMilliseconds(Math.Max(100, milliseconds * (1 + jitter)));
        }

        private void SetState(StarhermitConnectionState state)
        {
            lock (_gate)
            {
                if (_state == state) return;
                _state = state;
            }

            Raise(() => StateChanged?.Invoke(state));
        }

        private void DrainQueue(Exception reason)
        {
            lock (_gate)
            {
                while (_outbound.Count > 0) _outbound.Dequeue().Completion.TrySetException(reason);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }

            AutoReconnect = false;

            CancellationTokenSource? lifetime;
            IStarhermitSocket? socket;
            lock (_gate)
            {
                lifetime = _lifetime;
                socket = _socket;
                _lifetime = null;
                _socket = null;
            }

            lifetime?.Cancel();
            lifetime?.Dispose();
            socket?.Dispose();
            DrainQueue(new ObjectDisposedException(GetType().Name));
            _outboundSignal.Dispose();
            _connectGate.Dispose();
            SetState(StarhermitConnectionState.Disconnected);
        }

        private sealed class PendingSend
        {
            internal PendingSend(byte[] payload, bool isText)
            {
                Payload = payload;
                IsText = isText;
            }

            internal byte[] Payload { get; }

            internal bool IsText { get; }

            internal TaskCompletionSource<bool> Completion { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
