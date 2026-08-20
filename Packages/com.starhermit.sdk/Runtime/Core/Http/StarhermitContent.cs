using System;
using System.IO;
using System.Text;

namespace Starhermit
{
    /// <summary>
    /// A request body.
    /// </summary>
    /// <remarks>
    /// <see cref="CanReplay"/> is the property that matters to the retry pipeline: a body the SDK
    /// cannot produce a second time is never retried, because replaying half a consumed stream would
    /// upload a truncated file and call it a success.
    /// </remarks>
    public abstract class StarhermitContent
    {
        /// <summary>Creates content with the given media type.</summary>
        /// <param name="contentType">Value for the <c>Content-Type</c> header.</param>
        protected StarhermitContent(string contentType)
        {
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        }

        /// <summary>The media type sent with this body.</summary>
        public string ContentType { get; }

        /// <summary>True when the body can be produced again for a retry.</summary>
        public abstract bool CanReplay { get; }

        /// <summary>Byte length when known ahead of time, otherwise null.</summary>
        public abstract long? Length { get; }

        /// <summary>Materialises the body as bytes.</summary>
        /// <returns>The encoded body.</returns>
        public abstract byte[] ReadBytes();

        /// <summary>Opens the body as a stream for a streaming transport.</summary>
        /// <returns>A readable stream positioned at the start of the body.</returns>
        public virtual Stream OpenStream() => new MemoryStream(ReadBytes(), writable: false);

        /// <summary>Creates a JSON body.</summary>
        /// <param name="json">JSON text.</param>
        /// <returns>Replayable content.</returns>
        public static StarhermitContent Json(string json) =>
            new BytesContent(Encoding.UTF8.GetBytes(json ?? throw new ArgumentNullException(nameof(json))), "application/json; charset=utf-8");

        /// <summary>Creates a body from bytes.</summary>
        /// <param name="bytes">Body bytes.</param>
        /// <param name="contentType">Media type.</param>
        /// <returns>Replayable content.</returns>
        public static StarhermitContent Bytes(byte[] bytes, string contentType = "application/octet-stream") =>
            new BytesContent(bytes ?? throw new ArgumentNullException(nameof(bytes)), contentType);

        /// <summary>Creates a form-encoded body.</summary>
        /// <param name="fields">Field names and values, encoded in order.</param>
        /// <returns>Replayable content.</returns>
        public static StarhermitContent Form(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var builder = new StringBuilder();
            foreach (var field in fields)
            {
                if (builder.Length > 0) builder.Append('&');
                builder.Append(Uri.EscapeDataString(field.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(field.Value ?? string.Empty));
            }

            return new BytesContent(Encoding.UTF8.GetBytes(builder.ToString()), "application/x-www-form-urlencoded");
        }

        /// <summary>
        /// Creates a body that streams from a factory. The factory is called once per attempt, so
        /// content built this way stays replayable and retry-eligible.
        /// </summary>
        /// <param name="openStream">Opens a fresh stream over the same bytes.</param>
        /// <param name="length">Byte length when known.</param>
        /// <param name="contentType">Media type.</param>
        /// <returns>Replayable streaming content.</returns>
        public static StarhermitContent Stream(Func<Stream> openStream, long? length, string contentType = "application/octet-stream") =>
            new FactoryStreamContent(openStream ?? throw new ArgumentNullException(nameof(openStream)), length, contentType, canReplay: true);

        /// <summary>
        /// Creates a body over a stream that can only be read once - a network or pipe source. Such a
        /// request is never retried automatically.
        /// </summary>
        /// <param name="stream">The single-use stream.</param>
        /// <param name="length">Byte length when known.</param>
        /// <param name="contentType">Media type.</param>
        /// <returns>Non-replayable streaming content.</returns>
        public static StarhermitContent SingleUseStream(Stream stream, long? length, string contentType = "application/octet-stream")
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return new FactoryStreamContent(() => stream, length, contentType, canReplay: false);
        }

        private sealed class BytesContent : StarhermitContent
        {
            private readonly byte[] _bytes;

            internal BytesContent(byte[] bytes, string contentType) : base(contentType)
            {
                _bytes = bytes;
            }

            public override bool CanReplay => true;

            public override long? Length => _bytes.Length;

            public override byte[] ReadBytes() => _bytes;
        }

        private sealed class FactoryStreamContent : StarhermitContent
        {
            private readonly Func<Stream> _openStream;
            private readonly bool _canReplay;

            internal FactoryStreamContent(Func<Stream> openStream, long? length, string contentType, bool canReplay)
                : base(contentType)
            {
                _openStream = openStream;
                Length = length;
                _canReplay = canReplay;
            }

            public override bool CanReplay => _canReplay;

            public override long? Length { get; }

            public override Stream OpenStream() => _openStream();

            public override byte[] ReadBytes()
            {
                using var source = _openStream();
                using var buffer = new MemoryStream();
                source.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
    }
}
