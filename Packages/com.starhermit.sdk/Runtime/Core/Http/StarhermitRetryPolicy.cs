using System;
using System.Globalization;
using System.Threading;

namespace Starhermit
{
    /// <summary>
    /// Decides whether a failed attempt is repeated, and how long to wait first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only failures that could plausibly succeed on a second try are eligible: a connection that
    /// never landed, a timeout, <c>408</c>, <c>429</c>, and the transient <c>5xx</c> family. A
    /// <c>403</c>, <c>404</c>, <c>409</c> or validation failure is a decision, not a hiccup, and
    /// repeating it only wastes the player's battery.
    /// </para>
    /// <para>
    /// Two further gates apply before any of that: the request must be idempotent (a POST has to opt
    /// in, with an endpoint guarantee or an idempotency key), and its body must be replayable.
    /// </para>
    /// </remarks>
    public class StarhermitRetryPolicy
    {
        private readonly Random _random = new Random();
        private readonly object _randomGate = new object();

        /// <summary>A policy with the SDK's defaults.</summary>
        public static StarhermitRetryPolicy Default { get; } = new StarhermitRetryPolicy();

        /// <summary>A policy that never retries.</summary>
        public static StarhermitRetryPolicy None { get; } = new StarhermitRetryPolicy { MaxAttempts = 1 };

        /// <summary>Total attempts, including the first. Defaults to 3.</summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>Delay before the first retry, doubled for each subsequent one.</summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>Ceiling on the computed backoff, before any <c>Retry-After</c> is applied.</summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Random spread applied to each delay, as a fraction of it. Without jitter every client that
        /// saw the same outage retries in lockstep and rebuilds the spike that caused it.
        /// </summary>
        public double JitterFactor { get; set; } = 0.25;

        /// <summary>Whether a server's <c>Retry-After</c> overrides the computed backoff.</summary>
        public bool RespectRetryAfter { get; set; } = true;

        /// <summary>
        /// Longest <c>Retry-After</c> the SDK will actually wait. A longer one ends the attempt and is
        /// reported to the caller, because blocking a game for minutes inside one call is never right.
        /// </summary>
        public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Shared budget that caps retries across every client in the process, so a scene holding
        /// several clients cannot multiply one outage into a retry storm.
        /// </summary>
        public StarhermitRetryBudget Budget { get; set; } = StarhermitRetryBudget.Shared;

        /// <summary>Decides whether to retry after a failed attempt.</summary>
        /// <param name="attempt">1-based number of the attempt that just failed.</param>
        /// <param name="outcome">What the attempt produced.</param>
        /// <param name="delay">How long to wait before the next attempt.</param>
        /// <returns>True when the request should be sent again.</returns>
        public virtual bool ShouldRetry(int attempt, StarhermitAttemptOutcome outcome, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;
            if (attempt >= MaxAttempts) return false;
            if (!outcome.IsReplayable) return false;
            if (!IsTransient(outcome)) return false;

            if (RespectRetryAfter && outcome.RetryAfter.HasValue)
            {
                if (outcome.RetryAfter.Value > MaxRetryAfter) return false;
                delay = outcome.RetryAfter.Value;
            }
            else
            {
                delay = ComputeBackoff(attempt);
            }

            if (!Budget.TryConsume()) return false;
            return true;
        }

