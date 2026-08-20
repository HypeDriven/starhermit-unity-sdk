# Security and credentials

## Credential types are separate on purpose

| Credential | What it is | Where it lives |
| --- | --- | --- |
| Account session | The player's own access/refresh pair | `IStarhermitTokenStore` |
| Launch token | Game-scoped, fenced by the backend to one game's routes | scoped credential store, in memory |
| Server token | Minted by a dedicated server from a deployment key | scoped credential store, in memory |
| Deployment key | Identifies a game server deployment | supplied by the host, never stored by the SDK |

None substitutes for another. Minting a launch token does not replace the account session, and a
launch token cannot call account routes - the server refuses, and the SDK surfaces the refusal rather
than working around it.

`client.Games.ForSlug(slug).WithLaunchToken()` returns a client that authorises with the launch token.
Use it in a game build: it is the credential you can afford to hand to game code.

## Storage

The SDK ships no store that claims to be secure, because none of the options available to a package
are. The default is in-memory: the session ends with the process.

- **Best**: inject the platform's own store - a console secure store, an OS keychain, an entitlement
  service.
- **Opt-in fallback**: `EncryptedFileTokenStore`, AES-CBC with HMAC-SHA256 over a key *you* supply. It
  stops the pair being readable in a text editor. It does not stop anyone who can read your process
  memory or extract a key that shipped inside the build, and the documentation says so rather than
  implying otherwise.
- **Never**: `PlayerPrefs`. It is a plaintext registry key or an unprotected file depending on the
  platform, and the package will not pretend differently.

Refresh-token rotation is persisted before any waiting call resumes, so a crash mid-refresh cannot
leave the store holding a token the server has already retired.

## What never gets logged

Redaction is structural - by header, query-parameter and JSON member *name*, at every depth - so a
credential this SDK has never seen is still removed:

- `Authorization`, `Cookie`, invoke-key and API-key headers
- `access_token`, `refresh_token`, `code`, `state`, signature and signed-storage query parameters
- `accessToken`, `refreshToken`, `privateKey`, `keyData`, `invokeKey`, `signedUrl`, `uploadUrl`… in
  bodies
- URL fragments entirely - that is where OAuth returns tokens

`StarhermitSession.ToString()`, stored-session and scoped-token `ToString()` contain no token
material. Exceptions carry a redacted, size-capped body. Telemetry receives event name, duration,
status family, retry count and request id - never a URL, a body, or anything a player typed.

## Transport

HTTPS and WSS are required. `AllowInsecureTransport` exists for a development endpoint and is refused
at client construction otherwise; the package's build hook fails a non-development Unity build that
still has it enabled. It never disables certificate validation.

## Server authority

The SDK does not re-implement or relax platform rules. Authorization, entitlement, room membership,
score validation, game outcomes and storage budgets are decided server-side; the client reads the
answer. Where the server publishes a limit - the settings-document budget, an upload allowance - the
SDK surfaces the server's number rather than a copy that was true when the package shipped.

Remote JSON never selects a CLR type, a file path, or an object to activate. Downloads and cloud saves
are written to a temporary file and promoted atomically after any supplied checksum matches, and file
paths cannot escape the root the application chose.

## Bounded by default

Inbound socket messages, outbound queues and diagnostic bodies all have limits that hold even when the
deployment would allow more. Overflow raises a typed error or closes the connection; nothing grows
without bound because a peer misbehaved.
