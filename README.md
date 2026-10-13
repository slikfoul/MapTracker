# MapTracker

Client-side Valheim map tracker for BepInEx and Harmony. Development preview **0.12.0**.

MapTracker displays discovered objects and creatures on the native minimap and large map. It does not require Jotunn or a server plugin. Current builds are development previews; multiplayer sharing and performance on crowded maps still need in-game testing.

## Features

- Searchable object catalog built from the installed game and mods, with category filters and grouped prefab variants.
- Per-type **Show object** and **Keep discovered** choices. Portals and dungeons are remembered by default; confirmed portal destruction removes its discovery, while leaving the area preserves it. Old portal locations within 32 m are rechecked against ready network areas; twelve seconds of continuous absence removes stale discoveries. Existing network portals are retained even outside the configured detection radius or camera view. Creature positions remain temporary.
- Separate dungeon types, entrance icons, visited colors and click-to-toggle completion. Dungeon entrances retain a single discovery across network ID changes, preserving visited and completed states. Portal tags are displayed, and grouped portal names open on click.
- Independent creature icon switches and size settings for each map. Trophy heads without offset dots, or relationship-colored dots; optional silhouette outlines with adjustable thickness, stars and group counts.
- Independent name visibility, minimap creature name distance and zoom-based large-map labels. Names stay anchored to their markers; the selected destination and nearby creatures have priority when labels collide. Large-map names are independent of character distance, and hover reveals suppressed names. Creature and object font sizes can be adjusted separately.
- Optional radius-based grouping of same-species names into a centered caption and count, independent of icon grouping. Marker positions remain unchanged and selected destinations keep their own names.
- Discovery radius: line of sight, whole meters, or the maximum client-loaded area. Sight detection checks the camera, obstacles and the game's mist visibility API, including demister effects.
- Optional grouping, with separate plant and creature switches. Icons group when they overlap on screen and split as zoom creates room, with a configurable world span limit. Disable plant grouping for exact plant locations.
- Built-in Default, Gathering, Exploration and Household presets, plus named custom presets. Presets can be saved, loaded and deleted, including built-in presets.
- Optional sharing between MapTracker clients, disabled by default. Received discoveries are not forwarded. MapTracker markers are separate from native cartography-table pins.
- Collected plants, depleted ore and manually completed dungeons have separate visibility settings. Actual observed plant regrowth restores a marker.
- Original transparent head icons for bats, hens, chicks and young boars, wolves, asksvins and loxes, embedded in the DLL.
- Dense-map label placement uses spatial lookup, reusable buffers and cached layouts. Individually moving markers and scale changes invalidate the cache. FPS improvements still need in-game verification.
- Select a navigation destination to show straight-line distance and world compass direction below the minimap or in the corner of the large map. Locate it on the large map without enabling its entire category. Targets remain independent of grouping and can be cleared.
- Optional navigation arrow at the top of the gameplay HUD, pointing toward the destination relative to the camera, with its name and distance. Toggle **Navigation → Show navigation arrow** independently of the map destination.
- Independent category visibility on each map and presets limited to the per-type display, retention and notification choices in the filter window.
- Groups stay anchored to a real member and reveal exact member positions on click, with detail lists placed clear of the expanded icons and labels. Creature names use distance hysteresis; labels reserve space around the character arrow.
- Optional new-dungeon and per-type discovery notifications, disabled by default. Saved discoveries and rebinding existing entrances do not trigger notifications.
- A configurable scan budget, incremental marker updates, pooled nearby labels and cached visibility geometry.

## Initial settings

New profiles start with minimap dots and hidden minimap creature names, large-map trophy icons with names, silhouette outlines, a 19 px minimap icon size and 30 px large-map icon size. Minimap name distance is 70 m when names are enabled. Discovery uses all client-loaded data. General grouping is enabled with a minimum of 2 and a 400 m maximum world span; plant and creature icon grouping are disabled. Same-species name grouping is enabled with a 15 m radius. Only hostile creature relations are enabled; discovery sharing, automatic creature transmission and other-player pins are disabled. Completed dungeons remain visible, while collected plants and depleted ore are hidden. Portals and dungeons are remembered. Existing saved choices are preserved. Initial values come from the configuration bindings and `assets/defaults/settings.json`.

