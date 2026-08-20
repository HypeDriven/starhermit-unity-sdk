# Matchmaking game

Mints a game-scoped launch token, writes a player setting, queues for nearest-rating matchmaking,
attaches the game socket to the session it produces, and sends a command.

Two things are deliberate. The socket authorises with the launch token rather than the account
session, so a game build never holds account-wide credentials. And the sample never predicts the
outcome of a command: the authoritative frame comes back from the server, and until it does, nothing
has happened.
