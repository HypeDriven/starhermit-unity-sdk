using System;

namespace Starhermit
{
    /// <summary>Base type for every failure the SDK raises deliberately.</summary>
    public abstract class StarhermitException : Exception
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">Safe, human-readable description.</param>
        /// <param name="innerException">Underlying cause, when there is one.</param>
        protected StarhermitException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// A payload could not be read as the contract describes: malformed JSON, or a member whose type
    /// is not what the operation requires.
    /// </summary>
    /// <remarks>
    /// Unknown members and unknown enum strings are <em>not</em> failures - they are preserved. This
    /// is raised only when a value the SDK must understand is missing or of the wrong JSON type.
    /// </remarks>
    public sealed class StarhermitSerializationException : StarhermitException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">Description of the malformed payload.</param>
        /// <param name="innerException">Underlying cause, when there is one.</param>
        public StarhermitSerializationException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// The request never produced an HTTP response: DNS, TLS, connection, or a socket that dropped.
    /// </summary>
    /// <remarks>
    /// A transport failure is never reported as an API response, because "the server said no" and
    /// "the server was never reached" call for completely different handling by a game.
    /// </remarks>
    public class StarhermitTransportException : StarhermitException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">Safe description of the transport failure.</param>
        /// <param name="innerException">Underlying cause, when there is one.</param>
        public StarhermitTransportException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>A request exceeded its configured connect or response timeout.</summary>
    public sealed class StarhermitTimeoutException : StarhermitTransportException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">Safe description of what timed out.</param>
        /// <param name="timeout">The elapsed budget that was exhausted.</param>
        /// <param name="innerException">Underlying cause, when there is one.</param>
        public StarhermitTimeoutException(string message, TimeSpan timeout, Exception? innerException = null)
            : base(message, innerException)
        {
            Timeout = timeout;
        }

        /// <summary>The timeout that elapsed.</summary>
        public TimeSpan Timeout { get; }
    }

    /// <summary>
    /// A connection behaved in a way its protocol forbids: an unreadable frame, a frame larger than
    /// the negotiated cap, or a control message that arrived in the wrong state.
    /// </summary>
    public sealed class StarhermitProtocolException : StarhermitException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="message">Safe description of the protocol violation.</param>
        /// <param name="innerException">Underlying cause, when there is one.</param>
        public StarhermitProtocolException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// A capability this platform genuinely does not have was requested - a microphone on a headless
    /// server, a system browser on a locked-down console, a secure store nobody injected.
    /// </summary>
    /// <remarks>
    /// Every module compiles on every Unity target; absence surfaces here, at the call, with a stable
    /// <see cref="Reason"/> a game can branch on rather than as a missing type at build time.
    /// </remarks>
    public sealed class StarhermitFeatureUnavailableException : StarhermitException
    {
        /// <summary>Creates the exception.</summary>
        /// <param name="feature">The capability that was requested, for example <c>voice.capture</c>.</param>
        /// <param name="reason">Stable machine-readable reason, see <see cref="StarhermitFeatureReasons"/>.</param>
        /// <param name="message">Safe description for a human.</param>
        /// <param name="innerException">Underlying cause, when there is one.</param>
        public StarhermitFeatureUnavailableException(
            string feature,
            string reason,
            string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Feature = feature;
            Reason = reason;
        }

        /// <summary>The capability that is unavailable.</summary>
        public string Feature { get; }

        /// <summary>Stable reason code, safe to branch on across SDK versions.</summary>
        public string Reason { get; }
    }

    /// <summary>Stable reason codes carried by <see cref="StarhermitFeatureUnavailableException"/>.</summary>
    public static class StarhermitFeatureReasons
    {
        /// <summary>The running platform cannot provide the capability at all.</summary>
        public const string UnsupportedPlatform = "unsupported_platform";

        /// <summary>The capability needs an adapter the application did not supply.</summary>
        public const string AdapterNotConfigured = "adapter_not_configured";

        /// <summary>The user or operating system denied permission.</summary>
        public const string PermissionDenied = "permission_denied";

        /// <summary>No suitable device is present, such as a capture device on a server build.</summary>
        public const string DeviceUnavailable = "device_unavailable";

        /// <summary>The deployment disabled the feature server-side.</summary>
        public const string DisabledByServer = "disabled_by_server";
    }
}
