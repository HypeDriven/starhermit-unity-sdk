using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// Persists the account session between runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SDK ships no store that claims to be secure. <c>PlayerPrefs</c> is a plaintext registry key
    /// or an unprotected file depending on the platform, and describing it as secure storage would be
    /// a lie a game would build on. The default is <see cref="InMemoryTokenStore"/>: the session lives
    /// as long as the process unless the application supplies a real platform store.
    /// </para>
    /// <para>
    /// <see cref="SaveAsync"/> must be atomic. Refresh-token rotation writes the new pair before
    /// waiting callers resume, and a half-written pair would strand the account between a spent token
    /// and one that was never stored.
    /// </para>
    /// </remarks>
    public interface IStarhermitTokenStore
    {
        /// <summary>Loads the stored session, or null when there is none.</summary>
        /// <param name="cancellationToken">Cancels the load.</param>
        /// <returns>The stored session or null.</returns>
        Task<StarhermitStoredSession?> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>Stores a session, replacing any previous one atomically.</summary>
        /// <param name="session">The session to persist.</param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>A task that completes once the session is durably stored.</returns>
        Task SaveAsync(StarhermitStoredSession session, CancellationToken cancellationToken = default);

        /// <summary>Removes any stored session.</summary>
        /// <param name="cancellationToken">Cancels the clear.</param>
        /// <returns>A task that completes once nothing is stored.</returns>
        Task ClearAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>The persisted form of a session: exactly the token pair and the account it belongs to.</summary>
    public sealed class StarhermitStoredSession
    {
        /// <summary>Creates a stored session.</summary>
        /// <param name="accessToken">Bearer access token.</param>
        /// <param name="refreshToken">Rotating refresh token.</param>
        /// <param name="userId">Account id, when known.</param>
        public StarhermitStoredSession(string accessToken, string refreshToken, Guid? userId = null)
        {
            AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            RefreshToken = refreshToken ?? throw new ArgumentNullException(nameof(refreshToken));
            UserId = userId;
        }

        /// <summary>Bearer access token.</summary>
        public string AccessToken { get; }

        /// <summary>Rotating refresh token.</summary>
        public string RefreshToken { get; }

        /// <summary>Account id, when it was known at save time.</summary>
        public Guid? UserId { get; }

        /// <summary>Serialises the pair for a store that persists text.</summary>
        /// <returns>JSON holding the token pair.</returns>
        public string ToJson() => JsonWriter.SerializeObject(writer =>
        {
            writer.Write("accessToken", AccessToken);
            writer.Write("refreshToken", RefreshToken);
            writer.WriteIfPresent("userId", UserId);
        });

        /// <summary>Reads a pair previously written by <see cref="ToJson"/>.</summary>
        /// <param name="json">The stored text.</param>
        /// <returns>The session, or null when the text is absent or unreadable.</returns>
        public static StarhermitStoredSession? FromJson(string? json)
        {
            if (!JsonParser.TryParse(json, out var value) || !value.IsObject) return null;
            var accessToken = value["accessToken"].AsStringOrNull();
            var refreshToken = value["refreshToken"].AsStringOrNull();
            if (accessToken == null || refreshToken == null) return null;
            return new StarhermitStoredSession(accessToken, refreshToken, value["userId"].AsGuidOrNull());
        }

        /// <summary>Returns a description that contains no credential material.</summary>
        public override string ToString() => $"StarhermitStoredSession(user={UserId?.ToString() ?? "unknown"})";
    }

    /// <summary>
    /// Keeps the session in memory only. The default store, and the right one for a dedicated server
    /// that is handed its credentials by its host.
    /// </summary>
    public sealed class InMemoryTokenStore : IStarhermitTokenStore
    {
        private StarhermitStoredSession? _session;

        /// <summary>Creates an empty store.</summary>
        public InMemoryTokenStore()
        {
        }

        /// <summary>Creates a store seeded with an existing session.</summary>
        /// <param name="session">Session to start from.</param>
        public InMemoryTokenStore(StarhermitStoredSession? session)
        {
            _session = session;
        }

        /// <inheritdoc />
        public Task<StarhermitStoredSession?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Volatile.Read(ref _session));

        /// <inheritdoc />
        public Task SaveAsync(StarhermitStoredSession session, CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref _session, session ?? throw new ArgumentNullException(nameof(session)));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref _session, null);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Adapts any string-keyed storage - a console's save-data API, an OS keychain, an encrypted file -
    /// into a token store, so integrators write two lambdas instead of a class.
    /// </summary>
    public sealed class DelegateTokenStore : IStarhermitTokenStore
    {
        private readonly Func<CancellationToken, Task<string?>> _load;
        private readonly Func<string, CancellationToken, Task> _save;
        private readonly Func<CancellationToken, Task> _clear;

        /// <summary>Creates the store.</summary>
        /// <param name="load">Reads the previously stored text, or null.</param>
        /// <param name="save">Durably and atomically stores the text.</param>
        /// <param name="clear">Removes the stored text.</param>
        public DelegateTokenStore(
            Func<CancellationToken, Task<string?>> load,
            Func<string, CancellationToken, Task> save,
            Func<CancellationToken, Task> clear)
        {
            _load = load ?? throw new ArgumentNullException(nameof(load));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _clear = clear ?? throw new ArgumentNullException(nameof(clear));
        }

        /// <inheritdoc />
        public async Task<StarhermitStoredSession?> LoadAsync(CancellationToken cancellationToken = default) =>
            StarhermitStoredSession.FromJson(await _load(cancellationToken).ConfigureAwait(false));

        /// <inheritdoc />
        public Task SaveAsync(StarhermitStoredSession session, CancellationToken cancellationToken = default) =>
            _save((session ?? throw new ArgumentNullException(nameof(session))).ToJson(), cancellationToken);

        /// <inheritdoc />
        public Task ClearAsync(CancellationToken cancellationToken = default) => _clear(cancellationToken);
    }

    /// <summary>
    /// Holds the game-scoped and server credentials that must never share a store with the account
    /// session.
    /// </summary>
    /// <remarks>
    /// Launch tokens, deployment invoke keys and container server tokens are separate credential types
    /// with their own lifetimes and their own blast radius. Keeping them in a different store from the
    /// account session is what makes "this credential cannot reach that surface" a structural property
    /// rather than a convention.
    /// </remarks>
    public sealed class StarhermitScopedCredentials
    {
        private readonly Dictionary<string, StarhermitScopedToken> _launchTokens =
            new Dictionary<string, StarhermitScopedToken>(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new object();
        private StarhermitScopedToken? _serverToken;
        private string? _invokeKey;

        /// <summary>Stores a launch token for one game slug.</summary>
        /// <param name="gameSlug">The game the token is scoped to.</param>
        /// <param name="token">The launch token and its expiry.</param>
        public void SetLaunchToken(string gameSlug, StarhermitScopedToken token)
        {
            if (gameSlug == null) throw new ArgumentNullException(nameof(gameSlug));
            lock (_gate) _launchTokens[gameSlug] = token;
        }

        /// <summary>Reads the launch token held for a game slug, if any.</summary>
        /// <param name="gameSlug">The game to look up.</param>
        /// <returns>The token, or null when none is held.</returns>
        public StarhermitScopedToken? GetLaunchToken(string gameSlug)
        {
            if (gameSlug == null) return null;
            lock (_gate) return _launchTokens.TryGetValue(gameSlug, out var token) ? token : (StarhermitScopedToken?)null;
        }

        /// <summary>Forgets the launch token for a game slug.</summary>
        /// <param name="gameSlug">The game to clear.</param>
        public void ClearLaunchToken(string gameSlug)
        {
            if (gameSlug == null) return;
            lock (_gate) _launchTokens.Remove(gameSlug);
        }

        /// <summary>The dedicated-server token, when one has been exchanged.</summary>
        public StarhermitScopedToken? ServerToken
        {
            get { lock (_gate) return _serverToken; }
            set { lock (_gate) _serverToken = value; }
        }

        /// <summary>The deployment invoke key used to mint server tokens.</summary>
        public string? InvokeKey
        {
            get { lock (_gate) return _invokeKey; }
            set { lock (_gate) _invokeKey = value; }
        }

        /// <summary>Drops every scoped credential.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _launchTokens.Clear();
                _serverToken = null;
                _invokeKey = null;
            }
        }
    }

    /// <summary>A bearer token with a known expiry that is not the account session.</summary>
    public readonly struct StarhermitScopedToken
    {
        /// <summary>Creates a scoped token.</summary>
        /// <param name="token">The bearer token.</param>
        /// <param name="expiresAt">When it stops being accepted, when known.</param>
        public StarhermitScopedToken(string token, DateTimeOffset? expiresAt)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
            ExpiresAt = expiresAt;
        }

        /// <summary>The bearer token.</summary>
        public string Token { get; }

        /// <summary>Expiry, when the server reported one.</summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary>True when the token is at or past expiry, allowing for clock skew.</summary>
        /// <param name="now">Current time.</param>
        /// <param name="leeway">How far ahead of expiry to consider it spent.</param>
        /// <returns>True when the token should be renewed.</returns>
        public bool IsExpired(DateTimeOffset now, TimeSpan leeway) =>
            ExpiresAt.HasValue && ExpiresAt.Value - leeway <= now;

        /// <summary>Returns a description that contains no credential material.</summary>
        public override string ToString() => $"StarhermitScopedToken(expires={ExpiresAt?.ToString("u") ?? "unknown"})";
    }
}
