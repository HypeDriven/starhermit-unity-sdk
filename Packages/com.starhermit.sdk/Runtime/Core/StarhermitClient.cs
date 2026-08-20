using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Platform;

namespace Starhermit
{
    /// <summary>
    /// The SDK's entry point: one client owning the transports, the session, and every typed service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Create"/> performs no I/O at all - it builds objects and returns. Nothing contacts
    /// the network until <see cref="InitializeAsync"/> loads a stored session, or a service call is
    /// made, or a connection is opened. A client constructed in a loading screen therefore costs
    /// nothing until the game decides to spend it.
    /// </para>
    /// <para>
    /// There is no static state anywhere in the SDK. Two clients can run side by side against
    /// different environments or accounts - which is what makes tests possible - and one client
    /// survives scene loads because nothing about it is tied to a scene.
    /// </para>
    /// <para>
    /// Disposal is complete: in-flight requests are cancelled, sockets closed gracefully, heartbeats
    /// stopped, audio released. Nothing is left running and nothing is logged on the way out.
    /// </para>
    /// </remarks>
    public sealed class StarhermitClient : IDisposable
    {
        private readonly StarhermitOptions _options;
        private readonly StarhermitRestClient _rest;
        private readonly StarhermitSessionManager _sessions;
        private readonly StarhermitScopedCredentials _credentials;
        private readonly StarhermitServerClock _serverClock;
        private readonly IStarhermitCallbackDispatcher _dispatcher;
        private readonly IStarhermitSocketFactory _socketFactory;
        private readonly List<IDisposable> _ownedConnections = new List<IDisposable>();
        private readonly object _connectionGate = new object();
        private bool _disposed;

        private StarhermitClient(StarhermitOptions options)
        {
            _options = options;
            _dispatcher = options.CallbackDispatcher ?? new SynchronizationContextDispatcher();
            _serverClock = new StarhermitServerClock(options.Clock);
            _credentials = new StarhermitScopedCredentials();

            var transport = options.Transport;
            var ownsTransport = transport == null;
            transport ??= CreateDefaultTransport();

            _socketFactory = options.SocketFactory ?? CreateDefaultSocketFactory();

            _sessions = new StarhermitSessionManager(
                options.TokenStore,
                options.Clock,
                options.TokenRefreshLeeway,
                _dispatcher);

            _rest = new StarhermitRestClient(options, transport, ownsTransport, _sessions, _credentials);

            Auth = new StarhermitAuthClient(_rest, _sessions, options);
            _sessions.RefreshCall = (refreshToken, cancellationToken) =>
                Auth.ExchangeRefreshTokenAsync(refreshToken, cancellationToken);

            Me = new StarhermitProfileClient(_rest);
            Friends = new StarhermitFriendsClient(_rest);
            Chat = new StarhermitChatClient(_rest);
            Voice = new StarhermitVoiceClient(_rest);
            Software = new StarhermitSoftwareClient(_rest);
            Entitlements = new StarhermitEntitlementsClient(_rest);
            Activity = new StarhermitActivityClient(_rest);
            Ratings = new StarhermitRatingsClient(_rest);
            Wishlist = new StarhermitWishlistClient(_rest);
            CloudSaves = new StarhermitCloudSavesClient(_rest);
            Achievements = new StarhermitAchievementsClient(_rest);
            Leaderboards = new StarhermitLeaderboardsClient(_rest);
            Games = new StarhermitGamesClient(_rest, _credentials);
            GameServer = new StarhermitGameServerClient(_rest, _credentials);
            RealtimeRooms = new StarhermitRealtimeRoomsClient(_rest);
            Relay = new StarhermitRelayClient(_rest);
            BrowserGames = new StarhermitBrowserGamesClient(_rest);
            Publishers = new StarhermitPublishersClient(_rest);
            Time = new StarhermitTimeClient(_rest, _serverClock);
            Raw = new StarhermitRawClient(_rest);
        }

        /// <summary>Creates a client. Performs no I/O.</summary>
        /// <param name="options">Client options. Copied, so later edits do not affect this client.</param>
        /// <returns>The new client.</returns>
        /// <exception cref="ArgumentException">The options are incomplete or inconsistent.</exception>
        public static StarhermitClient Create(StarhermitOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var copy = options.Clone();
            copy.Validate();
            return new StarhermitClient(copy);
        }

        /// <summary>The options this client was created with.</summary>
        public StarhermitOptions Options => _options;

        /// <summary>Authentication and the session lifecycle.</summary>
        public StarhermitAuthClient Auth { get; }

        /// <summary>The signed-in account: profile, avatar, identities, privacy, keys, presence.</summary>
        public StarhermitProfileClient Me { get; }

        /// <summary>Friend requests and the friend list.</summary>
        public StarhermitFriendsClient Friends { get; }

        /// <summary>Conversations, rooms and messages.</summary>
        public StarhermitChatClient Chat { get; }

