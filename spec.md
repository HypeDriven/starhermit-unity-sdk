# Starhermit Unity SDK

Status: **implemented**, version 0.1.0. This document describes what the package does today.

Package name: `com.starhermit.sdk`
Primary namespace: `Starhermit`
API baseline: Starhermit REST API v1 and WebSocket API v1, inventoried from the backend source on 2026-10-10

## 1. What this is

A Unity package that gives a game typed, asynchronous access to every public feature of the
Starhermit platform. It ships:

- Authentication (OAuth and public-key), token refresh, session persistence through an injected store,
  and account credential management.
- Typed REST clients covering the whole public API v1: player, social, catalog, game, publisher and
  browser-game publishing.
- Six WebSocket connections: chat, voice, peer relay, realtime rooms, authoritative game sessions, and
  streamed game uploads.
- Platform adapters for transport, sockets, storage, files, OAuth, signing, audio, clock, logging and
  telemetry, so one assembly serves desktop, mobile, WebGL, console, XR, embedded and headless server
  builds.
- High-level helpers (presence heartbeat, cloud-save synchroniser, message deduplicator) and a `Raw`
  client for endpoints a future deployment adds before the SDK types them.
- 195 tests (185 hermetic, 10 against a live deployment), eight samples, XML documentation on every
  public member, and a generated coverage manifest that fails the build when an API operation has no
  SDK mapping.

The SDK does not implement platform rules locally or weaken them. Authorization, friendship,
entitlement, room membership, score validation, game outcomes and storage budgets stay
server-authoritative.

## 2. Compatibility

- Minimum editor: Unity 2021.3 LTS. API compatibility level .NET Standard 2.1, language level C# 9.
- Scripting backends: Mono and IL2CPP. Managed stripping: Disabled through High, with `Runtime/link.xml`
  shipped in the package.
- No reflection-based construction, no dynamic code generation, no endianness or pointer-size
  assumptions, no mandatory native library.
- Installation by UPM Git URL, local path or scoped registry. Import modifies no project settings and
  performs no network access.
- REST base address defaults to `https://api.starhermit.com/api/v1/`. The WebSocket base is derived
  from it (`wss://<host>/ws/v1/`) unless configured.
- Every request sends `Accept: application/json`, `X-Starhermit-SDK-Version` and a descriptive
  `User-Agent`.

### 2.1 Platform capability matrix

| Capability | Desktop / mobile / console | WebGL | Headless server |
|---|---|---|---|
| REST | `UnityWebRequestTransport` | `UnityWebRequestTransport` | `HttpClientTransport` |
| WebSocket | `ClientWebSocketAdapter` | `WebGLSocketFactory` + `Plugins/WebGL/StarhermitWebSocket.jslib` | `ClientWebSocketAdapter` |
| OAuth | injected `IStarhermitOAuthBrowser` | injected browser adapter | URL handoff from the host |
| Token storage | injected store; `EncryptedFileTokenStore` opt-in | injected browser storage | injected store or memory |
| File transfer | `SystemFileStore` | injected sink | `SystemFileStore` |
| Voice capture / playback | `UnityMicrophoneCapture` / `UnityAudioPlayback` | injected browser media adapter | unavailable unless injected |
| Public-key signing | injected `IStarhermitSigner` | injected signer or browser crypto | injected signer |

Every module compiles for every target. A capability the platform genuinely lacks raises
`StarhermitFeatureUnavailableException` with a stable `Reason` at the call - absence of a microphone
in a dedicated server does not stop REST, chat, relay or game sessions from working.

## 3. Package layout

```text
Packages/com.starhermit.sdk/
  package.json
  Runtime/
    Starhermit.asmdef
    link.xml
    Core/            options, client, request pipeline, JSON, sessions, sockets, diagnostics
    Auth/            OAuth and public-key authentication
    Profile/         account, privacy, avatar, identities, keys, presence
    Social/          friends, chat, voice, chat and voice connections
    Catalog/         software, entitlements, activity, ratings, wishlist, cloud saves,
                     achievements, leaderboards
    Games/           game clients, dedicated-server client, game connection
    Realtime/        rooms, relay, and their connections
    Publishing/      publishers, browser games, upload connection
    PublishingTools/ Starhermit.Publishing.asmdef (optional workflow helpers)
    Platform/        transport, sockets, storage, crypto, browser, audio, Unity integration
  Editor/            settings asset tooling, build validation, CI build entry point
  Plugins/WebGL/     browser WebSocket bridge
  Tests/Runtime/     the NUnit suite and its generated coverage data
  Samples~/          eight samples
  Documentation~/    getting started, platforms, security, diagnostics, API coverage
```

