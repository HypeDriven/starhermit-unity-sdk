using System;

namespace Starhermit
{
    /// <summary>How much the SDK writes to its logger.</summary>
    public enum StarhermitLogLevel
    {
        /// <summary>Log nothing at all.</summary>
        None = 0,

        /// <summary>Only failures the application should know about.</summary>
        Error = 1,

        /// <summary>Failures and recoverable problems such as a retry or a reconnect.</summary>
        Warning = 2,

        /// <summary>Lifecycle events: sessions, connections, deliberate state changes.</summary>
        Info = 3,

        /// <summary>Per-request and per-frame detail. Never enable in a shipped build.</summary>
        Debug = 4
    }

    /// <summary>Sink for the SDK's diagnostic messages.</summary>
    /// <remarks>
    /// Messages reaching a logger have already passed through <see cref="StarhermitRedactor"/>, so an
    /// implementation may persist or upload them without leaking credentials.
    /// </remarks>
    public interface IStarhermitLogger
    {
        /// <summary>Writes one message.</summary>
        /// <param name="level">Severity of the message.</param>
        /// <param name="message">Redacted message text.</param>
        /// <param name="exception">Associated exception, when there is one.</param>
        void Log(StarhermitLogLevel level, string message, Exception? exception = null);
    }

    /// <summary>A logger that discards everything. The default.</summary>
    public sealed class NullStarhermitLogger : IStarhermitLogger
    {
        /// <summary>The shared instance.</summary>
        public static readonly NullStarhermitLogger Instance = new NullStarhermitLogger();

        private NullStarhermitLogger()
        {
        }

        /// <inheritdoc />
        public void Log(StarhermitLogLevel level, string message, Exception? exception = null)
        {
        }
    }

    /// <summary>A logger that forwards to a delegate, for tests and custom sinks.</summary>
    public sealed class DelegateStarhermitLogger : IStarhermitLogger
    {
        private readonly Action<StarhermitLogLevel, string, Exception?> _write;

        /// <summary>Creates the logger.</summary>
        /// <param name="write">Receives every message.</param>
        public DelegateStarhermitLogger(Action<StarhermitLogLevel, string, Exception?> write)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        /// <inheritdoc />
        public void Log(StarhermitLogLevel level, string message, Exception? exception = null) =>
            _write(level, message, exception);
    }

    /// <summary>Filters messages by level before they reach the configured logger.</summary>
    internal sealed class LevelFilteredLogger
    {
        private readonly IStarhermitLogger _inner;
        private readonly StarhermitLogLevel _level;

        internal LevelFilteredLogger(IStarhermitLogger inner, StarhermitLogLevel level)
        {
            _inner = inner;
            _level = level;
        }

        internal bool IsEnabled(StarhermitLogLevel level) => level <= _level && level != StarhermitLogLevel.None;

        internal void Log(StarhermitLogLevel level, string message, Exception? exception = null)
        {
            if (!IsEnabled(level)) return;
            try
            {
                _inner.Log(level, message, exception);
            }
            catch (Exception)
            {
                // A logger that throws must not take down the request that was being logged.
            }
        }
    }
}
