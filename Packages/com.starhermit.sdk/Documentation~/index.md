# Starhermit Unity SDK

Typed, asynchronous access to the Starhermit platform from Unity: authentication, profile, friends,
chat, voice, catalog, entitlements, cloud saves, achievements, leaderboards, authoritative games,
realtime rooms, peer relay and publishing.

## Install

Unity Package Manager, by Git URL, local path, or a scoped registry. The package modifies no project
settings on import and performs no network access at import time.

Minimum editor: Unity 2021.3 LTS. API compatibility level: .NET Standard 2.1.

## The shape of it

```csharp
var client = StarhermitClient.Create(new StarhermitOptions
{
    ApiBaseUri = new Uri("https://api.starhermit.com/api/v1/"),
    GameSlug = "chess",
    TokenStore = myPlatformSecureStore,
    LogLevel = StarhermitLogLevel.Warning
});

await client.InitializeAsync();          // loads a stored session; refreshes it if it has expired
var profile = await client.Me.GetProfileAsync();
```

`Create` performs no I/O at all. Nothing contacts the network until you initialise, call a service, or
open a connection.

The client owns every typed service:

`Auth`, `Me`, `Friends`, `Chat`, `Voice`, `Software`, `Entitlements`, `Activity`, `Ratings`,
`Wishlist`, `CloudSaves`, `Achievements`, `Leaderboards`, `Games`, `GameServer`, `RealtimeRooms`,
`Relay`, `BrowserGames`, `Publishers`, `Time`, `Raw`.

and creates the six connections:

```csharp
using var chat = client.CreateChatConnection();
using var game = client.CreateGameConnection(sessionId, "chess", useLaunchToken: true);
using var room = client.CreateRealtimeConnection(roomId, "chess");
using var relay = client.CreateRelayConnection(relaySessionId, titleId);
using var voice = client.CreateVoiceConnection(voiceRoomId);
using var upload = client.CreateBundleUploadConnection(browserGameId);
```

Disposing the client closes every connection it handed out, cancels in-flight requests, stops
heartbeats and releases audio.

## Async and threading

Every I/O operation returns `Task` and takes a trailing `CancellationToken`. There are no `async void`
methods in the package, and no public method blocks the calling thread.

Events and progress callbacks are posted to the synchronization context the client was created on -
Unity's main thread, if you create it there - so a handler may touch Unity objects directly. A
dedicated server can skip that hop:

```csharp
options.CallbackDispatcher = ImmediateCallbackDispatcher.Instance;
```

Callbacks are ordered per connection, and a handler that throws is reported to diagnostics without
stopping the receive loop.

## Errors

A non-success response throws a typed `StarhermitApiException`:

| Status | Exception |
| --- | --- |
| 400 / 422 | `StarhermitBadRequestException`, `StarhermitValidationException` |
| 401 | `StarhermitAuthenticationException` |
| 402 | `StarhermitEntitlementException` |
| 403 | `StarhermitAuthorizationException` |
| 404 | `StarhermitNotFoundException` |
| 409 | `StarhermitConflictException` |
| 429 | `StarhermitRateLimitException` (carries `RetryAfter`) |
| 5xx | `StarhermitServerException` |

A request that never reached the API throws `StarhermitTransportException` (or
`StarhermitTimeoutException`) instead - never a fake API error. Cancellation always surfaces as
`OperationCanceledException`. A capability the platform genuinely lacks raises
`StarhermitFeatureUnavailableException` with a stable `Reason` you can branch on.

## Retries and refresh

Retries are bounded, jittered, and limited to failures a second attempt could survive: connection
errors, timeouts, `408`, `429`, and transient `5xx`. `403`, `404`, `409` and validation failures are
never retried. A POST is not retried unless the endpoint documents an idempotency guarantee or you
supply a key with `AsIdempotent`.

A `401` triggers at most one coordinated refresh and one replay. Concurrent callers join the same
refresh rather than starting several - with a rotating refresh token, a second exchange would revoke
the family and sign the player out.

## Pagination

List endpoints return `StarhermitPage<T>` with the server's own paging metadata, and offer lazy
enumeration:

```csharp
await foreach (var title in client.Software.EnumerateTitlesAsync(query))
{
    // one request per page, fetched only as you consume it
}
```

## Forward compatibility

Models keep the JSON they were read from. A field the deployment shipped after this SDK version is
still reachable:

```csharp
var value = profile.RawJson["shippedAfterThisSdk"].AsStringOrNull();
```

An endpoint this version does not type is reachable through `client.Raw`, with the same credentials,
retries and redaction as a typed call.

## Further reading

- [platforms.md](platforms.md) - adapters, WebGL, consoles, headless servers, IL2CPP and stripping
- [security.md](security.md) - credentials, storage, redaction, and what the SDK refuses to do
- [diagnostics.md](diagnostics.md) - logging, telemetry, request ids, and reading a bug report
- [api-coverage.md](api-coverage.md) - every API operation and the SDK method that covers it
