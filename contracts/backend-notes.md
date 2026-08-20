# Contract notes for the platform team

Findings from mapping this SDK against the deployed API. The deployed contract is authoritative for
wire compatibility, so the SDK follows it exactly as it is today - these are the places where doing so
costs a client more than it should.

## 1. The OpenAPI documents are empty

`/swagger/{api-v1,publisher-v1,ws-v1}/swagger.json` all return `200` with `"paths": {}`.

`AddApiExplorer` is configured with `GroupNameFormat = "'v'VVV"`, so API versioning tags every
operation with the group `v1`, while the registered documents are named `api-v1`, `publisher-v1` and
`ws-v1`. Swashbuckle's default inclusion predicate matches group name to document name, so no
operation lands in any document.

Either register a document named `v1`, or set a `DocInclusionPredicate` that maps the versioned group
onto the three intended documents.

Consequence for clients: there is no machine-readable contract to generate from. This SDK derives its
inventory from the controllers instead (`tools/generate_coverage.py`), which works but cannot see
request or response schemas.

## 2. Public-key challenges must be re-serialised in .NET property casing

`PublicKeyAuthService.VerifySignature` verifies the signature against
`JsonSerializer.Serialize(payload)` of the server-side `ChallengePayload` - PascalCase member names,
in declaration order. The response the client receives is serialised by ASP.NET with the web defaults,
so it arrives camel-cased.

A client therefore cannot sign the bytes it received; it has to rebuild the server's form
(`{"ChallengeId":…,"Fingerprint":…,"Issuer":…,"Audience":…,"Expiry":…,"Nonce":…,"ClientTimestamp":…}`),
reusing the timestamp strings verbatim, and hope neither the property order nor the serializer's date
format ever changes.

Suggested fix: return the exact bytes to sign - base64 - alongside the payload, and verify against
those. It removes an invisible coupling to one server's serializer settings.

## 3. Errors are prose, not codes

Almost every failure answers `{"error": "A sentence explaining the problem."}`. Two exceptions do
carry a code (`oauth_session_required`).

A client that wants to react differently to different refusals has to match on status alone, or on
English prose that is not a contract. A stable `code` member on every error would let clients handle
cases precisely and localise their own messages.

## 4. `revoked` means two different things

`DELETE /api/v1/me/public-keys/{keyId}` and `.../all` answer `{"revoked": [ …key rows… ]}`, while
`GET /api/v1/auth/public-key/revoke/confirm` answers `{"revoked": 3}`.

Same name, array in one place and a count in the other. This SDK reads both shapes; a generated client
would not.

## 5. Paging metadata is spelled two ways

Catalog and chat routes return `totalCount`; leaderboard entries and external software return `total`.
The SDK accepts either. One name would be better.
