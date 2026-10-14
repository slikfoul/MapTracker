# Changelog

## 1.0.4

- Shared dungeon visit and completion progress for locally discovered entrances.
- Synchronized manual completion marks and resets with persistent logical revisions.
- Prioritized dungeon-progress updates within the existing sharing traffic limits.

## 1.0.3

- Independent bidirectional discovery and creature-sharing channels.
- Ordinary player-pin visibility remains separate from mod exchange and mod markers.
- Saved dungeons and portals receive priority during initial discovery synchronization.
- Bandwidth-aware creature observation lifetimes during crowded multiplayer synchronization.
- Pending updates remain queued until outbound bandwidth is available.
- Background streamed discovery saves with atomic replacement and previous-save backups.

## 1.0.2

- Consistent marker retirement across object destruction, remote expiry and saved-discovery reconciliation.
- Spatially indexed shared navigation target lookup.
- Immediate cancellation of queued and retried shared destinations when discovery sharing is disabled.
- Current-creature-only transmission caches and stable minimap label visibility.
- Fresh filter, preset and discovery panels when entering a new world.

## 1.0.1

- Bandwidth-limited discovery synchronization with acknowledged batches, delta updates and native-connection congestion awareness.
- Bounded background processing of received discoveries and creature positions.
- Independent static-discovery, live-creature transmission and reception controls, with network-silent operation when all are disabled.
- Shared navigation and discovery exchange negotiate each player's reception settings independently.
- Active-marker rendering, incremental discovery indexing and stable label visibility for large discovery databases.
- Frame-budgeted startup preparation of saved markers and one-time duplicate reconciliation.

## 1.0.0

- Track creatures, portals, dungeons, resources, plants, dropped items and other world objects on the minimap and large map.
- Searchable categories with per-type display, discovery retention and notification controls.
- Remember portal tags and dungeon entrances, with white unvisited markers, light-blue visited markers and left-click completion marks.
- Independently customize each map's creature icons or colored dots, icon sizes, names, fonts, outlines, stars and category visibility.
- Zoom-aware marker grouping with centered counts and exact member locations on expansion.
- Optional centered same-species name groups, enabled by default with a 15-meter radius.
- Configurable discovery radius, including camera-, obstacle- and mist-aware line-of-sight detection.
- Save, load and delete built-in or custom filter presets.
- Personal navigation to objects, public players and arbitrary map points, with HUD arrows, icons, names, distances and automatic arrival.
- Configurable selection, sharing and cancellation keys, with right-click target cancellation.
- Optional shared discoveries with deduplication, recipient filters, retained static locations and separate live-creature transmission, disabled by default.
- Shared navigation alongside personal navigation, with gold, blue and combined target highlights.
- Prominent queued discovery notifications with object icons, a gold heading and contrasting names.
- Separate visibility controls for collected plants, depleted ore and completed dungeons.
