using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>Server time, and the clock offset the rest of the SDK measures from it.</summary>
    public sealed class StarhermitTimeClient : StarhermitServiceClient
    {
        private readonly StarhermitServerClock _clock;

        internal StarhermitTimeClient(StarhermitRestClient rest, StarhermitServerClock clock) : base(rest)
        {
            _clock = clock;
        }

        /// <summary>The server clock, corrected by the last measured offset.</summary>
        public StarhermitServerClock Clock => _clock;

        /// <summary>
        /// Reads the server's time and records the offset from this device's clock.
        /// </summary>
        /// <remarks>
        /// The request's own latency is halved and credited to the reading, and the device's clock is
        /// sent along so the deployment can report the skew it sees.
        /// </remarks>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>What the server reported.</returns>
        public async Task<StarhermitServerTime> SynchronizeAsync(CancellationToken cancellationToken = default)
        {
            var sentAt = Options.Clock.UtcNow;
            var request = Get("time")
                .WithCredential(StarhermitCredential.None)
                .WithQuery("clientTime", sentAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

            var json = await SendJsonAsync(request, "time.synchronize", cancellationToken).ConfigureAwait(false);
            var receivedAt = Options.Clock.UtcNow;

            var serverTime = json["serverTimeIso"].AsDateTimeOffsetOrNull()
                             ?? DateTimeOffset.FromUnixTimeMilliseconds(json["serverTime"].AsInt64OrDefault());

            _clock.Synchronize(serverTime, sentAt, receivedAt);

            return new StarhermitServerTime(
                serverTime,
                json["skew"].AsInt64OrNull(),
                receivedAt - sentAt,
                _clock.Offset);
        }
    }

    /// <summary>One reading of the server's clock.</summary>
    public readonly struct StarhermitServerTime
    {
        /// <summary>Creates a reading.</summary>
        /// <param name="serverTime">The instant the server reported.</param>
        /// <param name="reportedSkewMilliseconds">Skew the server measured against the client time sent.</param>
        /// <param name="roundTrip">How long the request took.</param>
        /// <param name="offset">The offset the SDK now applies to the device clock.</param>
        public StarhermitServerTime(
            DateTimeOffset serverTime,
            long? reportedSkewMilliseconds,
            TimeSpan roundTrip,
            TimeSpan offset)
        {
            ServerTime = serverTime;
            ReportedSkewMilliseconds = reportedSkewMilliseconds;
            RoundTrip = roundTrip;
            Offset = offset;
        }

        /// <summary>The instant the server reported.</summary>
        public DateTimeOffset ServerTime { get; }

        /// <summary>Skew the server measured, in milliseconds, when a client time was sent.</summary>
        public long? ReportedSkewMilliseconds { get; }

        /// <summary>Round-trip time of the reading.</summary>
        public TimeSpan RoundTrip { get; }

        /// <summary>The offset now applied to the device clock.</summary>
        public TimeSpan Offset { get; }
    }
}
