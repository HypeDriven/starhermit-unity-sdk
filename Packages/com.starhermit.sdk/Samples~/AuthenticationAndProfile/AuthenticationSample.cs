#if UNITY_2021_3_OR_NEWER
using System;
using System.Threading;
using System.Threading.Tasks;
using Starhermit;
using Starhermit.Platform;
using UnityEngine;

namespace Starhermit.Samples
{
    /// <summary>
    /// Signing in, keeping the session, and reading the account.
    /// </summary>
    /// <remarks>
    /// The important part is what this sample does <em>not</em> do: it never stores a token itself, it
    /// never logs one, and it does not ask the SDK to remember the session on disk unless the project
    /// supplies a store it trusts.
    /// </remarks>
    public sealed class AuthenticationSample : MonoBehaviour
    {
        [SerializeField]
        private StarhermitSettings? settings = null;

        [SerializeField]
        [Tooltip("Provider key configured on the deployment, for example google or github.")]
        private string provider = "google";

        private StarhermitClient? _client;
        private StarhermitPresenceHeartbeat? _heartbeat;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private void Start()
        {
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            var options = settings != null ? settings.ToOptions() : new StarhermitOptions();

            // A real project injects the platform's own secure store here. In its absence the session
            // lives in memory: the SDK will not pretend PlayerPrefs is a keychain.
            options.OAuthBrowser = new SystemBrowserOAuthAdapter();

            _client = StarhermitClient.Create(options);
            StarhermitLifecycle.Attach(_client);

            try
            {
                var restored = await _client.InitializeAsync(_lifetime.Token);
                if (restored == null)
                {
                    Debug.Log("[Sample] No stored session; starting sign-in.");
                    await _client.Auth.SignInWithOAuthAsync(provider, cancellationToken: _lifetime.Token);
                }

                var profile = await _client.Me.GetProfileAsync(_lifetime.Token);
                Debug.Log($"[Sample] Signed in as {profile.Username} ({profile.Id}).");

                if (string.IsNullOrEmpty(profile.TermsAcceptedHash))
                {
                    // A real game shows the terms first and only then records acceptance.
                    await _client.Me.AcceptTermsAsync("terms-2026-08", _lifetime.Token);
                }

                var avatar = await _client.Me.GetAvatarAsync(_lifetime.Token);
                Debug.Log($"[Sample] Avatar is {avatar.Bytes.Length} bytes of {avatar.ContentType}.");

                _heartbeat = _client.Me.CreateHeartbeat(TimeSpan.FromMinutes(1));
                _heartbeat.Start();

                _client.Auth.SessionExpired += () => Debug.Log("[Sample] The session ended; sign in again.");
            }
            catch (StarhermitFeatureUnavailableException unavailable)
            {
                Debug.LogWarning($"[Sample] {unavailable.Feature} is unavailable here ({unavailable.Reason}): {unavailable.Message}");
            }
            catch (StarhermitApiException failure)
            {
                Debug.LogError($"[Sample] The API refused sign-in: {failure.Message}");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _heartbeat?.Dispose();
            _client?.Dispose();
        }
    }

    /// <summary>
    /// A placeholder OAuth adapter: a real one opens a browser and waits for the callback.
    /// </summary>
    /// <remarks>
    /// Desktop uses a system browser and a loopback listener, mobile a custom URI scheme, WebGL a
    /// popup, and a console whatever its platform holder permits. The SDK deliberately does not pick
    /// for you, because getting this wrong is a certification failure rather than a bug.
    /// </remarks>
    public sealed class SystemBrowserOAuthAdapter : IStarhermitOAuthBrowser
    {
        /// <inheritdoc />
        public Task<StarhermitOAuthResult> AuthorizeAsync(Uri authorizeUri, string? redirectUri, CancellationToken cancellationToken)
        {
            Application.OpenURL(authorizeUri.ToString());
            throw new StarhermitFeatureUnavailableException(
                "auth.oauth",
                StarhermitFeatureReasons.AdapterNotConfigured,
                "This sample opens the browser but has nowhere to receive the callback. Implement IStarhermitOAuthBrowser for your platform.");
        }
    }
}
#endif