        /// <summary>True when the outcome is the kind of failure a second attempt could survive.</summary>
        /// <param name="outcome">What the attempt produced.</param>
        /// <returns>True when the failure is transient.</returns>
        protected virtual bool IsTransient(StarhermitAttemptOutcome outcome)
        {
            if (outcome.TimedOut || outcome.TransportFailed) return true;
            switch (outcome.Status)
            {
                case 408:
                case 429:
                case 500:
                case 502:
                case 503:
                case 504:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Computes the jittered exponential backoff for an attempt.</summary>
        /// <param name="attempt">1-based number of the attempt that just failed.</param>
        /// <returns>How long to wait.</returns>
        protected TimeSpan ComputeBackoff(int attempt)
        {
            var exponent = Math.Min(attempt - 1, 16);
            var scaled = BaseDelay.TotalMilliseconds * Math.Pow(2, exponent);
            var capped = Math.Min(scaled, MaxDelay.TotalMilliseconds);
            double jitter;
            lock (_randomGate) jitter = (_random.NextDouble() * 2 - 1) * JitterFactor;
            var withJitter = capped * (1 + jitter);
            return TimeSpan.FromMilliseconds(Math.Max(0, withJitter));
        }

        /// <summary>Parses a <c>Retry-After</c> header in either seconds or HTTP-date form.</summary>
        /// <param name="headerValue">The header value.</param>
        /// <param name="now">Current time, used for the HTTP-date form.</param>
        /// <returns>The wait, or null when the header is absent or unreadable.</returns>
        public static TimeSpan? ParseRetryAfter(string? headerValue, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(headerValue)) return null;

            if (double.TryParse(headerValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);

            if (DateTimeOffset.TryParse(
                    headerValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var when))
            {
                var delta = when - now;
                return delta <= TimeSpan.Zero ? TimeSpan.Zero : delta;
            }

            return null;
        }
    }

    /// <summary>What one attempt produced, as the retry policy sees it.</summary>
    public readonly struct StarhermitAttemptOutcome
    {
        /// <summary>Creates an outcome.</summary>
        /// <param name="status">HTTP status, or 0 when no response arrived.</param>
        /// <param name="transportFailed">True when the request never produced a response.</param>
        /// <param name="timedOut">True when the attempt exceeded its budget.</param>
        /// <param name="isReplayable">True when the request may be sent again at all.</param>
        /// <param name="retryAfter">Server-requested wait, when one was supplied.</param>
        public StarhermitAttemptOutcome(
            int status,
            bool transportFailed,
            bool timedOut,
            bool isReplayable,
            TimeSpan? retryAfter)
        {
            Status = status;
            TransportFailed = transportFailed;
            TimedOut = timedOut;
            IsReplayable = isReplayable;
            RetryAfter = retryAfter;
        }

        /// <summary>HTTP status, or 0 when no response arrived.</summary>
        public int Status { get; }

        /// <summary>True when no response was obtained at all.</summary>
        public bool TransportFailed { get; }

        /// <summary>True when the attempt timed out.</summary>
        public bool TimedOut { get; }

        /// <summary>True when the request is idempotent and its body can be produced again.</summary>
        public bool IsReplayable { get; }

        /// <summary>The server's requested wait, when it sent one.</summary>
        public TimeSpan? RetryAfter { get; }
    }

    /// <summary>
    /// A token bucket that caps how many retries the process performs per second, however many clients
    /// or sockets are running.
    /// </summary>
    public sealed class StarhermitRetryBudget
    {
        private readonly object _gate = new object();
        private readonly double _tokensPerSecond;
        private readonly double _capacity;
        private readonly IStarhermitClock _clock;
        private double _tokens;
        private DateTimeOffset _lastRefill;

        /// <summary>The process-wide budget used by default.</summary>
        public static StarhermitRetryBudget Shared { get; } = new StarhermitRetryBudget(30, 60);

        /// <summary>A budget that never refuses a retry, for tests.</summary>
        public static StarhermitRetryBudget Unlimited { get; } = new StarhermitRetryBudget(double.MaxValue, double.MaxValue);

        /// <summary>Creates a budget.</summary>
        /// <param name="capacity">Retries available in a burst.</param>
        /// <param name="tokensPerSecond">Sustained retries per second.</param>
        /// <param name="clock">Time source.</param>
        public StarhermitRetryBudget(double capacity, double tokensPerSecond, IStarhermitClock? clock = null)
        {
            _capacity = capacity;
            _tokensPerSecond = tokensPerSecond;
            _clock = clock ?? SystemClock.Instance;
            _tokens = capacity;
            _lastRefill = _clock.UtcNow;
        }

        /// <summary>Takes one retry from the budget.</summary>
        /// <returns>True when a retry is allowed right now.</returns>
        public bool TryConsume()
        {
            lock (_gate)
            {
                var now = _clock.UtcNow;
                var elapsed = (now - _lastRefill).TotalSeconds;
                if (elapsed > 0)
                {
                    _tokens = Math.Min(_capacity, _tokens + elapsed * _tokensPerSecond);
                    _lastRefill = now;
                }

                if (_tokens < 1) return false;
                _tokens -= 1;
                return true;
            }
        }
    }
}