The typed publisher clients live in the core assembly, so a project that excludes
`Starhermit.Publishing` loses only `StarhermitBuildPublisher` - the multi-step flow that requests
signed upload targets, uploads each asset and finalises the build - and not one API operation.

## 4. Programming model

### 4.1 Creating a client

```csharp
var client = StarhermitClient.Create(new StarhermitOptions
{
    ApiBaseUri = new Uri("https://api.starhermit.com/api/v1/"),
    GameSlug = "chess",
    TokenStore = platformSecureStore,
    LogLevel = StarhermitLogLevel.Warning
});

await client.InitializeAsync(cancellationToken);
```

`Create` performs no I/O; it copies the options, validates them, and returns. `InitializeAsync` loads
any stored session and refreshes it when it has expired. No heartbeat starts and no socket opens until
asked.

Service properties: `Auth`, `Me`, `Friends`, `Chat`, `Voice`, `Software`, `Entitlements`, `Activity`,
`Ratings`, `Wishlist`, `CloudSaves`, `Achievements`, `Leaderboards`, `Games`, `GameServer`,
`RealtimeRooms`, `Relay`, `BrowserGames`, `Publishers`, `Time`, `Raw`.

Connection factories: `CreateChatConnection`, `CreateVoiceConnection`, `CreateGameConnection`,
`CreateRealtimeConnection`, `CreateRelayConnection`, `CreateBundleUploadConnection`,
`CreateGameUploadConnection`.

`StarhermitClient` is `IDisposable`. Disposal cancels in-flight requests, closes every connection it
created, stops heartbeats, releases audio, and logs nothing sensitive. There is no static mutable
state anywhere in the package: two clients run side by side against different environments, and one
client survives scene loads.

### 4.2 Async and threading

- Every I/O operation returns `Task` and accepts a trailing `CancellationToken`.
- No `async void` methods. No public method blocks the calling thread.
- Events and progress callbacks are posted to the synchronization context captured at construction -
  Unity's main thread when created there. `ImmediateCallbackDispatcher` skips the hop for servers.
- Socket callbacks are ordered per connection; binary payloads preserve server order and bytes.
- A callback that throws is reported to diagnostics and does not stop the receive loop.

### 4.3 Results, errors and cancellation

Successful calls return typed models - except where the deployment answers `204` with no body, where
the call returns a plain `Task`: reading a model out of no body would hand back one made of empty
fields, indistinguishable from a real one. Declining a game or room invite, deleting a chat message and
deleting a game's whole settings document are among them. A non-success response throws `StarhermitApiException` carrying
HTTP status, the server's message, any machine-readable code, the request id, `Retry-After`, redacted
headers, and a size-capped redacted body. Typed subclasses:
`StarhermitBadRequestException`, `StarhermitValidationException` (field errors keyed by wire name),
`StarhermitAuthenticationException`, `StarhermitAuthorizationException`, `StarhermitNotFoundException`,
`StarhermitConflictException`, `StarhermitEntitlementException` (the API's `402`),
`StarhermitPreconditionFailedException` (`412`, with the version current now as `CurrentETag`),
`StarhermitQuotaExceededException` (`413` for one oversized payload, `507` for full account storage),
`StarhermitRateLimitException`, `StarhermitServerException`. A `507` is a quota, not an outage, so it
is never a server exception.

Limits are tuned per account and per game by the platform's operators, so a refusal names the number
in force rather than the SDK guessing it: `Limit`, `Used` and `LimitKey` on every API exception are
read from the body's `limit`, `used` and `limitKey`, and `ErrorCode` from its `code`
(`rate_limited`, `cloud_save_too_large`, `cloud_save_slots_exhausted`, `cloud_save_quota_exceeded`,
`no_public_key_session`, ...). `code` is also the name an OAuth authorization code travels under, so
the body is redacted before it is kept; the error code is read back from the raw body only when it has
the snake_case shape of an API error code, which a token never has.

Transport failures raise `StarhermitTransportException` / `StarhermitTimeoutException` and are never
dressed up as API responses. Protocol violations raise `StarhermitProtocolException`. Missing platform
capabilities raise `StarhermitFeatureUnavailableException`. Cancellation always surfaces as
`OperationCanceledException`.

### 4.4 Pagination and binary data

List endpoints return `StarhermitPage<T>` with the server's own `items`, total, page and page size -
the deployment spells the total `totalCount` on some routes and `total` on others, and both are read.
`EnumerateXAsync` methods expose `IAsyncEnumerable<T>` that fetches the next page only when consumed.

Downloads stream to an `IStarhermitFileStore` through a temporary file, verify a supplied SHA-256, and
are promoted atomically. `Software.OpenDownloadAsync` accepts a resume offset and reports what the
signed origin actually did: `IsResumed` is true only for a `206`, because appending a whole file to a
partial one would corrupt it while still passing a length check. Bundle uploads stream in chunks over the upload socket and are never buffered
whole. Byte-array overloads exist only where the API's own payloads are bounded (avatars, cover art,
cloud saves).

