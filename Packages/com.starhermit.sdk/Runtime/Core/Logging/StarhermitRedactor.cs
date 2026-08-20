using System;
using System.Collections.Generic;
using System.Text;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// Removes credential material from anything the SDK is about to log, report or hand to telemetry.
    /// </summary>
    /// <remarks>
    /// Redaction is structural, not a search for known secret values: any header, query parameter or
    /// JSON member whose <em>name</em> denotes a credential is replaced, so a token the SDK has never
    /// seen before is still removed. Browser handshakes have to put the access token in the query
    /// string, which is exactly why <see cref="RedactUri"/> exists.
    /// </remarks>
    public static class StarhermitRedactor
    {
        /// <summary>The text substituted for a redacted value.</summary>
        public const string Placeholder = "***";

        private static readonly HashSet<string> SecretHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "authorization",
            "proxy-authorization",
            "cookie",
            "set-cookie",
            "x-invoke-key",
            "x-starhermit-invoke-key",
            "x-api-key",
            "idempotency-key"
        };

        private static readonly HashSet<string> SecretQueryParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "access_token",
            "accesstoken",
            "refresh_token",
            "token",
            "code",
            "id_token",
            "state",
            "signature",
            "sig",
            "x-amz-signature",
            "x-amz-credential",
            "x-amz-security-token",
            "se",
            "sp",
            "sv",
            "invoke_key",
            "key"
        };

        private static readonly HashSet<string> SecretJsonMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "accesstoken",
            "access_token",
            "refreshtoken",
            "refresh_token",
            "token",
            "launchtoken",
            "servertoken",
            "idtoken",
            "id_token",
            "code",
            "clientsecret",
            "client_secret",
            "secret",
            "password",
            "privatekey",
            "private_key",
            "keydata",
            "invokekey",
            "invoke_key",
            "signedurl",
            "signed_url",
            "signature",
            "uploadurl",
            "upload_url",
            "downloadurl",
            "download_url",
            "url"
        };

        /// <summary>True when a header's value must never be logged.</summary>
        /// <param name="name">Header name.</param>
        /// <returns>True when the header carries credential material.</returns>
        public static bool IsSecretHeader(string name) => name != null && SecretHeaders.Contains(name);

        /// <summary>True when a query parameter's value must never be logged.</summary>
        /// <param name="name">Query parameter name.</param>
        /// <returns>True when the parameter carries credential material.</returns>
        public static bool IsSecretQueryParameter(string name) => name != null && SecretQueryParameters.Contains(name);

        /// <summary>
        /// Rewrites a URI so any credential-bearing query parameter is replaced. Fragments are dropped
        /// entirely: an OAuth provider returns tokens there and nothing in a fragment is ever worth
        /// logging.
        /// </summary>
        /// <param name="uri">The address to redact.</param>
        /// <returns>A safe string form of the address.</returns>
        public static string RedactUri(Uri? uri)
        {
            if (uri == null) return string.Empty;
            if (!uri.IsAbsoluteUri) return RedactQueryString(uri.OriginalString);

            var builder = new StringBuilder();
            builder.Append(uri.GetLeftPart(UriPartial.Path));
            if (!string.IsNullOrEmpty(uri.Query)) builder.Append(RedactQueryString(uri.Query));
            return builder.ToString();
        }

        /// <summary>Redacts credential parameters inside a query string or relative address.</summary>
        /// <param name="value">A query string, with or without a leading '?'.</param>
        /// <returns>The same text with secret values replaced.</returns>
        public static string RedactQueryString(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var text = value!;
            var fragment = text.IndexOf('#');
            if (fragment >= 0) text = text.Substring(0, fragment);

            var start = text.IndexOf('?');
            if (start < 0) return text;

            var path = text.Substring(0, start);
            var query = text.Substring(start + 1);
            var builder = new StringBuilder(path);
            builder.Append('?');

            var first = true;
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                if (!first) builder.Append('&');
                first = false;

                var separator = pair.IndexOf('=');
                if (separator < 0)
                {
                    builder.Append(pair);
                    continue;
                }

                var name = pair.Substring(0, separator);
                builder.Append(name).Append('=');
                builder.Append(IsSecretQueryParameter(Uri.UnescapeDataString(name)) ? Placeholder : pair.Substring(separator + 1));
            }

            return builder.ToString();
        }

        /// <summary>Returns a header value safe to log.</summary>
        /// <param name="name">Header name.</param>
        /// <param name="value">Header value.</param>
        /// <returns>The value, or a placeholder when the header is a credential.</returns>
        public static string RedactHeader(string name, string value) =>
            IsSecretHeader(name) ? Placeholder : value;

        /// <summary>
        /// Redacts a JSON document by member name, at every depth, preserving the shape so a redacted
        /// body is still readable as a diagnostic.
        /// </summary>
        /// <param name="json">The document to redact.</param>
        /// <returns>A redacted copy.</returns>
        public static JsonValue RedactJson(JsonValue json)
        {
            if (json == null) return JsonValue.Null;

            switch (json.Kind)
            {
                case JsonKind.Object:
                {
                    var members = new List<KeyValuePair<string, JsonValue>>(json.Count);
                    foreach (var member in json.Members)
                    {
                        members.Add(new KeyValuePair<string, JsonValue>(
                            member.Key,
                            SecretJsonMembers.Contains(member.Key)
                                ? JsonValue.String(Placeholder)
                                : RedactJson(member.Value)));
                    }

                    return JsonValue.Object(members);
                }

                case JsonKind.Array:
                {
                    var items = new List<JsonValue>(json.Count);
                    foreach (var item in json.Items) items.Add(RedactJson(item));
                    return JsonValue.Array(items);
                }

                default:
                    return json;
            }
        }

        /// <summary>
        /// Redacts a response or request body for diagnostics and truncates it to a cap, so an error
        /// carries enough to debug with and never a whole payload.
        /// </summary>
        /// <param name="body">The body text.</param>
        /// <param name="maxLength">Maximum number of characters to keep.</param>
        /// <returns>A redacted, truncated copy.</returns>
        public static string RedactBody(string? body, int maxLength)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;

            var text = body!;
            if (JsonParser.TryParse(text, out var json)) text = RedactJson(json).ToJson();

            return text.Length <= maxLength
                ? text
                : text.Substring(0, maxLength) + $"... [{text.Length - maxLength} more characters]";
        }
    }
}