        /// <summary>Voice rooms.</summary>
        public StarhermitVoiceClient Voice { get; }

        /// <summary>The catalog: titles, builds, claims, launches and downloads.</summary>
        public StarhermitSoftwareClient Software { get; }

        /// <summary>The account's entitlements.</summary>
        public StarhermitEntitlementsClient Entitlements { get; }

        /// <summary>Playtime, activity feeds and external libraries.</summary>
        public StarhermitActivityClient Activity { get; }

        /// <summary>Ratings and reviews.</summary>
        public StarhermitRatingsClient Ratings { get; }

        /// <summary>The account's wishlist.</summary>
        public StarhermitWishlistClient Wishlist { get; }

        /// <summary>Cloud saves.</summary>
        public StarhermitCloudSavesClient CloudSaves { get; }

        /// <summary>Achievements the account can see and claim.</summary>
        public StarhermitAchievementsClient Achievements { get; }

        /// <summary>Leaderboards.</summary>
        public StarhermitLeaderboardsClient Leaderboards { get; }

        /// <summary>Authoritative games, scoped by slug.</summary>
        public StarhermitGamesClient Games { get; }

        /// <summary>The dedicated-server surface. Belongs in a server build, never in a player build.</summary>
        public StarhermitGameServerClient GameServer { get; }

        /// <summary>Realtime rooms.</summary>
        public StarhermitRealtimeRoomsClient RealtimeRooms { get; }

        /// <summary>Peer relays.</summary>
        public StarhermitRelayClient Relay { get; }

        /// <summary>Browser games published from a repository.</summary>
        public StarhermitBrowserGamesClient BrowserGames { get; }

        /// <summary>Publisher operations.</summary>
        public StarhermitPublishersClient Publishers { get; }

        /// <summary>Server time and clock synchronisation.</summary>
        public StarhermitTimeClient Time { get; }

        /// <summary>The escape hatch for endpoints this SDK version does not type.</summary>
        public StarhermitRawClient Raw { get; }

        /// <summary>The current session, or null when signed out.</summary>
        public StarhermitSession? Session => _sessions.Current;

        /// <summary>True when a session is loaded.</summary>
        public bool IsAuthenticated => _sessions.IsAuthenticated;

        /// <summary>The server clock, corrected by the last measured offset.</summary>
        public StarhermitServerClock ServerClock => _serverClock;

        /// <summary>Where the SDK raises events and progress callbacks.</summary>
        public IStarhermitCallbackDispatcher Dispatcher => _dispatcher;

        /// <summary>
        /// The request pipeline, for the optional publishing assembly and for advanced callers that
        /// need to reach a signed storage target directly.
        /// </summary>
        public StarhermitRestClient Pipeline => _rest;

        internal StarhermitRestClient Rest => _rest;

        internal StarhermitSessionManager Sessions => _sessions;

        internal StarhermitScopedCredentials Credentials => _credentials;

        internal IStarhermitSocketFactory SocketFactory => _socketFactory;

        /// <summary>
        /// Loads any stored session and refreshes it if it has expired.
        /// </summary>
        /// <remarks>
        /// No heartbeat starts, no socket opens, nothing is subscribed. Initialising is only about
        /// answering "is this player already signed in?" before the game decides what to show.
        /// </remarks>
        /// <param name="cancellationToken">Cancels initialisation.</param>
        /// <returns>The loaded session, or null when there is none.</returns>
        public async Task<StarhermitSession?> InitializeAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var session = await _sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (session == null) return null;

            if (session.IsExpired(_options.Clock.UtcNow, _options.TokenRefreshLeeway))
            {
                await _sessions.TryRefreshAsync(session.AccessToken, cancellationToken).ConfigureAwait(false);
                return _sessions.Current;
            }

            return session;
        }

        /// <summary>Creates the live chat socket. Connect it when the game wants live delivery.</summary>
        /// <returns>The connection, owned by this client and closed when it is disposed.</returns>
        public StarhermitChatConnection CreateChatConnection()
        {
            ThrowIfDisposed();
            return new StarhermitChatConnection(this);
        }

        /// <summary>Creates a voice socket for a room the caller has already joined.</summary>
        /// <param name="roomId">The voice room.</param>
        /// <returns>The connection, owned by this client.</returns>
        public StarhermitVoiceConnection CreateVoiceConnection(Guid roomId)
        {
            ThrowIfDisposed();
            return new StarhermitVoiceConnection(this, roomId);
        }

        /// <summary>Creates a game socket for one session.</summary>
        /// <param name="sessionId">The session to attach to.</param>
        /// <param name="gameSlug">The game, when authorising with a launch token.</param>
        /// <param name="useLaunchToken">
        /// True to authorise with the game-scoped launch token instead of the account session. A game
        /// build should prefer the launch token: the backend fences it to that game's own routes.
        /// </param>
        /// <returns>The connection, owned by this client.</returns>
        public StarhermitGameConnection CreateGameConnection(Guid sessionId, string? gameSlug = null, bool useLaunchToken = false)
        {
            ThrowIfDisposed();
            return new StarhermitGameConnection(this, sessionId, gameSlug ?? _options.GameSlug, useLaunchToken);
        }

