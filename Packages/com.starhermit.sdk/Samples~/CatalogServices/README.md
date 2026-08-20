# Catalog services

Searches the catalog, claims a free title, wishlists and rates it, records a launch and ends it, then
synchronises a cloud save.

Worth noting:

- Ending a launch happens in a `finally` with an uncancelled token. A launch left open reports a
  session that never finished.
- Downloads verify a checksum and are promoted atomically, so an interrupted download cannot leave a
  truncated file where the game expects a whole one.
- The synchroniser reports a conflict rather than resolving it. Silently picking a winner is how a
  player loses a save.
