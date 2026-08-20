using System;
using System.Collections.Generic;

namespace Starhermit
{
    /// <summary>
    /// Receives safe, structured events about SDK activity.
    /// </summary>
    /// <remarks>
    /// The SDK collects nothing by default and sends nothing anywhere. When an application installs a
    /// sink it receives only what is listed on <see cref="StarhermitTelemetryEvent"/> - never a URL
    /// with a signed query, a body, a header, or anything a player typed.
    /// </remarks>
    public interface IStarhermitTelemetrySink
    {
        /// <summary>Records one event.</summary>
        /// <param name="telemetryEvent">The event to record.</param>
        void Record(StarhermitTelemetryEvent telemetryEvent);
    }

    /// <summary>One thing the SDK did, described without any payload or credential.</summary>
    public readonly struct StarhermitTelemetryEvent
    {
        /// <summary>Creates an event.</summary>
        /// <param name="name">Stable event name, for example <c>rest.request</c>.</param>
        /// <param name="operationId">The SDK operation that produced it, for example <c>chat.sendMessage</c>.</param>
        /// <param name="duration">How long the operation took.</param>
        /// <param name="statusFamily">HTTP status family - 2, 4, 5 - or 0 when there was no response.</param>
        /// <param name="retryCount">How many retries were spent.</param>
        /// <param name="requestId">Server correlation id, when one was returned.</param>
        /// <param name="outcome">Whether the operation succeeded, failed or was cancelled.</param>
        public StarhermitTelemetryEvent(
            string name,
            string operationId,
            TimeSpan duration,
            int statusFamily,
            int retryCount,
            string? requestId,
            StarhermitOperationOutcome outcome)
        {
            Name = name;
            OperationId = operationId;
            Duration = duration;
            StatusFamily = statusFamily;
            RetryCount = retryCount;
            RequestId = requestId;
            Outcome = outcome;
        }

        /// <summary>Stable event name.</summary>
        public string Name { get; }

        /// <summary>The SDK operation that produced the event.</summary>
        public string OperationId { get; }

        /// <summary>How long it took.</summary>
        public TimeSpan Duration { get; }

        /// <summary>HTTP status family, or 0 when no response arrived.</summary>
        public int StatusFamily { get; }

        /// <summary>Retries spent on the operation.</summary>
        public int RetryCount { get; }

        /// <summary>Server correlation id, when the deployment returned one.</summary>
        public string? RequestId { get; }

        /// <summary>How the operation ended.</summary>
        public StarhermitOperationOutcome Outcome { get; }
    }

    /// <summary>How an SDK operation ended.</summary>
    public enum StarhermitOperationOutcome
    {
        /// <summary>Completed successfully.</summary>
        Success = 0,

        /// <summary>The API refused it.</summary>
        ApiError = 1,

        /// <summary>No response was obtained.</summary>
        TransportError = 2,

        /// <summary>The caller cancelled it.</summary>
        Cancelled = 3
    }

    /// <summary>
    /// A point-in-time view of what the client is doing, for a debug overlay or a bug report.
    /// </summary>
    /// <remarks>
    /// Everything here is safe to display and to attach to a support ticket: no tokens, no bodies, no
    /// addresses with signatures in them. Token expiry is a time, not a token.
    /// </remarks>
    public sealed class StarhermitDiagnosticsSnapshot
    {
        /// <summary>Creates a snapshot.</summary>
        /// <param name="capturedAt">When the snapshot was taken.</param>
        /// <param name="hasSession">Whether an account session is loaded.</param>
        /// <param name="userId">The signed-in account, when there is one.</param>
        /// <param name="accessTokenExpiresAt">When the access token expires.</param>
        /// <param name="serverClockOffset">Measured offset between device and server clocks.</param>
        /// <param name="serverClockAge">How long ago the clock offset was measured.</param>
        /// <param name="connections">State of every live connection.</param>
        /// <param name="inFlightRequests">Requests currently in flight.</param>
        /// <param name="retriesSpent">Retries spent since the client was created.</param>
        /// <param name="lastError">The last failure, already redacted.</param>
        public StarhermitDiagnosticsSnapshot(
            DateTimeOffset capturedAt,
            bool hasSession,
            Guid? userId,
            DateTimeOffset? accessTokenExpiresAt,
            TimeSpan serverClockOffset,
            TimeSpan? serverClockAge,
            IReadOnlyList<StarhermitConnectionDiagnostics> connections,
            int inFlightRequests,
            int retriesSpent,
            string? lastError)
        {
            CapturedAt = capturedAt;
            HasSession = hasSession;
            UserId = userId;
            AccessTokenExpiresAt = accessTokenExpiresAt;
            ServerClockOffset = serverClockOffset;
            ServerClockAge = serverClockAge;
            Connections = connections;
            InFlightRequests = inFlightRequests;
            RetriesSpent = retriesSpent;
            LastError = lastError;
        }

        /// <summary>When the snapshot was taken.</summary>
        public DateTimeOffset CapturedAt { get; }

        /// <summary>True when an account session is loaded.</summary>
        public bool HasSession { get; }

        /// <summary>The signed-in account, when there is one.</summary>
        public Guid? UserId { get; }

        /// <summary>When the current access token expires.</summary>
        public DateTimeOffset? AccessTokenExpiresAt { get; }

        /// <summary>How far ahead of the device the server's clock is.</summary>
        public TimeSpan ServerClockOffset { get; }

        /// <summary>How long ago the clock offset was measured, or null if never.</summary>
        public TimeSpan? ServerClockAge { get; }

        /// <summary>State of every connection the client owns.</summary>
        public IReadOnlyList<StarhermitConnectionDiagnostics> Connections { get; }

        /// <summary>How many requests are in flight right now.</summary>
        public int InFlightRequests { get; }

        /// <summary>Retries spent since the client was created.</summary>
        public int RetriesSpent { get; }

        /// <summary>The last error, redacted and safe to display.</summary>
        public string? LastError { get; }
    }

    /// <summary>Diagnostics for one live connection.</summary>
    public sealed class StarhermitConnectionDiagnostics
    {
        /// <summary>Creates connection diagnostics.</summary>
        /// <param name="name">Which connection this is, for example <c>chat</c>.</param>
        /// <param name="state">Its current state.</param>
        /// <param name="outboundQueueDepth">Messages waiting to be sent.</param>
        /// <param name="reconnectAttempts">Reconnect attempts since the last clean connection.</param>
        /// <param name="lastActivityAt">When traffic last moved in either direction.</param>
        public StarhermitConnectionDiagnostics(
            string name,
            StarhermitConnectionState state,
            int outboundQueueDepth,
            int reconnectAttempts,
            DateTimeOffset? lastActivityAt)
        {
            Name = name;
            State = state;
            OutboundQueueDepth = outboundQueueDepth;
            ReconnectAttempts = reconnectAttempts;
            LastActivityAt = lastActivityAt;
        }

        /// <summary>Which connection this describes.</summary>
        public string Name { get; }

        /// <summary>Its current state.</summary>
        public StarhermitConnectionState State { get; }

        /// <summary>Messages queued for sending.</summary>
        public int OutboundQueueDepth { get; }

        /// <summary>Reconnect attempts since the last clean connection.</summary>
        public int ReconnectAttempts { get; }

        /// <summary>When traffic last moved.</summary>
        public DateTimeOffset? LastActivityAt { get; }
    }
}
