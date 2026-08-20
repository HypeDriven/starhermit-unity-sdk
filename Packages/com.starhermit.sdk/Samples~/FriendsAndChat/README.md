# Friends and chat

Lists friends with the presence the viewer is allowed to see, loads conversation history over REST,
then connects the chat socket for live delivery.

The point of the sample is reconciliation: the same message can arrive from both sources, so it is
de-duplicated by the server's message id. The SDK never invents an id or an optimistic timestamp,
which is what makes matching by id sufficient.
