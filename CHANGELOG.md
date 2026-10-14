# Changelog

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

## 0.13.0

- Prominent discovery HUD cards with an object icon, gold heading and contrasting name. Queued notices pause while maps or menus hide the gameplay HUD.

## 0.12.1

- Expanded group details retain a fixed position independently of native and hover captions, reserving member name space and avoiding duplicate hover captions.

## 0.12.0

- Independent configurable mouse or keyboard bindings for sharing navigation targets, default Alt + middle-click.

## 0.11.0

- Shared static discoveries merge into each recipient's discovery set, with per-type visibility and retention controls and distant ally observations.
- Optional live creature transmission with shared network identity and periodic position updates, disabled by default.
- Expanded group lists prefer the left side and avoid their member icons and labels.

## 0.10.0

- Map-filter-only presets preserve configuration manager preferences and retain per-type display, discovery memory and notification choices.

## 0.9.1

- Three-state highlighting of navigation targets on both maps, arbitrary shared map destinations and navigation summaries in the lower-left large-map corner.

## 0.9.0

- Destination icons alongside personal and shared navigation names on the HUD and map summaries.

## 0.8.0

- Configurable map navigation selection, default Alt + left-click, and right-click cancellation on the selected target.

- Alt + middle-click shared navigation with a separate blue destination and HUD arrow, preserving personal navigation.
- Centered creature-name grouping enabled by default with a 15-meter radius.

## 0.7.1

- Creature heads stand alone at their marker positions, with relationship colors retained in dot mode.

## 0.7.0

- Optional radius-based grouping of same-species names into a centered caption, independent of icon grouping.
- Separate Minimap and Large map configuration sections with shared font and outline controls.
- Solid black silhouette outline with adjustable thickness and an expanded 800-meter group span limit, default 400.

## 0.6.1

- Automatic overlap-based grouping that splits with map zoom, a single world span limit and centered group counts.
- Large-map creature names based on zoom and available space, independent of character distance.

## 0.6.0

- Nearby creature names anchored to their markers, independent name distances for both maps and hover names for crowded markers.
- Adjustable silhouette outline thickness and a single group compactness control.
- Separate font sizes for creatures and other objects, and Alt-click navigation to players with public map positions.

## 0.5.1

- Object categories are managed through the searchable filters without additional category rows in the configuration manager.

## 0.5.0

- Optional camera-relative navigation arrow with destination name and distance on the gameplay HUD.

## 0.2.0

- Search discovered locations by name, object type or portal tag; select a destination and display its distance and compass direction.
- Persistent favorite discoveries, highlighted on the map and available through a favorites-only list.
- Full presentation presets, a next-preset button and configurable preset/navigation shortcuts.
- Independent category layers for the minimap and large map.
- Group diameter limits in screen pixels and world meters, real-object group anchors and exact member locations on click.
- Stable creature name distance boundaries and a reserved area around the character marker.
- Optional notifications for newly discovered dungeons and selected object types.
- Configurable per-frame scanning budget, reusable label objects, cached visibility geometry and on-demand performance measurements.

## 0.1.27

- Dungeon entrances retain one remembered marker across network ID changes, with visited and completed states preserved.

## 0.1.26

- Remembered portal locations are reconciled against loaded world data when revisited.
- Independent creature presentation, discovery filters and named presets for both native maps.
