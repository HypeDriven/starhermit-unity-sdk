# Starhermit Unity SDK

Typed, asynchronous Unity client for the Starhermit platform: authentication, profile, friends, chat,
voice, catalog, entitlements, cloud saves, achievements, leaderboards, authoritative games, realtime
rooms, peer relay and publishing.

Every public operation of the deployed REST API v1 is mapped to a typed method, and all six WebSocket
protocols have a connection class. Coverage is checked by a test, not by hand:

| | |
| --- | --- |
| API operations | 192 |
| Mapped to typed SDK methods | 182 |
| Classified as not-for-clients (reachable through `Raw`) | 4 |
| Unmapped | 0 |
| WebSocket protocols | 6 |

## Install

Unity Package Manager, by Git URL, local path, or scoped registry. Minimum editor Unity 2021.3 LTS,
API compatibility level .NET Standard 2.1, Mono and IL2CPP, managed stripping up to High.

```csharp
var client = StarhermitClient.Create(new StarhermitOptions
{
    ApiBaseUri = new Uri("https://api.starhermit.com/api/v1/"),
    GameSlug = "chess",
    TokenStore = myPlatformSecureStore
});

await client.InitializeAsync();
var profile = await client.Me.GetProfileAsync();
```

Full documentation is in [`Packages/com.starhermit.sdk/Documentation~`](Packages/com.starhermit.sdk/Documentation~/index.md):
[getting started](Packages/com.starhermit.sdk/Documentation~/index.md),
[platforms](Packages/com.starhermit.sdk/Documentation~/platforms.md),
[security](Packages/com.starhermit.sdk/Documentation~/security.md),
[diagnostics](Packages/com.starhermit.sdk/Documentation~/diagnostics.md),
[API coverage](Packages/com.starhermit.sdk/Documentation~/api-coverage.md).

Eight samples ship with the package, from sign-in to a dedicated server: see `Samples~`.

## Design in one screen

- **Adapters, not `#if` forests.** Transport, sockets, token storage, OAuth, signing, files, audio and
  the clock are interfaces. Every module compiles for every Unity target; a capability the platform
  genuinely lacks raises `StarhermitFeatureUnavailableException` at the call, so a headless server
  without a microphone still runs chat, relay and game sessions.
- **No reflection, anywhere.** JSON is mapped by hand, so managed stripping cannot remove a member the
  wire format needs and remote JSON can never name a type to activate. IL2CPP with High stripping is a
  supported configuration rather than a hope.
- **Credentials are separate types.** Account session, game-scoped launch token and dedicated-server
  token live in different stores with different lifetimes. None can stand in for another.
- **The server is the authority.** The SDK never recomputes an entitlement, a rank, a score or a
  membership, and surfaces server-published limits instead of copies that were true at release.
- **Failures are classified.** Retries are bounded, jittered and limited to failures a second attempt
  could survive. A `401` buys exactly one coordinated refresh and one replay. A transport failure is
  never dressed up as an API response.
- **Nothing leaks.** Redaction is structural, by name, at every depth - so a credential this SDK has
  never seen is still removed from logs, exceptions and telemetry.

## Building and testing without Unity

Unity is the shipping compiler, but a licence is not needed to verify the package. Every Unity-only
file is guarded, so the same sources compile as plain .NET:

```bash
./tools/verify.sh              # build everything, run the tests, check API coverage
dotnet build Starhermit.Sdk.sln
dotnet test build/tests/Starhermit.Tests.csproj
```

`build/unity/*.csproj` compile the Unity-only paths - the `UnityWebRequest` transport, the WebGL
socket bridge, the settings asset, the audio adapters, the editor tooling and all eight samples -
against small API stubs, so that half of the package is type-checked in CI too.

The suite runs on NUnit, which is also Unity Test Framework's engine: the same files under
`Tests/Runtime` execute as EditMode tests inside the editor.

Five of the 149 tests read a real deployment instead of a fixture, so a contract drift on the server shows
up here rather than in a player's bug report. They are skipped unless you point them at one:

```bash
STARHERMIT_TEST_BASE_URL=http://starhermit.test:5050/api/v1/ dotnet test build/tests/Starhermit.Tests.csproj
```

## Contract maintenance

`tools/generate_coverage.py` reads the backend's controllers and regenerates
`contracts/coverage-manifest.json`, the documentation table, and the data the coverage test enforces.
Run it after any API change; an endpoint that appears without an SDK mapping fails the build rather
than going unnoticed.

Contract mismatches found against the deployed API are recorded in
[`contracts/backend-notes.md`](contracts/backend-notes.md) for the platform team.

## Licence

MIT. See [LICENSE](LICENSE).
