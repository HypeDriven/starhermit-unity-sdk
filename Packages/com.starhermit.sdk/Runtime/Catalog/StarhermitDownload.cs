using System;
using System.Globalization;
using System.IO;

namespace Starhermit
{
    /// <summary>
    /// An open download from a signed origin, with what the origin said about resuming.
    /// </summary>
    /// <remarks>
    /// The origin decides whether a range request is honoured. <see cref="IsResumed"/> reports what it
    /// actually did, which is the flag a caller must check before appending: a <c>200</c> in answer to
    /// a range request means the whole file is coming again, and appending it to a partial one would
    /// produce a corrupt archive that passes every length check.
    /// </remarks>
    public sealed class StarhermitDownload : IDisposable
    {
        private readonly StarhermitApiResponse _response;

        internal StarhermitDownload(StarhermitApiResponse response, long requestedOffset)
        {
            _response = response;
            RequestedOffset = requestedOffset;
            IsResumed = requestedOffset > 0 && response.Status == 206;
            SupportsResume = IsResumed ||
                             string.Equals(response.Header("Accept-Ranges"), "bytes", StringComparison.OrdinalIgnoreCase);
            TotalLength = ReadTotalLength(response);
        }

        /// <summary>The body to read. Never null while the download is open.</summary>
        public Stream Content => _response.BodyStream ?? Stream.Null;

        /// <summary>The offset the caller asked to resume from.</summary>
        public long RequestedOffset { get; }

        /// <summary>
        /// True when the origin honoured the range request and this stream continues from
        /// <see cref="RequestedOffset"/>. False means the stream starts at byte zero.
        /// </summary>
        public bool IsResumed { get; }

        /// <summary>True when the origin advertises byte ranges, so a future attempt may resume.</summary>
        public bool SupportsResume { get; }

        /// <summary>Total size of the complete file when the origin reported it.</summary>
        public long? TotalLength { get; }

        /// <summary>HTTP status the origin answered with.</summary>
        public int Status => _response.Status;

        /// <inheritdoc />
        public void Dispose() => _response.Dispose();

        private static long? ReadTotalLength(StarhermitApiResponse response)
        {
            // A partial response reports the whole size after the slash: "bytes 200-1023/1024".
            var contentRange = response.Header("Content-Range");
            if (!string.IsNullOrEmpty(contentRange))
            {
                var slash = contentRange!.LastIndexOf('/');
                if (slash >= 0 &&
                    long.TryParse(contentRange.Substring(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total))
                {
                    return total;
                }
            }

            var contentLength = response.Header("Content-Length");
            if (!string.IsNullOrEmpty(contentLength) &&
                long.TryParse(contentLength, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
            {
                return length;
            }

            return null;
        }
    }
}
