# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A **Unity Package Manager package** (`com.starhermit.sdk`, namespace `Starhermit`) giving Unity games
typed, asynchronous access to the Starhermit REST v1 and WebSocket v1 APIs — the backend that lives at
`../starhermit`. It targets every Unity build target: desktop, mobile, WebGL, console, XR, embedded and
headless server.

The package is implemented and verified: 227 of the API's 243 operations are mapped to typed methods
(10 more are classified as not-for-clients, 6 are the WebSocket routes), all six socket protocols have
connection classes, and 194 tests run green (10 of them skip unless a live deployment is named). `spec.md` describes what it does; this file describes how
to work on it.

This is one of the agent-generated projects under the parent dashboard pipeline. The parent
`../../CLAUDE.md` describes that pipeline, not this codebase — it is not guidance for working here.

## Commands

```bash
./tools/verify.sh                 # the whole gate: build all, test, regenerate + check API coverage
./tools/verify.sh --skip-coverage # same without the backend checkout

dotnet build Starhermit.Sdk.sln   # 6 projects: SDK, publishing, tests, 3 Unity compile-checks
dotnet test build/tests/Starhermit.Tests.csproj
dotnet test build/tests/Starhermit.Tests.csproj --filter "FullyQualifiedName~ProtocolTests"

python3 tools/generate_coverage.py            # regenerate contracts + docs + test data
python3 tools/generate_coverage.py --check    # non-zero exit if any API operation is unmapped
```

Unity is the shipping compiler, but **no Unity licence is needed to verify this package**. Every
Unity-only file is wrapped in `#if UNITY_2021_3_OR_NEWER` (or `UNITY_WEBGL`/`UNITY_EDITOR`), so the
same sources compile as plain .NET Standard 2.1 through `build/sdk`, and the Unity-only halves are
type-checked against small API stubs through `build/unity/*.csproj`. The stubs live in
`build/unity/stubs/` and ship with nothing — if you use a new Unity API, add its signature there or the
Unity code path silently stops being checked.

Tests use **NUnit**, which is also Unity Test Framework's engine, so `Tests/Runtime/*.cs` run both
under `dotnet test` and as EditMode tests inside the editor.

For contract work against a live backend, `./tools/live-test.sh` builds the Api from `../starhermit`
into a temporary directory, runs it against throwaway Postgres/Redis containers with
`tools/smtp_sink.py` as its mail server, runs every `Category=Live` test, and removes it all. The
signed-in half (`LiveSessionTests`) needs `STARHERMIT_TEST_MAILBOX` as well as
`STARHERMIT_TEST_BASE_URL`: it registers accounts through the emailed link rather than minting tokens,
and the deployment's per-address registration throttle must be off (`live-test.sh` sets
`PublicKeyAuth__RegistrationThrottleHours=0`). Public-key sign-in is limited to sixty requests a minute
per address, so the tests share one registered client rather than signing each test in.

## Architecture

### The request pipeline is the only place decisions are made

`StarhermitRestClient` (`Runtime/Core/Http/`) owns credential selection, the single coordinated
refresh, retry eligibility, error typing, redaction and telemetry. Every typed client derives from
`StarhermitServiceClient` and does nothing but describe an endpoint — path, verb, query, body, and how
to read the result. **Never make one of those decisions inside a service client**: 227 operations
agreeing about what a 401 means is the entire point of the split.

Adding an operation: add the method to its client with an `"area.operation"` id, then run
`tools/generate_coverage.py`. The generator finds the path by scanning the method body for a
`Get("…")`/`Post($"…")`/`Request("VERB", "…")` call plus an operation-id literal in the same method, so
a path built somewhere else (a helper, a variable) reads as unmapped and fails `ContractCoverageTests`.

### Adapters, not `#if` forests

