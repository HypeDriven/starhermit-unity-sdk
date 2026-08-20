# Dedicated server

Exchanges a deployment refresh key for a short-lived server token, reads a session with it, and drops
the token on shutdown.

Two rules this sample exists to demonstrate:

- The key comes from the environment, never from a serialised asset or a constant. A player build that
  contains one has published the game's credentials.
- A server token is a different credential type from a player session. It lives in its own store, it
  cannot be used as an account session, and it is redacted from logs by the same structural rules.