## 5. Configuration

`StarhermitOptions` carries: API and WebSocket addresses, game slug, request and connect timeouts,
retry policy, transport, socket factory, token store, OAuth browser, signer, clock, logger and log
level, telemetry sink, callback dispatcher, file store, audio capture and playback, token refresh
leeway, outbound queue and message size caps, diagnostic body cap, `AllowInsecureTransport`, and a
`User-Agent` suffix. Defaults are production-safe.

`StarhermitSettings` (a `ScriptableObject`) holds non-secret project defaults: addresses, slug, log
level, timeout, and the development flag. Tokens, refresh tokens, private keys, client secrets and
invoke keys are never serialised into an asset, a scene, `Resources`, a log, an exception message or a
build artifact.

`AllowInsecureTransport` permits `http`/`ws` for a development endpoint. Client construction refuses a
non-HTTPS address without it, and `StarhermitBuildValidation` fails a non-development Unity build that
still has it enabled. It never disables certificate validation.

## 6. Transport

### 6.1 REST

`UnityWebRequest` is the default inside Unity, `HttpClient` outside it, and both are replaceable.
JSON uses UTF-8 and the API's camel-case wire names. Timestamps are UTC `DateTimeOffset`; GUIDs use
canonical strings; integers are parsed from their source text so a 64-bit id keeps its exact value
through WebGL.

Retries use bounded exponential backoff with jitter and honour `Retry-After` up to a cap. Only
connection errors, timeouts, `408`, `429` and transient `5xx` are eligible, and only for idempotent
requests with replayable bodies. `403`, `404`, `409`, `412`, `413`, `507` and validation failures are
never retried. A POST
opts in through `AsIdempotent`. A process-wide `StarhermitRetryBudget` stops several clients turning
one outage into a retry storm.

### 6.2 Authentication coordination

One refresh runs per client; concurrent callers await it rather than starting their own. The rotated
pair is stored atomically before waiters resume. A definitive rejection clears the session and raises
`SessionExpired` exactly once; a transport failure preserves it. A `401` buys at most one coordinated
refresh and one replay, and a failed refresh surfaces the server's own message rather than a
substitute.

### 6.3 WebSockets

All six connections share `StarhermitConnection`: connect, graceful close, cancellation, message size
caps, bounded outbound queues with explicit backpressure, ordered sends, and the states
`Disconnected`, `Connecting`, `Connected`, `Reconnecting`, `Closing`, `Faulted`.

A browser cannot set handshake headers, so a socket's credential travels in its URL - the least
private part of a request. Every handshake therefore presents a **connection ticket** (`?ticket=`)
fetched immediately before it: single-use, valid for seconds, and only on `/ws`. The first connect and
every reconnect fetch their own, because the deployment spends a ticket on the handshake that presents
it. An account socket buys its ticket with the session (`Auth.IssueConnectionTicketAsync`); a socket
authorised by a launch token buys it with that token (`StarhermitGameClient.IssueConnectionTicketAsync`),
so the ticket carries the token's `game_scope` and reaches exactly what the token could. A ticketed
handshake sends no `Authorization` header: the ticket is its only credential.

Only a ticket endpoint answering `404` - a deployment that predates tickets - makes the handshake carry
the token instead, in the `Authorization` header and as `?access_token=`. Any other refusal of the
ticket (an expired session, a rate limit, an outage) fails the connect as the handshake itself would
have. A dedicated-server token never asks for a ticket: the deployment fences it to its game's
`/server/` routes, and a server has no browser between it and the header. Tickets and query tokens are
redacted from every log. A connect whose ticket fetch fails leaves the connection `Faulted`, so the
next `ConnectAsync` tries again.
Reconnection uses jittered backoff and stops for good
on authorization or policy closes. It never assumes membership survived: each protocol refetches or
rejoins in `OnReconnectedAsync`, and a failure there is logged rather than treated as a broken socket.

