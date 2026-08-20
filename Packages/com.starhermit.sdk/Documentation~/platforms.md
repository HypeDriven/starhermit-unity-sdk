# Platforms

Every module compiles for every Unity build target. What differs by platform is the *adapters*, and
absence surfaces as `StarhermitFeatureUnavailableException` at the call rather than as a missing type
at build time. A dedicated server with no microphone still runs chat, relay and game sessions.

| Capability | Desktop / mobile / console | WebGL | Headless server |
| --- | --- | --- | --- |
| REST | `UnityWebRequestTransport` (default) | `UnityWebRequestTransport` | `HttpClientTransport` |
| WebSocket | `ClientWebSocketAdapter` (default) | `WebGLSocketFactory` (browser bridge) | `ClientWebSocketAdapter` |
| OAuth | system browser or console adapter | popup / same-page adapter | URL handoff supplied by the host |
| Token storage | injected secure store; `EncryptedFileTokenStore` is the opt-in fallback | browser storage adapter | injected secret store or memory |
| File transfer | `SystemFileStore` | memory or chunked browser sink | `SystemFileStore` |
| Voice capture | `UnityMicrophoneCapture` | browser media adapter where permitted | unavailable unless injected |
| Public-key signing | injected `IStarhermitSigner` | browser crypto bridge or injected signer | injected signer |

The client picks a default transport and socket factory for the running platform; override either
through `StarhermitOptions`.

## WebGL

The browser owns the connection, so the SDK talks to it through
`Plugins/WebGL/StarhermitWebSocket.jslib`. Nothing extra is required in the WebGL template: the plugin
is part of the package and Unity links it automatically.

Two consequences to design around:

- A browser cannot set handshake headers, so the SDK also passes the access token as
  `?access_token=`. It is redacted from every log the SDK writes; make sure your own logging does the
  same, and prefer a deployment served over `wss`.
- There is no filesystem. Supply an `IStarhermitFileStore` backed by browser storage, or use the
  byte-array overloads and keep archives small.

## Consoles and other restricted platforms

Nothing platform-licensed lives in this package, which is what lets it be distributed openly. Supply
your own implementations of `IStarhermitOAuthBrowser`, `IStarhermitTokenStore`, `IStarhermitSigner`,
and - if the platform requires a certified networking stack - `IStarhermitTransport` and
`IStarhermitSocketFactory`. Your platform code stays in your own project, behind the same interfaces
the rest of the SDK already uses.

## Headless servers

```csharp
var options = new StarhermitOptions
{
    CallbackDispatcher = ImmediateCallbackDispatcher.Instance,
    Transport = new HttpClientTransport(),
};
```

A dedicated game server authenticates with a deployment key rather than a player session; see
`client.GameServer` and the DedicatedServer sample. Never ship a deployment key in a player build.

## IL2CPP, stripping and AOT

The SDK maps JSON by hand. There is no reflection-based serializer, no dynamic code generation, and no
type name that arrives from the wire, so managed stripping cannot remove a member the wire format
needs and AOT has nothing to fail to compile.

The package ships `Runtime/link.xml`, which preserves its own assemblies and the crypto types the
optional encrypted token store uses. High stripping is a supported and tested configuration.

## Unity lifecycle

`StarhermitLifecycle.Attach(client, heartbeat)` bridges Unity's application events: presence pauses
when the application is backgrounded and resumes immediately on return, and the client is disposed on
quit without any synchronous network work.