        /// <summary>Creates a realtime-room socket.</summary>
        /// <param name="roomId">The room to attach to.</param>
        /// <param name="gameSlug">The game, when authorising with a launch token.</param>
        /// <param name="useLaunchToken">True to authorise with the game-scoped launch token.</param>
        /// <returns>The connection, owned by this client.</returns>
        public StarhermitRealtimeConnection CreateRealtimeConnection(Guid roomId, string? gameSlug = null, bool useLaunchToken = false)
        {
            ThrowIfDisposed();
            return new StarhermitRealtimeConnection(this, roomId, gameSlug ?? _options.GameSlug, useLaunchToken);
        }

        /// <summary>Creates a peer-relay socket.</summary>
        /// <param name="sessionId">The relay to attach to.</param>
        /// <param name="titleId">The catalog title the relay belongs to.</param>
        /// <returns>The connection, owned by this client.</returns>
        public StarhermitRelayConnection CreateRelayConnection(Guid sessionId, Guid titleId)
        {
            ThrowIfDisposed();
            return new StarhermitRelayConnection(this, sessionId, titleId);
        }

        /// <summary>Creates an upload socket that replaces an existing browser game's bundle.</summary>
        /// <param name="gameId">The game to publish to.</param>
        /// <returns>The connection, owned by this client.</returns>
        public StarhermitGameUploadConnection CreateBundleUploadConnection(Guid gameId)
        {
            ThrowIfDisposed();
            return new StarhermitGameUploadConnection(this, gameId, null, null);
        }

        /// <summary>Creates an upload socket that creates a new browser game from an archive.</summary>
        /// <param name="displayName">Display name for the new game.</param>
        /// <param name="launchPath">Entry point within the archive.</param>
        /// <returns>The connection, owned by this client.</returns>
        public StarhermitGameUploadConnection CreateGameUploadConnection(string? displayName = null, string? launchPath = null)
        {
            ThrowIfDisposed();
            return new StarhermitGameUploadConnection(this, null, displayName, launchPath);
        }

        /// <summary>Registers a connection so disposal of the client closes it too.</summary>
        /// <param name="connection">The connection to own.</param>
        internal void TrackConnection(IDisposable connection)
        {
            lock (_connectionGate)
            {
                if (_disposed)
                {
                    connection.Dispose();
                    return;
                }

                _ownedConnections.Add(connection);
            }
        }

        /// <summary>Takes a snapshot of what the client is doing, safe to display or attach to a report.</summary>
        /// <returns>The snapshot.</returns>
        public StarhermitDiagnosticsSnapshot GetDiagnostics()
        {
            List<StarhermitConnectionDiagnostics> connections;
            lock (_connectionGate)
            {
                connections = new List<StarhermitConnectionDiagnostics>(_ownedConnections.Count);
                foreach (var connection in _ownedConnections)
                    if (connection is IStarhermitDiagnosticsSource source)
                        connections.Add(source.GetDiagnostics());
            }

            var session = _sessions.Current;
            return new StarhermitDiagnosticsSnapshot(
                _options.Clock.UtcNow,
                session != null,
                session?.UserId,
                session?.AccessTokenExpiresAt,
                _serverClock.Offset,
                _serverClock.Age,
                connections,
                _rest.InFlightRequests,
                _rest.RetriesSpent,
                _rest.LastError);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            List<IDisposable> connections;
            lock (_connectionGate)
            {
                if (_disposed) return;
                _disposed = true;
                connections = new List<IDisposable>(_ownedConnections);
                _ownedConnections.Clear();
            }

            foreach (var connection in connections)
            {
                try
                {
                    connection.Dispose();
                }
                catch (Exception)
                {
                    // Disposal must not throw: one faulty connection cannot be allowed to strand the
                    // rest of the client's resources.
                }
            }

            _rest.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StarhermitClient));
        }

        private static IStarhermitTransport CreateDefaultTransport()
        {
#if UNITY_2021_3_OR_NEWER
            return new UnityWebRequestTransport();
#else
            return new HttpClientTransport();
#endif
        }

        private static IStarhermitSocketFactory CreateDefaultSocketFactory()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLSocketFactory();
#else
            return new ClientWebSocketFactory();
#endif
        }
    }

    /// <summary>Implemented by connections that can describe their own state for diagnostics.</summary>
    public interface IStarhermitDiagnosticsSource
    {
        /// <summary>Describes this connection's current state.</summary>
        /// <returns>The diagnostics.</returns>
        StarhermitConnectionDiagnostics GetDiagnostics();
    }
}