## 7. Authentication

`client.Auth` covers the whole `/auth` surface:

- `GetOAuthProvidersAsync` (anonymous: the providers the deployment offers, one button each, and
  whether each recognises an existing account by email), `BuildAuthorizeUri`, `SignInWithOAuthAsync`,
  `CompleteOAuthAsync`, `ConfirmIdentityLinkAsync`.
- `BeginPublicKeyRegistrationAsync`, `VerifyPublicKeyRegistrationAsync`, `RequestKeyRevocationAsync`,
  `ConfirmKeyRevocationAsync`.
- `RequestChallengeAsync`, `CompletePublicKeyAuthenticationAsync`, and `SignInWithPublicKeyAsync`
  which runs the whole flow through an injected `IStarhermitSigner`.
- `ExchangeRefreshTokenAsync`, `SignOutAsync`, `AdoptSessionAsync`.
- `IssueConnectionTicketAsync`: a single-use, `/ws`-only credential with the account session's
  authority, for a socket handshake that has to carry its credential in the URL.

Supported key types are `Ed25519`, `ECDSA-P256` and `RSA-PSS`. The SDK never generates or stores a
private key.

`StarhermitChallenge.CanonicalPayload` reproduces the exact bytes the server verifies. The deployment
verifies against its own .NET serialisation of the challenge (PascalCase member names in declaration
order) while the response arrives camel-cased, so re-serialising what was received would never verify.
The string members are escaped the way System.Text.Json's default encoder escapes them (`+`, `<`, `>`,
`&`, `'`, `` ` ``, `"` and anything outside printable ASCII as upper-case `\uXXXX`), while the two
timestamps are copied verbatim: the nonce is base64, so about three challenges in ten carry a `+`, and
signing it literally fails exactly those sign-ins.
This coupling is recorded in `contracts/backend-notes.md` as something the API should fix by returning
the bytes to sign.

`StarhermitSession` exposes user id, expiry, issue time, authentication method and permissions, read
from the access token without verifying it - a local convenience for expiry checks only. The refresh
token never appears in `ToString()`, a log, or telemetry.

## 8. Account, social and voice

- `client.Me`: profile read and partial update, the terms in force (anonymous, with the hash to
  accept) and terms acceptance, avatar upload and download, public profiles and avatars, linked
  identities, privacy settings, presence heartbeat (with a helper that pauses on suspension and sends
  immediately on resume), public-key listing, registration and revocation - including
  `RevokeCurrentPublicKeyAsync`, which a key-authenticated session uses to drop its own key and every
  session it produced - and entitlements.
- `client.Friends`: send, list, accept and decline requests; remove a friend; list friends with the
  presence the viewer is permitted to see.
- `client.Chat`: direct and group conversations, rename, invitations, joinable rooms, join, add and
  remove participants, leave, list, read markers, unread totals, paged messages, send, edit, delete.
- `StarhermitChatConnection`: live `new_message`, `message_updated`, `message_deleted`,
  `conversation_created`, `conversation_renamed`, `participants_added`, `participant_removed`,
  `conversation_read`, `chat_invite`, `chat_invite_responded` and `game_invite` events, plus an
  `UnknownEventReceived` fallback that preserves any frame a later deployment adds.
  `StarhermitMessageDeduplicator` matches socket and REST deliveries by the server's message id; the
  SDK never invents an id or an optimistic timestamp.
- `client.Voice`: create, list, read, join, leave, mute and close voice rooms, and the host's server
  mute of another participant, which that participant cannot lift.
- `StarhermitVoiceConnection`: binary audio frames stamped by the platform with a 16-byte sender id,
  `mute`, `speaking` and `rtc` control frames, and a PCM helper for the platform's fallback convention
  (20 ms, 16 kHz, mono, signed 16-bit). Muting changes server state, not local playback volume.

## 9. Catalog, ownership, activity and storage

- `client.Software`: search and page titles, read a title, claim a free one (`402` surfaces as
  `StarhermitEntitlementException`), page builds, start a launch, request a signed download URL, and
  download to the file store with checksum verification and atomic promotion. Assets whose scan status
  is not clean are exposed but flagged.
- `client.Entitlements`: list, and a convenience membership check.
- `client.Activity`: end launches, own and friends' playtime for catalog and external titles, external
  launch recording, game feed, and the personal, friends and public activity feeds; external-library
  link, unlink, owned-software paging and external launch.