## Requirements

- Valheim with BepInEx installed.
- Configuration Manager is useful for changing settings in game.
- .NET SDK and .NET Framework 4.6.2 reference assemblies for building the plugin; the test projects target .NET 9.

Game, Unity, Harmony and BepInEx assemblies are referenced from your local installation and are not distributed in this repository. Newtonsoft.Json is provided by Valheim.

## Build

Set the game and mod-profile paths for your machine:

```powershell
dotnet build MapTracker.csproj -c Release -p:ValheimDir="C:\Games\Valheim" -p:ModProfile="C:\Path\To\ValheimProfile"
```

The output is `bin/Release/MapTracker.dll`. Deploy only this DLL; do not copy framework assemblies from the build folder.

`BuildPackage.ps1 -SkipBuild` prepares and verifies the importable local archive. For Thunderstore-managed profiles, `DeployProfile.ps1` installs that archive through the local registry with backups, preserving the config and enabled state. Close both Valheim and Thunderstore Mod Manager before local registration; background Overwolf may remain running.

Close Valheim before replacing the plugin. The deployment script checks the process, backs up the previous DLL and verifies the installed file:

```powershell
.\DeployProfile.ps1 -Profile "C:\Path\To\ValheimProfile"
```

Builds do not deploy automatically.

## Configuration and data

Open the filters through the gear button on the large map, the Configuration Manager **Filters** button, or the `maptracker` console command after loading a world.

Settings are grouped into General, Minimap, Large map, Names, Icons, Detection, Grouping, Sharing, Creatures, Completed, Performance, Notifications, Navigation and Tools.

For **dots on the minimap and heads on the large map**, disable the creature icon checkbox in **Minimap** and enable it in **Large map**. Names, stars and grouping retain their own settings.

Runtime files inside the BepInEx profile:

- `config/Slikfoul.MapTracker.cfg` — configuration.
- `config/MapTracker/catalog.json` — exported object catalog.
- `config/MapTracker/worlds/<world>_<character>.json` — discoveries for each world and character.

Saved discoveries are separate from native/manual pins. Disabling display does not delete saved discoveries.

## Navigation

Alt + left-click on the large map to navigate to a MapTracker marker, public player or any map location. Empty map space creates a temporary destination marker. Repeat the gesture on the selected target to stop navigation. Right-click a target to cancel it. Change the selection button or keyboard combination in Navigation → Select navigation key; Alt + left-click is the default. Keyboard bindings work while pointing at the large map. Ordinary middle-click keeps the game's ping, and scrolling keeps map zoom. Shared navigation has its own configurable binding in Navigation → Share navigation key, default Alt + middle-click. Dungeon completion states remain unchanged.


Use **Preset** in the map filter window to save, load or delete built-in and custom filter sets. Presets store only per-type Show object, Keep discovered and Notify choices. Existing presets retain their filter selections and ignore old configurator preferences. Sizes, grouping, radius, relationships and other configuration manager values stay unchanged.

Use the searchable filters to select object categories and types; category rows are hidden from the configuration manager while saved visibility choices remain intact. Per-map visibility, icons, sizes and names are organized under **Minimap** and **Large map**; shared font sizes and name grouping remain under **Names**, and the solid black silhouette outline remains under **Icons**. General grouping, plant grouping and creature grouping still have their existing switches. Groups form only when displayed icons overlap or nearly touch and split automatically as zoom creates room. **Maximum group span (m)** limits the distance between the furthest members, in the range **8–800**, default **400**; previously saved values are retained. It never forces separated screen icons into a group. Counts appear at the center of the representative icon. Click a large-map group to show its members at real positions; click again or an empty area to close it. A selected navigation target is displayed individually.

**Names → Group creature names** is enabled by default for a single caption such as **Leech ×3** at the center of nearby same-species markers on either map. **Creature name group radius (m)** controls this separately from discovery and icon grouping, default **15 m**. Existing saved choices are preserved; Reset applies the new defaults. The setting does not override name visibility switches or minimap name distance.

Alt + left-click a public player marker on the large map to navigate to that player. The destination follows the current public position, including players with matching names. Tracking stops on arrival, cancellation, disconnection or when the player stops sharing their map position. It is temporary and does not create saved discoveries or duplicate player pins.