Transport, sockets, token store, OAuth browser, signer, clock, file store, audio and telemetry are all
interfaces on `StarhermitOptions`. That is what lets one assembly serve every platform: a capability
the platform genuinely lacks raises `StarhermitFeatureUnavailableException` with a stable `Reason` at
the call, rather than failing the build or breaking unrelated modules. Keep new platform work behind an
interface; the only `#if` blocks should be adapter implementations.

### No reflection, ever

JSON is a hand-written parser (`Runtime/Core/Json/`) plus per-model `Read(JsonValue)` codecs. This is
not stylistic: it is what makes IL2CPP with High stripping safe and stops remote JSON naming a type to
activate. Do not introduce `System.Text.Json`, Newtonsoft, or any reflection-based mapping.

Model rules that tests enforce: unknown members stay reachable through `RawJson`; unknown enum strings
are preserved as strings (and an unknown privacy level reads as the *most private*, never Public);
absent members are `Missing`, distinct from `Null`, which is what `Optional<T>` needs for PATCH.

### Credentials are separate types

Account session (`StarhermitSessionManager` + `IStarhermitTokenStore`), game-scoped launch tokens and
dedicated-server tokens (`StarhermitScopedCredentials`) live in different stores. `Credential` on a
request selects which one the pipeline attaches. The `X-Starhermit-Sdk-Game-Slug` header steers that
choice inside the process and is stripped before the request leaves — if you touch header handling,
keep it stripped.

### Sockets share one state machine

`StarhermitConnection` (`Runtime/Core/Sockets/`) owns connecting, credentials, ordered sends, bounded
queues, reconnection and event dispatch. The six protocol classes only describe their path, query
parameters and frames. Every handshake - including each reconnect - fetches a fresh connection ticket
first (a ticket is spent by the handshake that presents it) and sends it as `?ticket=` with no
`Authorization` header; only a `404` from the ticket endpoint falls back to the token. A launch-scoped
socket buys its ticket with the launch token, never the account session, because the ticket copies the
claims of whatever bought it. `FakeTransport` answers ticket requests ahead of the script
(`ConnectionTickets`; null routes them through it, `WithoutConnectionTickets()` plays an old deployment).

Reconnection deliberately **stops** on authorization and policy closes, and `OnReconnectedAsync`
refetches or rejoins because membership may not have survived — a failure there is logged, not treated
as a broken socket.

### Contract coupling worth knowing

Public-key challenges must be signed over the server's **PascalCase** serialisation of the challenge
payload, not the camel-cased JSON the client receives (`StarhermitChallenge.CanonicalPayload`). That
and four other findings are written up in `contracts/backend-notes.md` for the platform team; the
deployed contract stays authoritative until they change it.

### An error body is redacted before it is read

`code` is on the redactor's JSON list because OAuth authorization codes travel under that name, and
`StarhermitErrorInfo.ReadBody` only ever sees the redacted body - so the API's own error codes
(`rate_limited`, `cloud_save_quota_exceeded`, ...) are read back from the raw body by `ReadErrorCode`,
and only when they have the snake_case shape every API error code has. Widen that shape and a token can
reach `ErrorCode`; drop the call and every code reads as `***`, which is what it did until limit
refusals needed one.

## Invariants

- Public members carry XML documentation and the build treats warnings as errors — both gates are on
  in every project, so an undocumented public member fails CI.
- Runtime assemblies never reference `UnityEditor`; models never reference `GameObject`,
  `MonoBehaviour` or scenes.
- No `async void`, no blocking the main thread, no static mutable state (tests run isolated clients
  side by side).
- Secrets never reach a ScriptableObject, scene, `Resources`, log, exception message, telemetry event
  or build artifact. Redaction is structural, by name — extend the name lists in
  `StarhermitRedactor` rather than filtering values at call sites.
- Server-authoritative decisions are never recomputed locally, and server-published limits are
  surfaced from responses rather than hard-coded.
- Large payloads stream; downloads are written to a temp file and promoted only after any checksum
  matches.
- `spec.md` describes shipped behaviour. A change that alters observable behaviour updates it in the
  same change; `CHANGELOG.md` holds version history.