- `client.Ratings` and `client.Wishlist`: upsert a rating with an optional review, bulk-query
  aggregates by game key, page reviews; idempotent wishlist add and remove.
- `client.CloudSaves`: metadata, download, upload, and file-based overloads. `TryDownloadAsync` reports
  absence as `null` rather than an error. Every stored save has a version (`ETag`), which
  `DownloadVersionAsync` returns from the same response as the bytes. An upload is unconditional
  unless it passes a `StarhermitSaveCondition` - `IfMatch(etag)`, `IfNoSaveExists`
  (`If-None-Match: *`), `IfAnySaveExists`, or `ForVersion(info)` - and a write that loses to another
  device throws `StarhermitPreconditionFailedException` instead of overwriting it.
  `DownloadIfChangedAsync(key, heldETag)` downloads only a version the caller does not already hold:
  it sends `If-None-Match`, and a `304` comes back as `NotModified` with no archive, in one request and
  without a metadata read first. The pipeline accepts a `304` only from a request that sent a validator;
  anywhere else it is an error, never an empty body. The account's
  size, slot and total limits surface as `StarhermitQuotaExceededException` (`413`/`507`) or
  `StarhermitConflictException` (`409`, slots), each naming the limit in force.
- `StarhermitCloudSaveSynchronizer` compares server metadata with a caller-owned sync marker and, when
  both sides changed, reports a conflict instead of picking a winner; `LocalWins`, `RemoteWins` and
  `Abort` are explicit policies. Every upload it makes names the version it compared against (or
  `If-None-Match: *` when there was none), so a write from another device in between is reported as
  `Conflict` with the server's current metadata - under `LocalWins` too, since that decision was about
  a version that no longer exists. A download is reported only with the metadata of the bytes it
  returns: when a write lands between the download and the metadata read, it reads again (three
  attempts) rather than hand back a marker for a save the device never saw. It does not download
  conditionally: its first request, the metadata read, already carries the server's version, and it
  downloads only when that version is not the one it compared against - by which point a `304` is
  impossible.
- `client.Achievements` and `client.Leaderboards`: unlocks, client-claimable unlock, definitions,
  paged entries with server-assigned ranks, and score submission where the definition permits it.

Game settings, cloud saves and game player state stay visibly separate: preferences in the settings
document, an opaque progression archive in cloud saves, and server-authoritative rating and history
read-only through the games API.

## 10. Authoritative games

`client.Games.ForSlug(slug)` returns a client covering game metadata and effective capabilities,
launch-token minting, session listing and reads, AI sessions, the game's match shapes
(`GetQueuesAsync`) and nearest-rating matchmaking for all of them or a named subset (enqueue, status
with how far the search has widened, cancel), invites (create, list, accept, decline), cross-game
invite inbox, replays (a game with replays disabled answers `404`, which is surfaced rather than
flattened to an empty list), control bindings, the schema-free player settings document
(whole-document get, replace, merge and delete, plus single-key operations), the game's leaderboards,
the caller's unlocks in another game (`GetLinkedAchievementsAsync`; a player who keeps achievements
private reads as hidden, not as having none), a session's recovery description (checkpoint size and
freshness, restore limits, transport budgets), leaving a persistent world, filing crash and bug reports
with attachments (`FileReportAsync`; the per-game daily cap is a `429` with `Retry-After`, size caps a
`413`) and reading the caller's own reports, and a connection ticket carrying the client's credential.
Server budgets are reported from the response rather than duplicated as SDK policy.

The same client carries the owner's tools that live on the game's route - `GetDiagnosticsAsync`
(sessions, script cost, matchmaking, webhook backlog, buffered writes) and webhook endpoint list,
create, delete and resume. Those always authenticate as the account, even from a launch-scoped client.
A webhook's signing secret is returned once, on create, and is redacted from logs by name.

`WithLaunchToken()` returns a client that authorises with the game-scoped launch token instead of the
account session; the backend's scope fence, not the SDK, decides what it may call. Minting a launch
token never replaces the account session.

`StarhermitGameConnection` attaches to `/ws/v1/games`, sends `{"type":"cmd","data":…}` commands
(including rate-limited realtime input), and raises `FrameReceived`, `AchievementUnlocked`,
`PresenceChanged`, `ErrorReceived` and an unknown-frame fallback. It does not tick game logic, predict
authoritative state, fabricate outcomes, or resend a possibly non-idempotent command after an
ambiguous disconnect.

