# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses semantic versioning:
additive API and endpoint coverage is a minor release, a source-breaking change is a major one.

## [Unreleased]

Brings the SDK level with the backend's current surface: 45 operations added, 3 classified, 0
unmapped (227 of 243 mapped, 6 socket routes, 10 classified).

### Added

- **Players.** On a game client: `GetQueuesAsync` and matchmaking for a named subset of queues,
  `GetLeaderboardsAsync`, `GetLinkedAchievementsAsync`, `GetSessionRecoveryAsync`,
  `LeaveSessionAsync`, `FileReportAsync` (crash and bug reports with attachments) and
  `GetMyReportsAsync`. `Me.GetTermsAsync` (anonymous) and `Me.RevokeCurrentPublicKeyAsync`.
  `Auth.GetOAuthProvidersAsync` (anonymous). `Voice.SetServerMuteAsync` for a room's host.
- **Connection tickets.** `Auth.IssueConnectionTicketAsync` and, carrying a launch token's authority,
  `StarhermitGameClient.IssueConnectionTicketAsync`.
- **Rooms.** `BrowseRoomsAsync`, `JoinByCodeAsync`, `GetJoinedRoomsAsync`, `UpdateRoomAsync`
  (compare-and-swap on `Revision`) and `MatchmakeRoomAsync`. `StarhermitRoom` gains `Name`,
  `JoinCode`, `IsVisible` and `Revision`; its config gains `BackfillAiPlayers` and `JoinInProgress`.
- **Game owners.** On a game client: `GetDiagnosticsAsync` and webhook list, create, delete and
  resume. On `BrowserGames`: `ChangeUrlAsync`, `ListRemovedAsync`, `RestoreAsync`,
  `GetReleaseNotesAsync`, live sessions (`GetSessionsAsync`, `EndSessionAsync`), achievement and
  leaderboard CRUD, `ResetPlayerEloAsync`, player reports (paged list and enumeration, detail,
  attachment download, status, delete) and server container output (`GetContainerLogsAsync`,
  `GetContainerCrashesAsync`, `GetContainerCrashAsync`).
- **Cloud-save versions.** `StarhermitCloudSaveInfo.ETag`, `DownloadVersionAsync`, and an
  `UploadAsync` overload taking a `StarhermitSaveCondition` (`IfMatch`, `IfNoSaveExists`,
  `IfAnySaveExists`, `ForVersion`).
- **Limit refusals.** `StarhermitPreconditionFailedException` (`412`, with `CurrentETag`) and
  `StarhermitQuotaExceededException` (`413`, `507`). Every API exception exposes `Limit`, `Used` and
  `LimitKey` from the refusal body.
- **Model fields.** `StarhermitBrowserGame.ServerRuntime`, `Description` and `ReleaseNotesUpdatedAt`;
  `StarhermitLeaderboard.Key`, `GameDefinitionId`, `CurrentPeriodStartedAt` and `NextResetAt`;
  `StarhermitGameSessionSummary.PausedAt`; `StarhermitMatchmakingTicket.WaitedSeconds`,
  `SearchEloBand` and `MaxWaitSeconds`; `StarhermitMatchmakingStatuses.Queued` and `Expired`.

### Changed

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
