using System;
using System.Threading;
using System.Threading.Tasks;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// The <c>/auth</c> surface: OAuth sign-in, public-key registration and authentication, token
    /// refresh, and sign-out.
    /// </summary>
    /// <remarks>
    /// Every call that produces a session adopts it: the client stores the token pair through the
    /// configured token store and subsequent requests carry it. Nothing here logs a token, and the
    /// SDK never generates or keeps a private key - <see cref="IStarhermitSigner"/> does the signing
    /// wherever the key actually lives.
    /// </remarks>
    public sealed class StarhermitAuthClient : StarhermitServiceClient
    {
        private readonly StarhermitSessionManager _sessions;
        private readonly StarhermitOptions _options;

        internal StarhermitAuthClient(StarhermitRestClient rest, StarhermitSessionManager sessions, StarhermitOptions options)
            : base(rest)
        {
            _sessions = sessions;
            _options = options;
        }

        /// <summary>The current session, or null when signed out.</summary>
        public StarhermitSession? Session => _sessions.Current;

        /// <summary>True when a session is loaded.</summary>
        public bool IsAuthenticated => _sessions.IsAuthenticated;

        /// <summary>Raised once when the session ends for good and the player must sign in again.</summary>
        public event Action? SessionExpired
        {
            add => _sessions.SessionExpired += value;
            remove => _sessions.SessionExpired -= value;
        }

        /// <summary>Raised whenever the session changes, including when it is cleared.</summary>
        public event Action<StarhermitSession?>? SessionChanged
        {
            add => _sessions.SessionChanged += value;
            remove => _sessions.SessionChanged -= value;
        }

        /// <summary>
        /// Builds the URL that starts an OAuth flow. Open it with an
        /// <see cref="IStarhermitOAuthBrowser"/>, or hand it to a platform that owns its own browser.
        /// </summary>
        /// <param name="provider">Provider key, for example <c>google</c> or <c>github</c>.</param>
        /// <param name="link">True to link the provider to the signed-in account instead of signing in.</param>
        /// <param name="client">Optional client key selecting the deployment's configured redirect.</param>
        /// <returns>The absolute authorize URL.</returns>
        public Uri BuildAuthorizeUri(string provider, bool link = false, string? client = null)
        {
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("A provider is required.", nameof(provider));
            var request = Get($"auth/oauth/{Escape(provider)}/authorize")
                .WithQuery("link", link ? true : (bool?)null)
                .WithQuery("client", client);
            return Rest.BuildUri(request);
        }

        /// <summary>
        /// Runs a full OAuth sign-in through the configured browser adapter and adopts the session it
        /// returns.
        /// </summary>
        /// <param name="provider">Provider key, for example <c>google</c>.</param>
        /// <param name="client">Optional client key selecting the deployment's configured redirect.</param>
        /// <param name="cancellationToken">Cancels the flow.</param>
        /// <returns>The new session.</returns>
        /// <exception cref="StarhermitFeatureUnavailableException">No OAuth browser adapter is configured.</exception>
        public async Task<StarhermitSession> SignInWithOAuthAsync(
            string provider,
            string? client = null,
            CancellationToken cancellationToken = default)
        {
            var browser = _options.OAuthBrowser ?? throw new StarhermitFeatureUnavailableException(
                "auth.oauth",
                StarhermitFeatureReasons.AdapterNotConfigured,
                "OAuth sign-in needs an IStarhermitOAuthBrowser. Supply one in StarhermitOptions.OAuthBrowser, or drive the flow yourself with BuildAuthorizeUri and CompleteOAuthAsync.");

            var result = await browser
                .AuthorizeAsync(BuildAuthorizeUri(provider, link: false, client), null, cancellationToken)
                .ConfigureAwait(false);

            return await CompleteOAuthAsync(result, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Adopts the session carried by an OAuth callback that the application handled itself.
        /// </summary>
        /// <param name="result">Parameters returned by the provider.</param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>The new session.</returns>
        /// <exception cref="StarhermitApiException">The callback reported an error or carried no tokens.</exception>
        public async Task<StarhermitSession> CompleteOAuthAsync(
            StarhermitOAuthResult result,
            CancellationToken cancellationToken = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (result.Error != null)
            {
                throw StarhermitApiException.Create(new StarhermitErrorInfo
                {
                    Status = 401,
                    Method = "GET",
                    Path = "auth/oauth/callback",
                    ErrorCode = result.Error,
                    ServerMessage = result.ErrorDescription ?? result.Error
                });
            }

            var accessToken = result.AccessToken;
            var refreshToken = result.RefreshToken;
            if (accessToken == null || refreshToken == null)
            {
                throw StarhermitApiException.Create(new StarhermitErrorInfo
                {
                    Status = 401,
                    Method = "GET",
                    Path = "auth/oauth/callback",
                    ServerMessage = "The OAuth callback did not carry a token pair."
                });
            }

            var session = new StarhermitSession(accessToken, refreshToken);
            await _sessions.SetAsync(session, cancellationToken).ConfigureAwait(false);
            return session;
        }

        /// <summary>
        /// Begins registering a public key against an email address. The key is attached only once the
        /// emailed link is opened.
        /// </summary>
        /// <param name="email">Address that will receive the verification link.</param>
        /// <param name="keyType">Key algorithm.</param>
        /// <param name="publicKeyData">Base64 public key material.</param>
        /// <param name="userId">Existing account to attach the key to, when there is one.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The pending registration.</returns>
        public Task<StarhermitRegistrationReceipt> BeginPublicKeyRegistrationAsync(
            string email,
            string keyType,
            string publicKeyData,
            Guid? userId = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("auth/public-key/register"), writer =>
            {
                writer.Write("email", email);
                writer.Write("keyType", keyType);
                writer.Write("keyData", publicKeyData);
                writer.WriteIfPresent("userId", userId);
            }).WithCredential(StarhermitCredential.None);

            return SendAsync(request, "auth.beginPublicKeyRegistration", StarhermitRegistrationReceipt.Read, cancellationToken);
        }

        /// <summary>
        /// Completes a registration with the token from the verification email and adopts the session
        /// it returns.
        /// </summary>
        /// <param name="verificationToken">Token from the emailed link.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The attached key and the session issued for it.</returns>
        public async Task<StarhermitRegistrationVerification> VerifyPublicKeyRegistrationAsync(
            string verificationToken,
            CancellationToken cancellationToken = default)
        {
            var request = Get("auth/public-key/verify")
                .WithQuery("token", verificationToken)
                .WithCredential(StarhermitCredential.None);

            var verification = await SendAsync(request, "auth.verifyPublicKeyRegistration", StarhermitRegistrationVerification.Read, cancellationToken)
                .ConfigureAwait(false);
            await _sessions.SetAsync(verification.Session, cancellationToken).ConfigureAwait(false);
            return verification;
        }

        /// <summary>
        /// Asks for a link that revokes every public key on the account owning an address. Always
        /// succeeds with the same answer, so it cannot be used to discover which addresses exist.
        /// </summary>
        /// <param name="email">Address to send the confirmation to.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The server's acknowledgement message.</returns>
        public async Task<string> RequestKeyRevocationAsync(string email, CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("auth/public-key/revoke-request"), writer => writer.Write("email", email))
                .WithCredential(StarhermitCredential.None);
            var json = await SendJsonAsync(request, "auth.requestKeyRevocation", cancellationToken).ConfigureAwait(false);
            return json["message"].AsStringOrNull() ?? string.Empty;
        }

        /// <summary>Completes a held revocation using the token from the confirmation email.</summary>
        /// <param name="confirmationToken">Token from the emailed link.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>How many keys were revoked and how many sessions ended.</returns>
        public Task<StarhermitRevocationResult> ConfirmKeyRevocationAsync(
            string confirmationToken,
            CancellationToken cancellationToken = default)
        {
            var request = Get("auth/public-key/revoke/confirm")
                .WithQuery("token", confirmationToken)
                .WithCredential(StarhermitCredential.None);
            return SendAsync(request, "auth.confirmKeyRevocation", StarhermitRevocationResult.Read, cancellationToken);
        }

        /// <summary>Requests a challenge for a registered public key.</summary>
        /// <param name="keyType">Key algorithm.</param>
        /// <param name="publicKeyData">Base64 public key material.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The challenge, including the exact bytes to sign.</returns>
        public Task<StarhermitChallenge> RequestChallengeAsync(
            string keyType,
            string publicKeyData,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("auth/public-key/challenge"), writer =>
            {
                writer.Write("keyType", keyType);
                writer.Write("keyData", publicKeyData);
            }).WithCredential(StarhermitCredential.None);

            return SendAsync(request, "auth.requestChallenge", StarhermitChallenge.Read, cancellationToken);
        }

        /// <summary>Completes public-key authentication with a signature and adopts the session.</summary>
        /// <param name="challengeId">The challenge being answered.</param>
        /// <param name="signatureBase64">Base64 signature over the challenge's canonical payload.</param>
        /// <param name="keyType">Key algorithm.</param>
        /// <param name="publicKeyData">Base64 public key material.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new session.</returns>
        public async Task<StarhermitSession> CompletePublicKeyAuthenticationAsync(
            Guid challengeId,
            string signatureBase64,
            string keyType,
            string publicKeyData,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("auth/public-key/complete"), writer =>
            {
                writer.Write("challengeId", challengeId);
                writer.Write("signature", signatureBase64);
                writer.Write("keyType", keyType);
                writer.Write("keyData", publicKeyData);
            }).WithCredential(StarhermitCredential.None);

            var json = await SendJsonAsync(request, "auth.completePublicKeyAuthentication", cancellationToken).ConfigureAwait(false);
            var session = ReadSession(json);
            await _sessions.SetAsync(session, cancellationToken).ConfigureAwait(false);
            return session;
        }

        /// <summary>
        /// Signs in with a public key end to end: request a challenge, sign its canonical bytes with
        /// the configured signer, and complete.
        /// </summary>
        /// <param name="signer">Signer to use; defaults to the one in options.</param>
        /// <param name="cancellationToken">Cancels the flow.</param>
        /// <returns>The new session.</returns>
        /// <exception cref="StarhermitFeatureUnavailableException">No signer is available.</exception>
        public async Task<StarhermitSession> SignInWithPublicKeyAsync(
            IStarhermitSigner? signer = null,
            CancellationToken cancellationToken = default)
        {
            var key = signer ?? _options.Signer ?? throw new StarhermitFeatureUnavailableException(
                "auth.publicKey",
                StarhermitFeatureReasons.AdapterNotConfigured,
                "Public-key sign-in needs an IStarhermitSigner. Supply one in StarhermitOptions.Signer.");

            var challenge = await RequestChallengeAsync(key.KeyType, key.PublicKeyData, cancellationToken).ConfigureAwait(false);
            var signature = await key.SignAsync(challenge.CanonicalPayload, cancellationToken).ConfigureAwait(false);
            return await CompletePublicKeyAuthenticationAsync(
                    challenge.ChallengeId,
                    Convert.ToBase64String(signature),
                    key.KeyType,
                    key.PublicKeyData,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Exchanges a refresh token for a new pair.
        /// </summary>
        /// <remarks>
        /// Applications rarely call this: the pipeline refreshes on demand, once, with concurrent
        /// callers joined onto the same exchange. Calling it directly bypasses that coordination.
        /// </remarks>
        /// <param name="refreshToken">The refresh token to spend.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new session, not yet adopted.</returns>
        public async Task<StarhermitSession> ExchangeRefreshTokenAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("auth/refresh"), writer => writer.Write("refreshToken", refreshToken))
                .WithCredential(StarhermitCredential.None);

            var json = await SendJsonAsync(request, "auth.refresh", cancellationToken).ConfigureAwait(false);
            return ReadSession(json);
        }

        /// <summary>
        /// Signs out: revokes the refresh token server-side and clears the local session.
        /// </summary>
        /// <remarks>
        /// The local session is cleared even when the revoke call fails. Leaving a signed-out player
        /// holding a live token because the network was down is the worse outcome of the two.
        /// </remarks>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the session is gone locally.</returns>
        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            var session = _sessions.Current;
            if (session != null)
            {
                try
                {
                    var request = WithBody(Post("auth/logout"), writer => writer.Write("refreshToken", session.RefreshToken))
                        .WithCredential(StarhermitCredential.AccountOptional);
                    await SendAsync(request, "auth.logout", cancellationToken).ConfigureAwait(false);
                }
                catch (StarhermitApiException)
                {
                    // The token is already invalid server-side, which is the state we wanted.
                }
                catch (StarhermitTransportException)
                {
                    // Offline sign-out still has to end the session on this device.
                }
            }

            await _sessions.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Completes an identity link that was held for email confirmation.</summary>
        /// <param name="confirmationToken">Token from the emailed link.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The provider that was linked.</returns>
        public async Task<string> ConfirmIdentityLinkAsync(
            string confirmationToken,
            CancellationToken cancellationToken = default)
        {
            var request = Get("auth/oauth/link/confirm")
                .WithQuery("token", confirmationToken)
                .WithCredential(StarhermitCredential.None);
            var json = await SendJsonAsync(request, "auth.confirmIdentityLink", cancellationToken).ConfigureAwait(false);
            return json["provider"].AsStringOrNull() ?? string.Empty;
        }

        /// <summary>Adopts a session the application obtained by other means.</summary>
        /// <param name="session">The session to adopt.</param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>A task that completes once the session is stored.</returns>
        public Task AdoptSessionAsync(StarhermitSession session, CancellationToken cancellationToken = default) =>
            _sessions.SetAsync(session, cancellationToken);

        private static StarhermitSession ReadSession(JsonValue json) =>
            new StarhermitSession(
                json["accessToken"].AsStringOrNull() ?? throw new StarhermitSerializationException("The response carried no access token."),
                json["refreshToken"].AsStringOrNull() ?? throw new StarhermitSerializationException("The response carried no refresh token."),
                json["userId"].AsGuidOrNull());
    }
}
