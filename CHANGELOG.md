# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses semantic versioning:
additive API and endpoint coverage is a minor release, a source-breaking change is a major one.

## [Unreleased]

Brings the SDK level with the backend's current surface: 45 operations added, 3 classified, 0
unmapped (227 of 243 mapped, 6 socket routes, 10 classified). Sockets then move onto connection
tickets, rooms take their newer settings at creation, cloud saves gain a conditional download, and the
live contract suite runs signed in.

### Added

- **Players.** On a game client: `GetQueuesAsync` and matchmaking for a named subset of queues,
  `GetLeaderboardsAsync`, `GetLinkedAchievementsAsync`, `GetSessionRecoveryAsync`,
  `LeaveSessionAsync`, `FileReportAsync` (crash and bug reports with attachments) and
  `GetMyReportsAsync`. `Me.GetTermsAsync` (anonymous) and `Me.RevokeCurrentPublicKeyAsync`.
  `Auth.GetOAuthProvidersAsync` (anonymous). `Voice.SetServerMuteAsync` for a room's host.
- **Connection tickets.** `Auth.IssueConnectionTicketAsync` and, carrying a launch token's authority,
  `StarhermitGameClient.IssueConnectionTicketAsync` - and every socket now uses them (see Changed).
- **Rooms.** `BrowseRoomsAsync`, `JoinByCodeAsync`, `GetJoinedRoomsAsync`, `UpdateRoomAsync`
  (compare-and-swap on `Revision`) and `MatchmakeRoomAsync`. `StarhermitRoom` gains `Name`,
  `JoinCode`, `IsVisible` and `Revision`; its config gains `BackfillAiPlayers` and `JoinInProgress`.
- **Game owners.** On a game client: `GetDiagnosticsAsync` and webhook list, create, delete and
  resume. On `BrowserGames`: `ChangeUrlAsync`, `ListRemovedAsync`, `RestoreAsync`,
  `GetReleaseNotesAsync`, live sessions (`GetSessionsAsync`, `EndSessionAsync`), achievement and
  leaderboard CRUD, `ResetPlayerEloAsync`, player reports (paged list and enumeration, detail,
  attachment download, status, delete) and server container output (`GetContainerLogsAsync`,
  `GetContainerCrashesAsync`, `GetContainerCrashAsync`).
- **Room settings at creation and quick-join filters.** `CreateRoomAsync(gameSlug, StarhermitRoomSettings)`
  sets a name, browser visibility (`IsVisible`), the AI backfill cap (`BackfillAiPlayers`, where `0`
  starts with empty seats) and `JoinInProgress` alongside the layout, AI seats, backfill delay and
  metadata. `QuickJoinAsync(gameSlug, StarhermitQuickJoinFilter)` takes the deployment's filters - team
  count, seats per team, a metadata subset, and `IncludeInProgress` - and a miss is a
  `StarhermitNotFoundException`. The original signatures are unchanged and send what they always sent.
- **Cloud-save versions.** `StarhermitCloudSaveInfo.ETag`, `DownloadVersionAsync`, and an
  `UploadAsync` overload taking a `StarhermitSaveCondition` (`IfMatch`, `IfNoSaveExists`,
  `IfAnySaveExists`, `ForVersion`).
- **Conditional downloads.** `CloudSaves.DownloadIfChangedAsync(key, heldETag)` sends `If-None-Match`
  and returns `StarhermitCloudSaveDownload.NotModified` with no archive on a `304`, so checking for a
  newer save costs one request and no bytes. `StarhermitRequest.IfNoneMatch` (and `AcceptsNotModified`)
  lets a `Raw` request do the same; the pipeline treats a `304` as an answer only for a request that
  sent a validator.
