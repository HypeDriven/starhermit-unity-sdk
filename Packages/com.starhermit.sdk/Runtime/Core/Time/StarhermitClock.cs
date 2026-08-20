using System;
using System.Threading;

namespace Starhermit
{
    /// <summary>Source of the current time, so tests and platforms can supply their own.</summary>
    public interface IStarhermitClock
    {
        /// <summary>The current UTC time.</summary>
        DateTimeOffset UtcNow { get; }
    }

    /// <summary>The default clock, reading the operating system time.</summary>
    public sealed class SystemClock : IStarhermitClock
    {
        /// <summary>The shared instance.</summary>
        public static readonly SystemClock Instance = new SystemClock();

        private SystemClock()
        {
        }

        /// <inheritdoc />
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Tracks the offset between this device's clock and the server's.
    /// </summary>
    /// <remarks>
    /// A device clock can be wrong by hours, and a player can set it deliberately. Anything the game
    /// shows against a server deadline - an event window, a match timer, a token's remaining life -
    /// should be measured against <see cref="ServerNow"/>. The offset is advisory and never used to
    /// decide anything the server already decides.
    /// </remarks>
    public sealed class StarhermitServerClock
    {
        private readonly IStarhermitClock _deviceClock;
        private long _offsetTicks;
        private long _lastSyncedTicks;
        private long _roundTripTicks;

        /// <summary>Creates a server clock over a device clock.</summary>
        /// <param name="deviceClock">The local time source.</param>
        public StarhermitServerClock(IStarhermitClock deviceClock)
        {
            _deviceClock = deviceClock ?? throw new ArgumentNullException(nameof(deviceClock));
        }

        /// <summary>The device's own current time.</summary>
        public DateTimeOffset DeviceNow => _deviceClock.UtcNow;

        /// <summary>The device time corrected by the measured server offset.</summary>
        public DateTimeOffset ServerNow => _deviceClock.UtcNow + Offset;

        /// <summary>How far ahead of the device the server is. Zero until the first synchronisation.</summary>
        public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

        /// <summary>Round-trip time of the last synchronisation.</summary>
        public TimeSpan RoundTrip => TimeSpan.FromTicks(Interlocked.Read(ref _roundTripTicks));

        /// <summary>When the offset was last measured, or null if never.</summary>
        public DateTimeOffset? LastSynchronizedAt
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastSyncedTicks);
                return ticks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        /// <summary>How long ago the offset was measured, or null if never.</summary>
        public TimeSpan? Age
        {
            get
            {
                var last = LastSynchronizedAt;
                return last.HasValue ? _deviceClock.UtcNow - last.Value : (TimeSpan?)null;
            }
        }

        /// <summary>
        /// Records a server time reading. The request's own latency is halved and credited to the
        /// reading, which is the standard correction for a single round trip.
        /// </summary>
        /// <param name="serverTime">The instant the server reported.</param>
        /// <param name="sentAt">Device time when the request left.</param>
        /// <param name="receivedAt">Device time when the response arrived.</param>
        public void Synchronize(DateTimeOffset serverTime, DateTimeOffset sentAt, DateTimeOffset receivedAt)
        {
            var roundTrip = receivedAt - sentAt;
            if (roundTrip < TimeSpan.Zero) roundTrip = TimeSpan.Zero;
            var estimatedServerNow = serverTime + TimeSpan.FromTicks(roundTrip.Ticks / 2);
            Interlocked.Exchange(ref _offsetTicks, (estimatedServerNow - receivedAt).Ticks);
            Interlocked.Exchange(ref _roundTripTicks, roundTrip.Ticks);
            Interlocked.Exchange(ref _lastSyncedTicks, receivedAt.ToUniversalTime().Ticks);
        }
    }
}
