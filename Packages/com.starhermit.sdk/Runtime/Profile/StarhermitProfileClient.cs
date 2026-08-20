using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// The account surface: profile, terms, avatar, linked identities, privacy, public keys,
    /// entitlements and presence.
    /// </summary>
    /// <remarks>
    /// Several operations here are restricted to an OAuth session by the server - changing the account
    /// email, and anything that adds or revokes a key. The SDK surfaces that refusal as a
    /// <see cref="StarhermitAuthorizationException"/> rather than trying to work around it, because the
    /// restriction is what stops a stolen key repointing the address that hands out new keys.
    /// </remarks>
    public sealed class StarhermitProfileClient : StarhermitServiceClient
    {
        internal StarhermitProfileClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Reads the signed-in account's profile.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The profile.</returns>
        public Task<StarhermitProfile> GetProfileAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Get("me"), "me.getProfile", StarhermitProfile.Read, cancellationToken);

        /// <summary>Updates the profile. Members left unset are not touched.</summary>
        /// <param name="update">Fields to change.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the update is stored.</returns>
        public Task UpdateProfileAsync(StarhermitProfileUpdate update, CancellationToken cancellationToken = default)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            return SendAsync(WithBody(Patch("me"), update.Write), "me.updateProfile", cancellationToken);
        }

        /// <summary>Records acceptance of a terms version.</summary>
        /// <param name="termsHash">Hash identifying the accepted terms, at most 64 characters.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>What the server recorded.</returns>
        public Task<StarhermitTermsAcceptance> AcceptTermsAsync(string termsHash, CancellationToken cancellationToken = default) =>
            SendAsync(
                WithBody(Post("me/terms/accept"), writer => writer.Write("hash", termsHash)),
                "me.acceptTerms",
                StarhermitTermsAcceptance.Read,
                cancellationToken);

        /// <summary>
        /// Replaces the account's avatar. The API accepts a square PNG of at most 512x512 and 1 MB.
        /// </summary>
        /// <param name="pngBytes">The PNG image.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the avatar is stored.</returns>
        public Task UpdateAvatarAsync(byte[] pngBytes, CancellationToken cancellationToken = default)
        {
            if (pngBytes == null) throw new ArgumentNullException(nameof(pngBytes));
            var request = WithBody(Put("me/avatar"), writer => writer.Write("imageBase64", Convert.ToBase64String(pngBytes)));
            return SendAsync(request, "me.updateAvatar", cancellationToken);
        }

        /// <summary>Downloads the account's own avatar.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The image bytes.</returns>
        public async Task<StarhermitAvatar> GetAvatarAsync(CancellationToken cancellationToken = default)
        {
            var binary = await SendBytesAsync(Get("me/avatar"), "me.getAvatar", cancellationToken).ConfigureAwait(false);
            return new StarhermitAvatar(binary.Bytes, binary.ContentType);
        }

        /// <summary>
        /// Downloads another account's avatar. The deployment generates a deterministic image for
        /// accounts that never uploaded one, so this succeeds for any real account.
        /// </summary>
        /// <param name="userId">The account to fetch.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The image bytes.</returns>
        public async Task<StarhermitAvatar> GetUserAvatarAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var request = Get($"users/{Escape(userId)}/avatar").WithCredential(StarhermitCredential.AccountOptional);
            var binary = await SendBytesAsync(request, "me.getUserAvatar", cancellationToken).ConfigureAwait(false);
            return new StarhermitAvatar(binary.Bytes, binary.ContentType);
        }

        /// <summary>Reads another account's public profile.</summary>
        /// <param name="userId">The account to fetch.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The public profile.</returns>
        public Task<StarhermitPublicProfile> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Get($"users/{Escape(userId)}/profile").WithCredential(StarhermitCredential.AccountOptional),
                "me.getUserProfile",
                StarhermitPublicProfile.Read,
                cancellationToken);

        /// <summary>Lists provider identities linked to the account.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The linked identities.</returns>
        public async Task<IReadOnlyList<StarhermitIdentity>> GetIdentitiesAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/identities"), "me.getIdentities", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitIdentity.Read);
        }

        /// <summary>
        /// Links a self-asserted identity for a provider the deployment does not manage through OAuth.
        /// Providers that do have an OAuth flow can only be linked through it.
        /// </summary>
        /// <param name="provider">Provider key.</param>
        /// <param name="providerUserId">The account's id at that provider.</param>
        /// <param name="metadata">Optional metadata to store with the link.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The new identity.</returns>
        public Task<StarhermitIdentity> AddIdentityAsync(
            string provider,
            string providerUserId,
            string? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("me/identities"), writer =>
            {
                writer.Write("provider", provider);
                writer.Write("providerUserId", providerUserId);
                writer.WriteIfPresent("metadata", metadata);
            });

            return SendAsync(request, "me.addIdentity", StarhermitIdentity.Read, cancellationToken);
        }

        /// <summary>Removes a linked identity.</summary>
        /// <param name="identityId">The identity to remove.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the identity is gone.</returns>
        public Task RemoveIdentityAsync(Guid identityId, CancellationToken cancellationToken = default) =>
            SendAsync(Delete($"me/identities/{Escape(identityId)}"), "me.removeIdentity", cancellationToken);

        /// <summary>Reads the account's privacy settings.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The settings, or the server defaults when none were saved.</returns>
        public async Task<StarhermitPrivacySettings> GetPrivacyAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/privacy"), "me.getPrivacy", cancellationToken).ConfigureAwait(false);
            return json.IsObject ? StarhermitPrivacySettings.Read(json) : new StarhermitPrivacySettings();
        }

        /// <summary>Replaces the account's privacy settings.</summary>
        /// <param name="settings">The settings to store.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once they are stored.</returns>
        public Task UpdatePrivacyAsync(StarhermitPrivacySettings settings, CancellationToken cancellationToken = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return SendAsync(WithBody(Put("me/privacy"), settings.Write), "me.updatePrivacy", cancellationToken);
        }

        /// <summary>
        /// Reports that the player is still present. The server owns the throttling and the window;
        /// the SDK just tells it the truth.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes once the heartbeat is recorded.</returns>
        public Task SendHeartbeatAsync(CancellationToken cancellationToken = default) =>
            SendAsync(Post("me/heartbeat"), "me.heartbeat", cancellationToken);

        /// <summary>Creates a helper that sends presence heartbeats on a timer.</summary>
        /// <param name="interval">How often to send. Defaults to one minute.</param>
        /// <returns>The heartbeat, which the caller starts, stops and disposes.</returns>
        public StarhermitPresenceHeartbeat CreateHeartbeat(TimeSpan? interval = null) =>
            new StarhermitPresenceHeartbeat(this, interval ?? TimeSpan.FromMinutes(1), Options);

        /// <summary>Lists the account's registered public keys.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The keys, including revoked ones.</returns>
        public async Task<IReadOnlyList<StarhermitPublicKey>> GetPublicKeysAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/public-keys"), "me.getPublicKeys", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitPublicKey.Read);
        }

        /// <summary>Registers a public key on the account. The API requires an OAuth session.</summary>
        /// <param name="keyType">Key algorithm.</param>
        /// <param name="publicKeyData">Base64 public key material.</param>
        /// <param name="label">Optional label for the key.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The registered key.</returns>
        public Task<StarhermitPublicKey> AddPublicKeyAsync(
            string keyType,
            string publicKeyData,
            string? label = null,
            CancellationToken cancellationToken = default)
        {
            var request = WithBody(Post("me/public-keys"), writer =>
            {
                writer.Write("keyType", keyType);
                writer.Write("keyData", publicKeyData);
                writer.WriteIfPresent("label", label);
            });

            return SendAsync(request, "me.addPublicKey", StarhermitPublicKey.Read, cancellationToken);
        }

        /// <summary>Revokes one public key and ends the sessions it authenticated.</summary>
        /// <param name="keyId">The key to revoke.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>What was revoked.</returns>
        public Task<StarhermitKeyRevocation> RevokePublicKeyAsync(Guid keyId, CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete($"me/public-keys/{Escape(keyId)}"),
                "me.revokePublicKey",
                StarhermitKeyRevocation.Read,
                cancellationToken);

        /// <summary>Revokes every public key on the account.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>What was revoked.</returns>
        public Task<StarhermitKeyRevocation> RevokeAllPublicKeysAsync(CancellationToken cancellationToken = default) =>
            SendAsync(
                Delete("me/public-keys/all"),
                "me.revokeAllPublicKeys",
                StarhermitKeyRevocation.Read,
                cancellationToken);

        /// <summary>Lists the account's entitlements.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The entitlements.</returns>
        public async Task<IReadOnlyList<StarhermitEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/entitlements"), "me.getEntitlements", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitEntitlement.Read);
        }
    }

    /// <summary>
    /// Sends presence heartbeats on a timer, and stops sending them when the application is not there
    /// to be present.
    /// </summary>
    /// <remarks>
    /// Starting twice does not create a second loop, suspension stops the timer rather than piling up
    /// missed ticks, and resuming sends immediately so a returning player shows as online without
    /// waiting out the interval.
    /// </remarks>
    public sealed class StarhermitPresenceHeartbeat : IDisposable
    {
        private readonly StarhermitProfileClient _profile;
        private readonly TimeSpan _interval;
        private readonly LevelFilteredLogger _log;
        private readonly object _gate = new object();

        private CancellationTokenSource? _cancellation;
        private Task? _loop;
        private bool _paused;
        private bool _disposed;

        internal StarhermitPresenceHeartbeat(StarhermitProfileClient profile, TimeSpan interval, StarhermitOptions options)
        {
            _profile = profile;
            _interval = interval < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : interval;
            _log = new LevelFilteredLogger(options.Logger, options.LogLevel);
        }

        /// <summary>True while the heartbeat loop is running.</summary>
        public bool IsRunning
        {
            get { lock (_gate) return _loop != null; }
        }

        /// <summary>True while the loop is running but suspended.</summary>
        public bool IsPaused
        {
            get { lock (_gate) return _paused; }
        }

        /// <summary>Starts the loop. Calling it again while running does nothing.</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(StarhermitPresenceHeartbeat));
                if (_loop != null) return;
                _cancellation = new CancellationTokenSource();
                _paused = false;
                _loop = RunAsync(_cancellation.Token);
            }
        }

        /// <summary>Stops the loop.</summary>
        public void Stop()
        {
            CancellationTokenSource? cancellation;
            lock (_gate)
            {
                cancellation = _cancellation;
                _cancellation = null;
                _loop = null;
                _paused = false;
            }

            cancellation?.Cancel();
            cancellation?.Dispose();
        }

        /// <summary>Suspends sending without tearing the loop down - for an application going to background.</summary>
        public void Pause()
        {
            lock (_gate) _paused = true;
        }

        /// <summary>Resumes after a pause and sends one heartbeat immediately.</summary>
        public void Resume()
        {
            lock (_gate)
            {
                if (!_paused) return;
                _paused = false;
            }

            _ = SendOnceAsync();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool paused;
                lock (_gate) paused = _paused;
                if (!paused) await SendOnceAsync().ConfigureAwait(false);

                try
                {
                    await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task SendOnceAsync()
        {
            try
            {
                await _profile.SendHeartbeatAsync().ConfigureAwait(false);
            }
            catch (StarhermitApiException exception)
            {
                _log.Log(StarhermitLogLevel.Debug, $"Presence heartbeat refused: {exception.Message}");
            }
            catch (StarhermitTransportException)
            {
                // Offline. The next tick will try again; presence is advisory.
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }

            Stop();
        }
    }
}
