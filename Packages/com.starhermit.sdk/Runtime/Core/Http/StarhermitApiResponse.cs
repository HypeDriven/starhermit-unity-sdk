using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// A successful response, in whichever form the request asked for.
    /// </summary>
    /// <remarks>
    /// A streamed response owns a live connection: dispose it, or the socket stays open until the
    /// finaliser runs.
    /// </remarks>
    public sealed class StarhermitApiResponse : IDisposable
    {
        private readonly IDisposable? _owner;
        private JsonValue? _json;
        private bool _disposed;

        internal StarhermitApiResponse(
            int status,
            IReadOnlyDictionary<string, string> headers,
            byte[]? body,
            Stream? bodyStream,
            string? requestId,
            IDisposable? owner)
        {
            Status = status;
            Headers = headers;
            Body = body;
            BodyStream = bodyStream;
            RequestId = requestId;
            _owner = owner;
        }

        /// <summary>HTTP status code.</summary>
        public int Status { get; }

        /// <summary>Response headers.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Buffered body bytes, when the request asked for bytes or JSON.</summary>
        public byte[]? Body { get; }

        /// <summary>Live body stream, when the request asked to stream.</summary>
        public Stream? BodyStream { get; }

        /// <summary>Server correlation id, when the deployment returned one.</summary>
        public string? RequestId { get; }

        /// <summary>True when the response had no body at all.</summary>
        public bool IsEmpty => (Body == null || Body.Length == 0) && BodyStream == null;

        /// <summary>Reads a response header.</summary>
        /// <param name="name">Header name.</param>
        /// <returns>The value, or null when absent.</returns>
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

        /// <summary>
        /// The body parsed as JSON. An empty body reads as a JSON null, so a <c>204</c> is not an
        /// error for an operation that has nothing to return.
        /// </summary>
        /// <exception cref="StarhermitSerializationException">The body is not valid JSON.</exception>
        public JsonValue Json
        {
            get
            {
                if (_json != null) return _json;
                if (Body == null || Body.Length == 0) return _json = JsonValue.Null;
                return _json = JsonParser.Parse(Body);
            }
        }

        /// <summary>The body decoded as UTF-8 text.</summary>
        /// <returns>The body text, or an empty string when there is none.</returns>
        public string Text() => Body == null ? string.Empty : Encoding.UTF8.GetString(Body);

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BodyStream?.Dispose();
            _owner?.Dispose();
        }
    }
}
