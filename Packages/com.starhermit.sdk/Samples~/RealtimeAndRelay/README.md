# Realtime and relay

Quick-joins a lobby, connects the room socket, sends a ready control frame, and - when this client is
the host - assigns seats, starts the match and opens a peer relay bound to the room.

Notice what the host can do that a guest cannot: only the host may send `event` control frames, and
the server closes a guest that tries. Notice too that the SDK refetches the room after a reconnect,
because seats, the host and the room itself may all have changed while the socket was down.
