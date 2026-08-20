using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>Signature of the call that exchanges a refresh token for a new pair.</summary>
    /// <param name="refreshToken">The refresh token to spend.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The new session.</returns>
    public delegate Task<StarhermitSession> StarhermitRefreshCall(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Owns the account session and serialises refreshes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only one refresh may be in flight per client. Ten requests that all see <c>401</c> at once must
    /// produce one refresh and nine waiters, not ten refreshes - with a rotating refresh token, the
    /// second exchange invalidates the first and the family gets revoked, signing the player out for
    /// no reason.
    /// </para>
    /// <para>
    /// The rotated pair is persisted <em>before</em> waiters resume. A crash between exchange and save
    /// would otherwise leave the store holding a token the server has already retired.
    /// </para>
    /// <para>
    /// Failure is classified rather than uniform: a definitive rejection ends the session and raises
    /// <see cref="SessionExpired"/> exactly once, while a network error leaves the session intact so
    /// the player is not signed out by a tunnel.
    /// </para>
    /// </remarks>
    public sealed class StarhermitSessionManager
    {
        private readonly IStarhermitTokenStore _store;
        private readonly IStarhermitClock _clock;
        private readonly TimeSpan _leeway;
        private readonly IStarhermitCallbackDispatcher _dispatcher;
        private readonly object _gate = new object();

        private StarhermitSession? _session;
        private Task<bool>? _refreshInFlight;
        private string? _refreshingFromAccessToken;
        private bool _expiredRaised;

        /// <summary>Creates a session manager.</summary>
        /// <param name="store">Where the session is persisted.</param>
        /// <param name="clock">Time source.</param>
        /// <param name="leeway">How far ahead of expiry to refresh.</param>
        /// <param name="dispatcher">Where events are raised.</param>
        public StarhermitSessionManager(
            IStarhermitTokenStore store,
            IStarhermitClock clock,
            TimeSpan leeway,
            IStarhermitCallbackDispatcher dispatcher)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _leeway = leeway;
            _dispatcher = dispatcher ?? ImmediateCallbackDispatcher.Instance;
        }

        /// <summary>The call used to exchange a refresh token. Wired by the client at construction.</summary>
        public StarhermitRefreshCall? RefreshCall { get; set; }

        /// <summary>Raised once when a session ends for good and the player must sign in again.</summary>
        public event Action? SessionExpired;

        /// <summary>Raised whenever the current session changes, including when it is cleared.</summary>
        public event Action<StarhermitSession?>? SessionChanged;

        /// <summary>The current session, or null when signed out.</summary>
        public StarhermitSession? Current
        {
            get { lock (_gate) return _session; }
        }

        /// <summary>True when a session is loaded.</summary>
        public bool IsAuthenticated => Current != null;

        /// <summary>Loads any persisted session into memory.</summary>
        /// <param name="cancellationToken">Cancels the load.</param>
        /// <returns>The loaded session, or null.</returns>
        public async Task<StarhermitSession?> LoadAsync(CancellationToken cancellationToken = default)
        {
            var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (stored == null) return null;

            var session = new StarhermitSession(stored.AccessToken, stored.RefreshToken, stored.UserId);
            lock (_gate)
            {
                _session = session;
                _expiredRaised = false;
            }

            RaiseChanged(session);
            return session;
        }

        /// <summary>Adopts a session and persists it.</summary>
        /// <param name="session">The session to adopt.</param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>A task that completes once the session is stored.</returns>
        public async Task SetAsync(StarhermitSession session, CancellationToken cancellationToken = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            await _store
                .SaveAsync(new StarhermitStoredSession(session.AccessToken, session.RefreshToken, session.UserId), cancellationToken)
                .ConfigureAwait(false);

            lock (_gate)
            {
                _session = session;
                _expiredRaised = false;
            }

            RaiseChanged(session);
        }

        /// <summary>Ends the session locally and clears the store.</summary>
        /// <param name="cancellationToken">Cancels the clear.</param>
        /// <returns>A task that completes once nothing is stored.</returns>
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate) _session = null;
            await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
            RaiseChanged(null);
        }

        /// <summary>
        /// Returns an access token to send, refreshing first when the current one is spent.
        /// </summary>
        /// <param name="cancellationToken">Cancels the refresh.</param>
        /// <returns>The token to send, or null when there is no session.</returns>
        public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            var session = Current;
            if (session == null) return null;

            if (session.IsExpired(_clock.UtcNow, _leeway))
            {
                await TryRefreshAsync(session.AccessToken, cancellationToken).ConfigureAwait(false);
                session = Current;
            }

            return session?.AccessToken;
        }

        /// <summary>
        /// Refreshes the session, joining an in-flight refresh rather than starting a second one.
        /// </summary>
        /// <param name="spentAccessToken">
        /// The access token the caller found unusable. When it is not the current one, another refresh
        /// has already replaced it and this returns success without a further exchange.
        /// </param>
        /// <param name="cancellationToken">Cancels waiting for the refresh.</param>
        /// <returns>True when a usable session is now in place.</returns>
        public Task<bool> TryRefreshAsync(string? spentAccessToken, CancellationToken cancellationToken = default)
        {
            Task<bool> refresh;
            lock (_gate)
            {
                if (_session == null) return Task.FromResult(false);

                // Someone else already rotated past the token this caller was using: nothing to do.
                if (spentAccessToken != null &&
                    !string.Equals(spentAccessToken, _session.AccessToken, StringComparison.Ordinal) &&
                    !string.Equals(spentAccessToken, _refreshingFromAccessToken, StringComparison.Ordinal))
                {
                    return Task.FromResult(true);
                }

                if (_refreshInFlight != null) return _refreshInFlight;

                _refreshingFromAccessToken = _session.AccessToken;
                refresh = RefreshCoreAsync(_session.RefreshToken);
                _refreshInFlight = refresh;
            }

            return refresh;
        }

        private async Task<bool> RefreshCoreAsync(string refreshToken)
        {
            // Yield first so the lock in TryRefreshAsync is released before any awaiting happens and
            // the in-flight task is observable to every other caller.
            await Task.Yield();

            try
            {
                var call = RefreshCall;
                if (call == null) return false;

                StarhermitSession refreshed;
                try
                {
                    refreshed = await call(refreshToken, CancellationToken.None).ConfigureAwait(false);
                }
                catch (StarhermitAuthenticationException)
                {
                    await EndSessionAsync().ConfigureAwait(false);
                    return false;
                }
                catch (StarhermitAuthorizationException)
                {
                    await EndSessionAsync().ConfigureAwait(false);
                    return false;
                }
                catch (StarhermitApiException exception) when (exception.Status == 400 || exception.Status == 404)
                {
                    // The deployment refused the token itself rather than the request shape; the
                    // session is over whatever status it chose to say so with.
                    await EndSessionAsync().ConfigureAwait(false);
                    return false;
                }
                catch (StarhermitTransportException)
                {
                    // Transient: the session is still valid, the network was not.
                    return false;
                }

                // Store the rotated pair before anyone resumes, so a crash here cannot leave the store
                // holding a refresh token the server has already retired.
                await SetAsync(refreshed).ConfigureAwait(false);
                return true;
            }
            finally
            {
                lock (_gate)
                {
                    _refreshInFlight = null;
                    _refreshingFromAccessToken = null;
                }
            }
        }

        private async Task EndSessionAsync()
        {
            bool raise;
            lock (_gate)
            {
                _session = null;
                raise = !_expiredRaised;
                _expiredRaised = true;
            }

            await _store.ClearAsync().ConfigureAwait(false);
            RaiseChanged(null);
            if (raise) _dispatcher.Post(() => SessionExpired?.Invoke());
        }

        private void RaiseChanged(StarhermitSession? session) =>
            _dispatcher.Post(() => SessionChanged?.Invoke(session));
    }
}
