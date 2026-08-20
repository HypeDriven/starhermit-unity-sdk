using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// The server-to-server surface a dedicated game server uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A container-hosted game server holds a deployment key, not a player session. It exchanges that
    /// key for a short-lived server token and reads sessions with it. The token lives in the scoped
    /// credential store, never in the account token store, and is redacted from logs by the same
    /// structural rules as every other credential.
    /// </para>
    /// <para>
    /// This client belongs in a server build. Shipping a deployment key inside a player build hands
    /// every player the game's own credentials.
    /// </para>
    /// </remarks>
    public sealed class StarhermitGameServerClient : StarhermitServiceClient
    {
        /// <summary>Header carrying the deployment refresh key when minting a server token.</summary>
        public const string RefreshKeyHeader = "X-Starhermit-Refresh-Key";

        /// <summary>Header the platform uses to invoke a container game server.</summary>
        public const string InvokeKeyHeader = "X-Starhermit-Invoke-Key";

        private readonly StarhermitScopedCredentials _credentials;

        internal StarhermitGameServerClient(StarhermitRestClient rest, StarhermitScopedCredentials credentials)
            : base(rest)
        {
            _credentials = credentials;
        }

        /// <summary>The server token currently held, if any.</summary>
        public StarhermitScopedToken? ServerToken => _credentials.ServerToken;

        /// <summary>
        /// Exchanges a deployment refresh key for a server token and stores it separately from any
        /// account session.
        /// </summary>
        /// <param name="gameSlug">The game this deployment serves.</param>
        /// <param name="refreshKey">The deployment's refresh key.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The server token and its expiry.</returns>
        public async Task<StarhermitScopedToken> AuthenticateAsync(
            string gameSlug,
            string refreshKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(gameSlug)) throw new ArgumentException("A game slug is required.", nameof(gameSlug));
            if (string.IsNullOrWhiteSpace(refreshKey)) throw new ArgumentException("A refresh key is required.", nameof(refreshKey));

            var request = Post($"games/{Escape(gameSlug)}/server/token")
                .WithCredential(StarhermitCredential.None)
                .WithHeader(RefreshKeyHeader, refreshKey);

            var json = await SendJsonAsync(request, "gameServer.authenticate", cancellationToken).ConfigureAwait(false);
            var token = json["token"].AsStringOrNull()
                        ?? throw new StarhermitSerializationException("The server-token response carried no token.");
            var expiresIn = json["expiresInSeconds"].AsInt32OrNull();

            var scoped = new StarhermitScopedToken(
                token,
                expiresIn.HasValue ? Options.Clock.UtcNow.AddSeconds(expiresIn.Value) : (DateTimeOffset?)null);

            _credentials.ServerToken = scoped;
            return scoped;
        }

        /// <summary>Reads a session with the server token.</summary>
        /// <param name="gameSlug">The game this deployment serves.</param>
        /// <param name="sessionId">The session to read.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The session.</returns>
        /// <exception cref="StarhermitFeatureUnavailableException">No server token has been obtained.</exception>
        public Task<StarhermitGameSession> GetSessionAsync(
            string gameSlug,
            Guid sessionId,
            CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"games/{Escape(gameSlug)}/server/sessions/{Escape(sessionId)}")
                    .WithCredential(StarhermitCredential.Server),
                "gameServer.getSession",
                StarhermitGameSession.Read,
                cancellationToken);

        /// <summary>Forgets the stored server token.</summary>
        public void ClearServerToken() => _credentials.ServerToken = null;
    }
}
