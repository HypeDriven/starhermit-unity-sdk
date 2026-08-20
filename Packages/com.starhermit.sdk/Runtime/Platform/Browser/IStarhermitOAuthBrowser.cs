using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// Runs the browser half of an OAuth sign-in.
    /// </summary>
    /// <remarks>
    /// The shape of this differs completely by platform - a system browser and a loopback listener on
    /// desktop, a custom URI scheme on mobile, a popup on WebGL, and on a locked-down console often no
    /// browser at all, only an out-of-band handoff the title implements. The SDK never follows a
    /// callback address itself; it hands the authorize URL to this adapter and waits for the result.
    /// </remarks>
    public interface IStarhermitOAuthBrowser
    {
        /// <summary>Opens the authorization URL and waits for the provider to come back.</summary>
        /// <param name="authorizeUri">The URL to open.</param>
        /// <param name="redirectUri">The redirect the flow was started with, when the platform needs it.</param>
        /// <param name="cancellationToken">Cancels the flow.</param>
        /// <returns>The parameters the provider returned.</returns>
        Task<StarhermitOAuthResult> AuthorizeAsync(Uri authorizeUri, string? redirectUri, CancellationToken cancellationToken);
    }

    /// <summary>What an OAuth round trip returned.</summary>
    /// <remarks>
    /// Values here come out of a URL fragment or query. They are credentials: the SDK reads them and
    /// never logs them, and neither should an adapter.
    /// </remarks>
    public sealed class StarhermitOAuthResult
    {
        /// <summary>Creates a result.</summary>
        /// <param name="parameters">Parameters parsed from the callback.</param>
        public StarhermitOAuthResult(IReadOnlyDictionary<string, string> parameters)
        {
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        /// <summary>Every parameter returned by the provider.</summary>
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>The access token, when the callback carried one.</summary>
        public string? AccessToken => Read("access_token");

        /// <summary>The refresh token, when the callback carried one.</summary>
        public string? RefreshToken => Read("refresh_token");

        /// <summary>The authorization code, for a code flow.</summary>
        public string? Code => Read("code");

        /// <summary>The state value the provider echoed back.</summary>
        public string? State => Read("state");

        /// <summary>The error code, when the provider refused.</summary>
        public string? Error => Read("error");

        /// <summary>The provider's error description, when it sent one.</summary>
        public string? ErrorDescription => Read("error_description");

        /// <summary>Reads one parameter by name.</summary>
        /// <param name="name">Parameter name.</param>
        /// <returns>The value, or null when absent.</returns>
        public string? Read(string name) => Parameters.TryGetValue(name, out var value) ? value : null;

        /// <summary>
        /// Parses the query or fragment of a callback URL. Handles both, because providers differ and
        /// the platform adapter may hand back either.
        /// </summary>
        /// <param name="callbackUrl">The full callback URL, or just its query/fragment.</param>
        /// <returns>The parsed result.</returns>
        public static StarhermitOAuthResult Parse(string callbackUrl)
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(callbackUrl)) return new StarhermitOAuthResult(parameters);

            var text = callbackUrl;
            var hash = text.IndexOf('#');
            var question = text.IndexOf('?');

            void ReadPairs(string segment)
            {
                foreach (var pair in segment.Split('&'))
                {
                    if (pair.Length == 0) continue;
                    var separator = pair.IndexOf('=');
                    if (separator < 0) continue;
                    var name = Uri.UnescapeDataString(pair.Substring(0, separator));
                    var value = Uri.UnescapeDataString(pair.Substring(separator + 1));
                    parameters[name] = value;
                }
            }

            if (question >= 0)
            {
                var end = hash > question ? hash : text.Length;
                ReadPairs(text.Substring(question + 1, end - question - 1));
            }

            if (hash >= 0) ReadPairs(text.Substring(hash + 1));
            if (question < 0 && hash < 0) ReadPairs(text);

            return new StarhermitOAuthResult(parameters);
        }
    }
}
