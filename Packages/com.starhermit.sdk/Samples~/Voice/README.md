# Voice

Opens a voice room on a conversation, connects the voice socket, plays what arrives, and captures the
microphone when there is one.

The sample treats a missing microphone as an ordinary outcome rather than an error: a headless build,
a device without a capture device, or a declined permission prompt all raise
`StarhermitFeatureUnavailableException` from the capture adapter, and the player carries on listening.

Frames use the platform's fallback convention - 20 ms of 16 kHz mono 16-bit PCM - so a Unity game and
a desktop client can share a room without either installing a codec.
