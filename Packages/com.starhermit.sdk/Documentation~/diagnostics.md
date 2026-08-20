# Diagnostics

## Logging

```csharp
options.Logger = new UnityStarhermitLogger();
options.LogLevel = StarhermitLogLevel.Warning;   // None, Error, Warning, Info, Debug
```

`Debug` logs every request and frame and is not for a shipped build. Everything reaching a logger has
already been redacted, so log lines are safe to persist or upload.

## Telemetry

Nothing is collected unless you install a sink:

```csharp
options.Telemetry = myTelemetrySink;   // IStarhermitTelemetrySink
```

Each event carries an operation id (`chat.sendMessage`), a duration, the status family, a retry count,
the server's request id, and an outcome. No URLs, no bodies, no player content.

## The diagnostics snapshot

```csharp
var snapshot = client.GetDiagnostics();
```

Safe to render in a debug overlay or attach to a support ticket: session presence and account id,
access-token expiry (a time, not a token), the measured server-clock offset and its age, per-connection
state, queue depth, reconnect attempts and last activity, requests in flight, retries spent, and the
last error - already redacted.

## Request ids

Every `StarhermitApiException` carries `RequestId` when the deployment returns one, and telemetry
records the same id. Quoting it in a bug report lets an operator find the exact request in the server
log without anyone pasting a token into a ticket.

## Server time

Device clocks are wrong, sometimes deliberately. Measure against the server's:

```csharp
await client.Time.SynchronizeAsync();
var serverNow = client.ServerClock.ServerNow;   // device time + measured offset
var freshness = client.ServerClock.Age;         // how long ago the offset was measured
```

The offset is advisory. Nothing the server decides is re-decided from it.

## When a connection misbehaves

`StateChanged`, `Closed` and `Faulted` on every connection report exactly what happened, including the
peer's close code. Reconnection stops for good on an authorization or policy close - repeating a
refusal only makes a rate limit worse - and the state settles on `Faulted` so a game can tell "still
trying" from "this will not recover".
