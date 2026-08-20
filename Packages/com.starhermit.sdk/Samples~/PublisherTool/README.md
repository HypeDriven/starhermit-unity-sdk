# Publisher tool

Reads the caller's publishers and download analytics, then streams a game bundle over the upload
socket.

The upload protocol is worth understanding before adapting this: the server concatenates binary
frames in arrival order and publishes only when it receives `{"type":"complete"}`. A dropped
connection, a cancelled task, or an explicit abort therefore all mean "nothing was published",
which is what makes retrying from the start safe.
