# Changelog

## 1.0.9

- Stable player availability and hover hints in the preset-sharing recipient list.
- Progressive discovery search with stable ordering while large result sets are prepared.
- Lower-allocation discovery archive loading and multiplayer synchronization.
- Frame-budgeted catalog and dungeon preparation with cached map-label ordering.
- Nearby markers appear while saved discoveries are prepared.
- Background catalog export and progressive full-map marker refresh.
- Discovery tracking and sharing exclude transient technical effects.
- Continuous discovery sharing without rebuilding the saved archive.
- Loaded-area validation of shared portal discoveries.
- Low-allocation streaming of large discovery archives.
- Frame-budgeted preparation and saving of large discovery archives.

## 1.0.8

- Share built-in and custom filter presets directly with a selected online player.
- Accept or decline incoming preset offers. Accepted presets are added without applying them or replacing existing presets.
- Sender and receiver progress bars with transfer percentages and save confirmation.
- Optional blocking of incoming preset offers, disabled by default.
- Map filter gear styled and sized to match the native map controls.

## 1.0.7

- Added game artwork and model-based icons for trees, containers, plants, resources and world landmarks. Related variants share matching icons.
- Added native trophy icons for related creature variants, including young and summoned creatures, boss aspects and special variants.
- Added model-based portraits for creatures without suitable trophy artwork, including Fallen Warrior, Frozen King and Frysling.
- Charred Twitchers now use the native Charred Skull icon.
- Training dummies now use their native construction icon.
- Moved Ashlands ruins from Dungeons to Locations and added a distinct ruined-arch icon.
- Preserved existing native map markers for boss locations, traders and other locations already marked by the game.

## 1.0.6

- Charred Fortress in the Dungeons category with a fortress map symbol and dungeon discovery notifications.
- Shared fortress visit status when a player opens its Charred Chest, with manual completion kept separate.
- Creature-found cards accompanied by an independent green creature-search HUD arrow for types selected with Notify.
- Automatic nearest-target selection and switching after a target dies, disappears or is reached within five meters.
- Creature search selections in map filter presets, alongside static discovery notifications.

## 1.0.5

- Persistent portal captions at close zoom, with upper and lower label placement for crowded large-map markers.
- Separate black silhouette outlines and thickness controls for non-creature object icons on both maps.

- Location-aware dungeon entrance identity across remembered discoveries and multiplayer synchronization.
- Retained visit, completion and favorite state when reconciling legacy entrance records.

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
