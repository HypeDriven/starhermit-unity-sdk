# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses semantic versioning:
additive API and endpoint coverage is a minor release, a source-breaking change is a major one.

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