- **Signed-in live contract tests.** Five more live tests run when `STARHERMIT_TEST_MAILBOX` names the
  directory a deployment's mail lands in: they register an account through the emailed link, as a
  player does, and check public-key sign-in, ticketed handshakes (one per handshake, a replay refused,
  a launch token's ticket fenced to its game), room settings with filtered quick-join across two
  accounts, and conditional cloud-save downloads. `tools/live-test.sh` runs the whole live suite
  against a throwaway backend built from the checkout, with `tools/smtp_sink.py` as its mail server.
- **Limit refusals.** `StarhermitPreconditionFailedException` (`412`, with `CurrentETag`) and
  `StarhermitQuotaExceededException` (`413`, `507`). Every API exception exposes `Limit`, `Used` and
  `LimitKey` from the refusal body.
- **Model fields.** `StarhermitBrowserGame.ServerRuntime`, `Description` and `ReleaseNotesUpdatedAt`;
  `StarhermitLeaderboard.Key`, `GameDefinitionId`, `CurrentPeriodStartedAt` and `NextResetAt`;
  `StarhermitGameSessionSummary.PausedAt`; `StarhermitMatchmakingTicket.WaitedSeconds`,
  `SearchEloBand` and `MaxWaitSeconds`; `StarhermitMatchmakingStatuses.Queued` and `Expired`.
- **AOT and stripping checks without a Unity licence.** `build/aot/Starhermit.AotCheck.csproj`
  compiles the runtime under the .NET trimming and AOT analyzers with warnings as errors, as part of
  the solution build. `build/aot-smoke` publishes it with Native AOT and full trimming and, with
  `STARHERMIT_LIVE_AOT=1 tools/live-test.sh`, drives a live deployment through registration, key
  sign-in, a ticketed socket and versioned cloud saves (17 checks, all passing).

### Changed

- **Breaking: four calls whose route answers `204` now return `Task`.** `RealtimeRooms.DeclineInviteAsync`
  (was `Task<StarhermitRoomInvite>`), `StarhermitGameClient.DeclineInviteAsync` (was
  `Task<StarhermitGameInvite>`), `StarhermitGameClient.DeleteSettingsAsync` (was
  `Task<StarhermitGameSettings>`) and `Chat.DeleteMessageAsync` (was `Task<StarhermitMessage>`). The
  deployment sends no body for any of them, so the model they returned was assembled from nothing - an
  empty id, status and content that read like a real answer. Code that awaited them without using
  the result compiles unchanged; code that read the result was reading empty fields. Every other
  operation was checked against its backend action and already matched.
- **Socket handshakes present a connection ticket, not the access token.** Every connect and every
  reconnect fetches a fresh single-use ticket immediately before the handshake and passes it as
  `?ticket=`, with no `Authorization` header beside it; a launch-scoped socket buys its ticket with
  the launch token, so the ticket carries that game's scope. Only a deployment whose ticket endpoint
  answers `404` gets the token as before (header and `?access_token=`). Dedicated-server sockets keep
  presenting their token, which the ticket endpoint is fenced from by design.
- **The cloud-save synchroniser no longer overwrites a save it did not compare.** Its uploads name the
  version it read (`If-Match`, or `If-None-Match: *` when there was none), and a write from another
  device in between is reported as `Conflict` - under `LocalWins` too. A download is reported only
  with the metadata of the bytes it returns, reading again if a write lands in between. Against a
  deployment that sends no versions it behaves as before.
- **`507` is a `StarhermitQuotaExceededException`**, not a `StarhermitServerException`, and `413` is
  one too rather than the base `StarhermitApiException`. Code catching either base type is unaffected.
- `StarhermitMatchmakingStatuses.Waiting` is obsolete: the API reports a searching ticket as `queued`.
- Connection tickets and room join codes are redacted from logs by name.

### Fixed

- **Public-key sign-in failed about three times in ten.** `StarhermitChallenge.CanonicalPayload`
  escaped the challenge's strings as ordinary JSON, while the deployment verifies bytes written by
  System.Text.Json's default encoder, which writes `+` as `\u002B`. The nonce is base64, so every
  challenge whose nonce held a `+` answered `401 Invalid signature`. Found by the new live tests; the
  string members are now escaped as that encoder does, and timestamps are still copied verbatim.
- **A connect whose credential could not be obtained left the connection reading `Connecting`**, so
  every later `ConnectAsync` returned at once without connecting. It now reads `Faulted`, and the
  next connect tries again - which matters now that each connect first fetches a ticket.
- `QuickJoinAsync` was documented as creating a room when none is free. It never did: the deployment
  answers `404`, and the documentation now says so.
- **API error codes were always `***`.** The error body is redacted before it is read, and `code` is
  redacted by name (OAuth authorization codes travel under it), so `ErrorCode` never carried an API
  code from a `code` member. It is now read back from the raw body when it has the snake_case shape of
  an API error code, which an authorization code does not.
- An upload's `StarhermitCloudSaveInfo` reported `Exists = false` for the save it had just stored.

- **Emailed links are redeemed with POST.** `Auth.VerifyPublicKeyRegistrationAsync`,
  `Auth.ConfirmKeyRevocationAsync` and `Auth.ConfirmIdentityLinkAsync` now `POST` the link's URL. The
  backend answers `GET` on those links with a confirm page that changes nothing — so a mail scanner
  opening the link cannot redeem it — and these calls got that page instead of JSON. The three `GET`
  pages are classified in the coverage manifest as browser-only.

## [0.1.0] - 2026-08-20

First implementation. Covers the whole deployed REST API v1 and all six WebSocket protocols.

### Added

- **Core.** `StarhermitClient` with no static state and no I/O at construction; injectable transport,
  socket factory, token store, OAuth browser, signer, clock, logger, telemetry sink, callback
  dispatcher, file store and audio adapters; bounded, jittered retries with a process-wide budget;
  coordinated single-flight token refresh with atomic rotation; structural redaction; typed exception
  hierarchy; diagnostics snapshot; server-clock synchronisation.
- **Reflection-free JSON.** Hand-written parser, writer and per-model codecs. Unknown members and
  unknown enum strings are preserved, large integers keep their exact value, and `Optional<T>`
  distinguishes omitted from explicitly null for PATCH bodies.
- **Typed clients** for authentication, profile and privacy, public keys, friends, chat, voice,
  catalog, entitlements, activity and external libraries, ratings, wishlist, cloud saves with an
  opt-in conflict-reporting synchroniser, achievements, leaderboards, authoritative games (including
  game-scoped launch tokens, matchmaking, invites, replays, controls and the player settings
  document), the dedicated-server surface, realtime rooms, peer relay, browser games, publishers, and
  server time - plus `Raw` for endpoints this version does not type, resumable signed downloads, and
  `StarhermitBuildPublisher` in the optional publishing assembly.
- **Six WebSocket connections** sharing one state machine, ordered sends, bounded outbound queues,
  jittered reconnection that stops on authorization and policy closes, and per-protocol state refresh
  after a reconnect: chat, voice, authoritative games, realtime rooms, peer relay and streamed game
  uploads.
- **Unity platform layer.** `UnityWebRequest` transport, browser WebSocket bridge for WebGL, settings
  asset, console logger, microphone capture and per-speaker playback, application-lifecycle bridge,
  `link.xml`, texture helpers with explicit ownership, and an editor build hook that refuses to ship a
  player pointed at a development endpoint.
- **Verification.** 149 NUnit tests - 144 hermetic, plus 5 that read a live deployment when one is
  configured - running both under `dotnet test` and as Unity EditMode tests;
  Unity-only code compiled against API stubs in CI; and a generated coverage manifest that fails the
  build when an API operation has no SDK mapping.

### Known gaps

- Platform qualification (per-target runtime smoke tests, IL2CPP build matrix) needs the licensed
  build farm described in `spec.md` §17.2 and has not been run here.
- The live contract tests cover the anonymous API surface only; the authenticated half needs a seeded
  test account.
- The optional WebRTC voice adapter is not implemented; the PCM fallback path is.
