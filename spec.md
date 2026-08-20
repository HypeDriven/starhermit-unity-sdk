# Starhermit Unity SDK

Status: **implemented**, version 0.1.0. This document describes what the package does today.

Package name: `com.starhermit.sdk`
Primary namespace: `Starhermit`
API baseline: Starhermit REST API v1 and WebSocket API v1 as deployed on 2026-08-20

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
- 149 tests (144 hermetic, 5 against a live deployment), eight samples, XML documentation on every
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

Successful calls return typed models. A non-success response throws `StarhermitApiException` carrying
HTTP status, the server's message, any machine-readable code, the request id, `Retry-After`, redacted
headers, and a size-capped redacted body. Typed subclasses:
`StarhermitBadRequestException`, `StarhermitValidationException` (field errors keyed by wire name),
`StarhermitAuthenticationException`, `StarhermitAuthorizationException`, `StarhermitNotFoundException`,
`StarhermitConflictException`, `StarhermitEntitlementException` (the API's `402`),
`StarhermitRateLimitException`, `StarhermitServerException`.

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
requests with replayable bodies. `403`, `404`, `409` and validation failures are never retried. A POST
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

Credentials ride the `Authorization` header where the platform allows it and `?access_token=`
otherwise, because a browser cannot set handshake headers; the query token is redacted from every log.
Reconnection uses jittered backoff, re-acquires a current token before each attempt, and stops for good
on authorization or policy closes. It never assumes membership survived: each protocol refetches or
rejoins in `OnReconnectedAsync`, and a failure there is logged rather than treated as a broken socket.

## 7. Authentication

`client.Auth` covers the whole `/auth` surface:

- `BuildAuthorizeUri`, `SignInWithOAuthAsync`, `CompleteOAuthAsync`, `ConfirmIdentityLinkAsync`.
- `BeginPublicKeyRegistrationAsync`, `VerifyPublicKeyRegistrationAsync`, `RequestKeyRevocationAsync`,
  `ConfirmKeyRevocationAsync`.
- `RequestChallengeAsync`, `CompletePublicKeyAuthenticationAsync`, and `SignInWithPublicKeyAsync`
  which runs the whole flow through an injected `IStarhermitSigner`.
- `ExchangeRefreshTokenAsync`, `SignOutAsync`, `AdoptSessionAsync`.

Supported key types are `Ed25519`, `ECDSA-P256` and `RSA-PSS`. The SDK never generates or stores a
private key.

`StarhermitChallenge.CanonicalPayload` reproduces the exact bytes the server verifies. The deployment
verifies against its own .NET serialisation of the challenge (PascalCase member names in declaration
order) while the response arrives camel-cased, so re-serialising what was received would never verify.
This coupling is recorded in `contracts/backend-notes.md` as something the API should fix by returning
the bytes to sign.

`StarhermitSession` exposes user id, expiry, issue time, authentication method and permissions, read
from the access token without verifying it - a local convenience for expiry checks only. The refresh
token never appears in `ToString()`, a log, or telemetry.

## 8. Account, social and voice

- `client.Me`: profile read and partial update, terms acceptance, avatar upload and download, public
  profiles and avatars, linked identities, privacy settings, presence heartbeat (with a helper that
  pauses on suspension and sends immediately on resume), public-key listing, registration and
  revocation, and entitlements.
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
- `client.Voice`: create, list, read, join, leave, mute and close voice rooms.
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
  absence as `null` rather than an error. `StarhermitCloudSaveSynchronizer` compares server metadata
  with a caller-owned sync marker and, when both sides changed, reports a conflict instead of picking
  a winner; `LocalWins`, `RemoteWins` and `Abort` are explicit policies.
- `client.Achievements` and `client.Leaderboards`: unlocks, client-claimable unlock, definitions,
  paged entries with server-assigned ranks, and score submission where the definition permits it.

Game settings, cloud saves and game player state stay visibly separate: preferences in the settings
document, an opaque progression archive in cloud saves, and server-authoritative rating and history
read-only through the games API.

## 10. Authoritative games

`client.Games.ForSlug(slug)` returns a client covering game metadata and effective capabilities,
launch-token minting, session listing and reads, AI sessions, nearest-rating matchmaking (enqueue,
status, cancel), invites (create, list, accept, decline), cross-game invite inbox, replays (a game
with replays disabled answers `404`, which is surfaced rather than flattened to an empty list),
control bindings, and the schema-free player settings document (whole-document get, replace, merge and
delete, plus single-key operations). Server budgets are reported from the response rather than
duplicated as SDK policy.

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

`client.RealtimeRooms`: create rooms with teams, seats, AI seats and backfill; read the caller's active
room; list, accept and decline invites; quick-join; read a room; invite; open for backfill; start;
leave; assign seats; submit results.

`StarhermitRealtimeConnection` attaches to `/ws/v1/realtime`, sends `chat`, `ready` and (host only)
`event` control frames, and receives binary payloads prefixed with the sender's 16-byte participant id
plus presence and roster frames. It refetches the room after a reconnect.

`client.Relay` lists, creates (bound to exactly one game session or realtime room), reads, joins and
closes relays. `StarhermitRelayConnection` carries opaque binary payloads verbatim in both directions
and rejoins after a reconnect. The SDK assumes no send rate; the deployment paces the connection from
the game's declaration and closes a connection that exceeds it.

## 12. Browser games and publishing

`client.BrowserGames`: submit a repository, claim, list own and all, transfer, delete, icon and cover
art, streamed bundle upload over HTTP, folder upload, audience stats, hosting toggle, deployment pin
and read, and the GitHub link state.

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
  credential the SDK has never seen is still removed. URL fragments are dropped entirely.
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

- **144 hermetic NUnit tests** covering the JSON layer, the request pipeline (routes, verbs, query, bodies,
  headers, credentials, response mapping, cancellation, typed errors, paging), retry eligibility and
  jitter bounds, refresh coordination and rotation persistence, redaction, socket machinery
  (ordering, backpressure, reconnection, policy closes, handler exceptions), all six wire protocols,
  cloud-save conflict resolution, client lifecycle and model tolerance, and the coverage manifest.
  They run under `dotnet test` and, unchanged, as Unity EditMode tests.
- **Five live contract tests** that read a real deployment when `STARHERMIT_TEST_BASE_URL` is set, and
  are skipped otherwise. They confirm the SDK's parsing, model mapping, paging metadata, clock
  synchronisation and error typing against the API as deployed, over the anonymous surface. They have
  been run against the development deployment and pass.
- **Three Unity compile-checks** (`build/unity/*.csproj`) type-check the Unity-only code - the
  `UnityWebRequest` transport, the WebGL bridge, settings, audio adapters, editor tooling and all
  eight samples - against small API stubs, on machines with no Unity licence.
- **A generated coverage manifest**: `tools/generate_coverage.py` reads the backend's controllers and
  emits `contracts/coverage-manifest.json`, `Documentation~/api-coverage.md` and the data
  `ContractCoverageTests` enforces. Of 192 API operations, 182 are mapped to typed SDK methods, 6 are
  WebSocket routes served by connection classes, and 4 are classified as not-for-clients with reasons.
  Zero are unmapped.
- **`tools/verify.sh`** runs the whole gate: build every project with warnings as errors and XML
  documentation required, run the suite, regenerate the manifest and fail on any drift.

### 17.2 What needs the licensed build farm

The CI workflow defines the editor matrix (2021.3 LTS, 2022.3 LTS, Unity 6) and IL2CPP player builds
for Linux, Android and WebGL with High stripping, gated on a `UNITY_LICENSE` secret. Those jobs have
not been run here. Until they have, the platform matrix in §2.1 describes intended support rather than
qualified support, and no platform should be advertised as verified.

### 17.3 Not yet built

- The optional WebRTC voice adapter. The PCM fallback path is implemented; a WebRTC adapter would slot
  in behind the same interfaces.
- The authenticated half of the live contract suite. Exercising it needs a real session, and the
  tests deliberately will not mint one from a signing secret - a test that forges credentials stops
  testing the thing it claims to test. Wiring it to a seeded test account on an ephemeral backend is
  the remaining work.

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