`client.GameServer` exchanges a deployment refresh key for a server token and reads sessions with it.
The token lives in the scoped credential store, never with the account session.

## 11. Realtime rooms and peer relay

`client.RealtimeRooms`: create rooms with teams, seats, AI seats and backfill - and, through
`CreateRoomAsync(slug, StarhermitRoomSettings)`, a name, browser visibility, a cap on AI backfill and
join-in-progress; read the caller's active
room and every room they hold a seat in; browse listed rooms (`StarhermitRoomSummary`, which carries
no join code or roster), optionally including running matches with a vacant seat; join by code; list,
accept and decline invites; quick-join, optionally filtered (`StarhermitQuickJoinFilter`: team count,
seats per team, a metadata subset, and running matches that take players mid-match) with a miss
surfacing as `StarhermitNotFoundException`; read a room; invite; open for backfill; start; leave; assign
seats; submit results; and, as host, rename, list or unlist, change metadata and backfill
(`UpdateRoomAsync`, a compare-and-swap on the room's `Revision`) and put the roster into matchmaking as
a party. A room's `JoinCode` is a capability - holding it takes a seat - and is redacted from logs by
name; the deployment chooses it, and making a room quick-joinable is `OpenRoomAsync`, so neither is a
creation setting. The original `CreateRoomAsync` and `QuickJoinAsync` signatures are unchanged and send
exactly what they always did.

`StarhermitRealtimeConnection` attaches to `/ws/v1/realtime`, sends `chat`, `ready` and (host only)
`event` control frames, and receives binary payloads prefixed with the sender's 16-byte participant id
plus presence and roster frames. It refetches the room after a reconnect.

`client.Relay` lists, creates (bound to exactly one game session or realtime room), reads, joins and
closes relays. `StarhermitRelayConnection` carries opaque binary payloads verbatim in both directions
and rejoins after a reconnect. The SDK assumes no send rate; the deployment paces the connection from
the game's declaration and closes a connection that exceeds it.

## 12. Browser games and publishing

`client.BrowserGames`: submit a repository, claim, list own and all, transfer, delete, move to a
different URL keeping the game's id (`ChangeUrlAsync`), list removed games and restore an uploaded
one, icon and cover art, release notes, streamed bundle upload over HTTP, folder upload, audience
stats, hosting toggle, deployment pin and read, and the GitHub link state. `StarhermitBrowserGame`
reports `ServerRuntime` (`script`, `container`, or null for a browser-only game), which is the way to
ask whether a game has a backend - every game whose author is known has a slug.

For a game's owner it also covers: the server's live sessions and ending one; achievements and
leaderboards beside the game's own (`Origin` tells an owner-created achievement from one the script
declares, which only a redeploy changes; owner boards are filled by the game's server logic, never by a
client); resetting one player's rating; player crash and bug reports (paged list filtered by kind and
status, full report, attachment download, status change, delete); and the server container's output -
recent logs (live, or the last crash's when nothing runs) and paged crash reports with each crash's
final output.

`client.Publishers`: create a publisher, list memberships, add, remove and read members, create or
update titles, generate signed upload targets, finalise builds, download and launch analytics,
entitlement grant and revoke, and achievement and leaderboard definition CRUD.
`StarhermitBuildPublisher`, in the optional publishing assembly, runs the whole flow and finalises only
after every asset has uploaded, so an interrupted publish leaves the previous build serving players.
Signed storage targets are reached through `client.Pipeline.UploadSignedAsync`, which sends no session
credential to a storage host.

`StarhermitGameUploadConnection` implements the upload protocol: wait for the server's `ready` notice
(mode and byte allowance), stream binary chunks, observe `ack` and `progress` frames, then send
`{"type":"complete"}`. Nothing is published until that frame arrives, so a dropped connection, a
cancellation or an explicit `abort` leaves the live game untouched. An archive larger than the
server's stated allowance is refused before a byte is sent.

## 13. Server time

`client.Time.SynchronizeAsync()` reads the server clock, credits half the round trip to the reading,
and records the offset on `client.ServerClock`, which exposes `ServerNow`, `Offset`, `RoundTrip` and
`Age`. The offset is advisory; nothing the server decides is re-decided from it.

## 14. Security and privacy

- HTTPS and WSS are required outside an explicitly declared development environment.
- Redaction is structural, by header, query-parameter and JSON member name at every depth, so a
  credential the SDK has never seen is still removed - connection tickets, webhook secrets and room
  join codes included. URL fragments are dropped entirely.
- The package ships no store that claims to be secure. The default is in-memory;
  `EncryptedFileTokenStore` (AES-CBC with HMAC-SHA256 over an application-supplied key) is the opt-in
  fallback, documented as obfuscation at rest rather than a keychain. `PlayerPrefs` is never presented
  as secure.
- Account session, launch token, deployment key and server token are separate credential types in
  separate stores; none substitutes for another.
- Remote JSON never selects a CLR type, a file path or an object to activate. File paths cannot escape
  the store's root. Downloads and cloud saves are written to a temporary file and promoted atomically.
- Inbound frame sizes, outbound queues and diagnostic bodies are bounded locally even when the
  deployment permits more; overflow raises a typed error or closes with a documented code.
- No telemetry is collected by default. An injected sink receives event name, operation id, duration,
  status family, retry count, request id and outcome - never URLs, bodies or player content.
- Player-authored text and bytes are surfaced as such; the SDK never renders or executes them.

## 15. Reliability and lifecycle

`StarhermitLifecycle` bridges Unity's application events: presence pauses on suspension and sends
immediately on resume, and the client is disposed on quit without synchronous network work.
Connectivity is treated as advisory - calls are attempted and classified by their actual outcome.
Retries and reconnects share process-wide caps. `client.GetDiagnostics()` returns connection states,
queue depths, reconnect counts, token expiry, clock freshness, in-flight requests, retries spent and
the last redacted error.

## 16. Serialization

JSON is parsed into an immutable `JsonValue` tree by a hand-written reader and mapped by hand-written
codecs. There is no reflection anywhere in the runtime.

- Models are immutable and expose `RawJson`, so a member shipped after this SDK version is still
  readable.
- Unknown enum strings are preserved as strings rather than coerced; unknown privacy levels read as
  the most private interpretation rather than the most permissive.
- `Optional<T>` distinguishes omitted, explicit null and value for PATCH bodies.
- Absent members are `Missing` rather than `Null`, and an absent collection reads as empty.
- Socket frames dispatch on a type discriminator with an unknown-frame fallback that keeps the payload.
- Models reference no `GameObject`, `MonoBehaviour`, scene or editor type. `StarhermitTextures`
  converts avatar and cover-art bytes to Unity textures on the main thread, and documents that the
  caller owns and must destroy them.

## 17. Verification

### 17.1 What runs today

- **185 hermetic NUnit tests** covering the JSON layer, the request pipeline (routes, verbs, query, bodies,
  headers, credentials, response mapping, cancellation, typed errors and limit refusals, paging), retry
  eligibility and jitter bounds, refresh coordination and rotation persistence, redaction, socket
  machinery (ordering, backpressure, reconnection, policy closes, handler exceptions, a ticket per
  handshake and the fallback for a deployment without tickets), all six wire
  protocols, cloud-save conflict resolution and conditional writes, the player, owner and room
  operations' wire shapes, client lifecycle and model tolerance, and the coverage manifest.
  They run under `dotnet test` and, unchanged, as Unity EditMode tests.
- **Ten live contract tests** that read a real deployment when `STARHERMIT_TEST_BASE_URL` is set, and
  are skipped otherwise. Five read the anonymous surface (parsing, model mapping, paging metadata,
  clock synchronisation, error typing). Five more run signed in when `STARHERMIT_TEST_MAILBOX` also
  names the directory the deployment's mail lands in: they create an account the way a player does -
  register a key, redeem the emailed link, accept the terms - never by minting a token, then check
  public-key sign-in, a fresh single-use ticket per handshake (a replayed one is refused), a launch
  token's ticket reaching its own game's room and not another game's, room settings, declining a room
  invite (a `204`) and filtered quick-join across two accounts, and conditional cloud-save downloads. `tools/live-test.sh` stands up
  a throwaway backend from the checkout (Postgres, Redis, the Api and `tools/smtp_sink.py`), runs them,
  and removes it; all ten pass against it.
- **Three Unity compile-checks** (`build/unity/*.csproj`) type-check the Unity-only code - the
  `UnityWebRequest` transport, the WebGL bridge, settings, audio adapters, editor tooling and all
  eight samples - against small API stubs, on machines with no Unity licence.
- **An AOT compile-check** (`build/aot/Starhermit.AotCheck.csproj`, part of the solution build) compiles
  the runtime, Unity paths included, under the .NET trimming and AOT analyzers with warnings as errors.
  IL2CPP with High stripping fails on what they flag - reflection over unreferenced members, types named
  at runtime, generated code - so the hand-mapped design is enforced rather than claimed. A canary using
  `Activator`/`MakeGenericType` fails it (IL2055, IL2057, IL3050), which is how the check was proven live.
- **An AOT smoke build** (`build/aot-smoke`): the runtime published with Native AOT and full trimming -
  no JIT, every unreferenced member removed, the nearest thing to a stripped IL2CPP player that runs
  without an editor - driving a live deployment through registration by emailed link, eight public-key
  sign-ins, a ticketed socket and its reconnect, and versioned cloud saves (17 checks).
  `STARHERMIT_LIVE_AOT=1 tools/live-test.sh` runs it after the live suite; all 17 pass.
- **A generated coverage manifest**: `tools/generate_coverage.py` reads the backend's controllers and
  emits `contracts/coverage-manifest.json`, `Documentation~/api-coverage.md` and the data
  `ContractCoverageTests` enforces. Of 243 API operations, 227 are mapped to typed SDK methods, 6 are
  WebSocket routes served by connection classes, and 10 are classified as not-for-clients with reasons
  (browser-only pages, server-to-server callbacks, and the game host's nginx hooks). Zero are unmapped.
- **`tools/verify.sh`** runs the whole gate: build every project with warnings as errors and XML
  documentation required, run the suite, regenerate the manifest and fail on any drift.

### 17.2 What needs the licensed build farm

The CI workflow defines the editor matrix (2021.3 LTS, 2022.3 LTS, Unity 6) and IL2CPP player builds
for Linux, Android and WebGL with High stripping, gated on a `UNITY_LICENSE` secret. Those jobs have
not been run here. The AOT compile-check and smoke build above cover the failure modes stripping and
ahead-of-time compilation introduce, but not Unity's own player pipeline, its WebGL bridge or a device;
until the editor jobs have run, the platform matrix in §2.1 describes intended support rather than
qualified support, and no platform should be advertised as verified.

### 17.3 Not yet built

- The optional WebRTC voice adapter. The PCM fallback path is implemented; a WebRTC adapter would slot
  in behind the same interfaces.

## 18. Samples and documentation

Eight samples ship under `Samples~`: authentication and profile, friends and chat, matchmaking game,
realtime and relay, voice, catalog services, publisher tool, and dedicated server. All eight are
compiled by CI, so a sample cannot drift from the API it demonstrates.

`Documentation~` covers getting started and the threading, error and pagination model
(`index.md`), platform adapters, WebGL, consoles, headless servers and stripping (`platforms.md`),
credentials, storage, redaction and server authority (`security.md`), logging, telemetry, request ids
and the diagnostics snapshot (`diagnostics.md`), and the generated operation map
(`api-coverage.md`). Every public member carries XML documentation, enforced by the build.

## 19. Sources of truth and maintenance

Behaviour is derived, in priority order, from:

1. The deployed Starhermit API contract.
2. The backend running specification and source in `~/pi/dashboard/projects/starhermit`.
3. Public documentation at <https://wiki.starhermit.com/>.

The deployment's OpenAPI documents are currently empty (see `contracts/backend-notes.md`), so this SDK
derives its inventory from the backend's controllers instead. When the sources disagree, the deployed
contract wins for wire compatibility and the mismatch is reported upstream rather than worked around.

Any API change requires regenerating the coverage manifest, adjusting the affected clients and tests,
and releasing under semantic versioning: additive endpoints and fields are a minor release, a
source-breaking change is a major one.

## Keeping this document current

This file describes **what the SDK does today**, in the present tense. It is not a wishlist and not a
changelog.

- Every change that alters observable behaviour updates the affected section in the same change: a new
  or removed operation, a changed wire format, a new adapter or platform rule, a change to retry,
  refresh, redaction or reconnection behaviour, a new socket protocol or frame.
- Edit in place rather than appending. Version history belongs in `CHANGELOG.md`.
- Keep the *why* where it constrains future work - the challenge-payload casing in §7 and the credential
  separation in §14 are the kind of detail that stops someone "simplifying" a deliberate decision.
- Counts and coverage numbers (§1, §17) come from the generated manifest and the test run. Regenerate
  rather than guess.
- `CLAUDE.md` explains how to work in the repository; this file explains what the repository does. When
  a change belongs in both, write it in both. The code remains the source of truth for both.
