# Repository Guidelines

## Project Structure & Module Organization

This repository is currently specification-first. [`spec.md`](spec.md) defines the SDK contract and is the source of truth for implementation scope. Build the Unity Package Manager package under `Packages/com.starhermit.sdk/`:

- `Runtime/`: platform-neutral SDK code, grouped into `Core`, `Auth`, `Profile`, `Social`, `Catalog`, `Games`, `Realtime`, `Publishing`, and `Platform`.
- `Editor/`: Unity editor tooling only; runtime assemblies must never reference it.
- `Plugins/WebGL/`: browser bridges for WebSockets, OAuth, and platform capabilities.
- `Tests/Runtime/` and `Tests/Editor/`: PlayMode and EditMode tests.
- `Samples~/` and `Documentation~/`: compilable examples and user documentation.

Keep assembly definitions small enough that consumers can exclude optional publishing and sample code.

## Build, Test, and Development Commands

No build scripts are committed yet. When scaffolding the package, provide repeatable commands such as:

```bash
<UNITY> -batchmode -quit -projectPath TestProject -runTests -testPlatform EditMode
<UNITY> -batchmode -quit -projectPath TestProject -runTests -testPlatform PlayMode
git diff --check
```

Replace `<UNITY>` with the supported Unity editor executable. CI must test the minimum Unity LTS, the latest two LTS releases, IL2CPP/high stripping, and the platform matrix defined in `spec.md`.

## Coding Style & Naming Conventions

Use C# with four-space indentation, nullable annotations, and XML documentation on public APIs. Use `PascalCase` for types and public members, `camelCase` for locals and parameters, and `_camelCase` for private fields. Async methods end in `Async` and accept a trailing `CancellationToken`. Interfaces begin with `I`; keep DTO wire names aligned with the generated OpenAPI contracts. Avoid reflection-dependent construction, blocking calls, static mutable session state, and runtime references to `UnityEditor`.

## Testing Guidelines

Use Unity Test Framework/NUnit. Name tests `Method_Scenario_ExpectedResult`. Every API operation needs route, verb, serialization, cancellation, error, and authorization coverage. Add contract fixtures for unknown fields/enums and protocol tests for fragmented frames, reconnects, ordering, and secret redaction.

## Commit & Pull Request Guidelines

History uses short, imperative subjects such as `Fix ...`, `Add ...`, and `Preserve ...`. Keep commits focused. Pull requests should describe behavior, list tests and Unity targets exercised, link relevant issues, and update `spec.md`, generated contracts, coverage manifests, samples, or migration notes when affected. Include screenshots only for editor or sample UI changes.

## Security & Configuration

Never commit access tokens, refresh tokens, private keys, invoke keys, signed URLs, or console SDK code. Use injected secure stores and platform adapters. Keep development endpoints and TLS overrides out of production builds.