**Notifications** are opt-in: enable new dungeons, use the **Notify** column in the object catalog before your first discovery, for particular static types. Messages are spaced at least four seconds apart. Notifications require actual client discovery within the chosen radius or sight mode. Previously saved places, dynamic creatures, loose dropped items and already collected resources do not notify.

**Sharing → Share discoveries** sends static discoveries to other MapTracker users in the same world. Received dungeons, ore, roots, plants and other static objects merge into the normal discovery set. Repeated packets and discoveries of the same object do not create another marker; nearby distinct objects remain separate. Local observations and local dungeon progress take priority.

Enable **Sharing → Other player pins** to display received discoveries, then use your own **Show object** filters. **Keep discovered** retains static discoveries after the sender leaves or you restart. Fresh observations from a teammate can appear outside your detection radius, including another map zone. Receipt and storage do not depend on whether a type is currently displayed. Sending observes your detection distance or sight mode independently of display filters.

**Sharing → Share creature positions** is disabled by default and requires Share discoveries. Enable it to transmit live creature positions, levels and relationships. Receiving uses Other player pins and your own creature filters. Creatures share network identity, so two players seeing the same creature produce one marker, with local observations taking priority. Moving creatures update at most twice per second; unchanged creatures use a periodic heartbeat. Live positions expire after updates stop and are not stored as permanent creature sightings. Received data is never automatically forwarded. This checkbox does not disable explicit shared navigation.

Expanded group lists prefer space to the left of their members and avoid their icons and visible names. The list retains its chosen position and follows the group when dragging the map; pointer movement does not reposition it. Placement is checked after visible names are finalized, using their actual text bounds. Near a map edge or after a real collision the list uses another free area; if none fits, it remains hidden while the expanded markers stay visible.

With **Sharing → Share discoveries** enabled, use **Navigation → Share navigation key** (default **Alt + middle-click**) on any map location or object to share a navigation target. Configure this independently of **Select navigation key**: choose a mouse button, keyboard key or combination, or clear the binding to disable the gesture. Keyboard bindings work while pointing at the large map. A configured mouse gesture replaces that button's normal map action; if both navigation bindings match, sharing has priority. Both sender and recipients must enable Share discoveries and have shared navigation support. Personal navigation stays active. The shared HUD arrow is blue; when both destinations exist, the arrows sit side by side at the top center. Existing object markers are highlighted without a duplicate waypoint. Empty locations or objects not yet available to a recipient use a shared waypoint. Personal targets are orange, shared targets blue, and a target selected both ways uses their blended color. Only one shared destination is active at a time. Available object positions update the shared destination. Right-click its marker to cancel it; arrival also clears it. Shared destinations are temporary and are not forwarded automatically.

Navigation summaries include the destination icon for actual objects, without an icon for empty map points. Large-map summaries stack in the lower-left corner. Shared packets include an optional catalog type key, so each recipient resolves the image locally; older packets retain the destination label without an object icon.

## Performance

The default **Scan budget ms** is 2, adjustable from 1 to 8. It limits inspection work per frame; snapshot creation and a single expensive object can exceed that amount.

## Validation

After building, check compatibility against your local game and profile:

```powershell
.\VerifyBuild.ps1 -Game "C:\Games\Valheim" -Profile "C:\Path\To\ValheimProfile"
```

Geometry and grouping checks do not require a running game:

```powershell
dotnet run --project tests/LabelLayoutChecks/LabelLayoutChecks.csproj -c Release
dotnet run --project tests/ClusterChecks.csproj -c Release
```

Serialization checks use the actual game and BepInEx assemblies:

```powershell
dotnet run --project tests/SerializationChecks/SerializationChecks.csproj -c Release -p:ManagedDir="C:\Games\Valheim\valheim_Data\Managed" -p:CoreDir="C:\Path\To\ValheimProfile\BepInEx\core"
```

Static checks cover Harmony targets, runtime dependencies and embedded PNG resources. Label tests compare placement against the original algorithm, including randomized maps, dense crowds, cached panning and movement invalidation. These checks do not establish in-game FPS or real-server behavior.
