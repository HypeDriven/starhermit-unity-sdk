# Authentication and profile

Signs in through OAuth, restores a stored session on launch, reads `/me`, records terms acceptance,
downloads the avatar, and runs a presence heartbeat that pauses with the application.

What to take from it:

- `StarhermitClient.Create` does no I/O; `InitializeAsync` is what decides whether the player is
  already signed in.
- OAuth needs a platform adapter. The sample's is a stub that opens a browser and then explains what
  is missing, because there is no correct default across desktop, mobile, WebGL and console.
- Nothing here writes a token anywhere. Supply `StarhermitOptions.TokenStore` with a store your
  platform actually protects, or accept that the session ends with the process.
