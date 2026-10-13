# MapTracker

Client-side Valheim map tracker for BepInEx and Harmony. Development preview **0.7.0**.

MapTracker displays discovered objects and creatures on the native minimap and large map. It does not require Jotunn or a server plugin. Current builds are development previews; multiplayer sharing and performance on crowded maps still need in-game testing.

## Features

- Searchable object catalog built from the installed game and mods, with category filters and grouped prefab variants.
- Per-type **Show object** and **Keep discovered** choices. Portals and dungeons are remembered by default; confirmed portal destruction removes its discovery, while leaving the area preserves it. Old portal locations within 32 m are rechecked against ready network areas; twelve seconds of continuous absence removes stale discoveries. Existing network portals are retained even outside the configured detection radius or camera view. Creature positions remain temporary.
- Separate dungeon types, entrance icons, visited colors and click-to-toggle completion. Dungeon entrances retain a single discovery across network ID changes, preserving visited and completed states. Portal tags are displayed, and grouped portal names open on click.
- Independent creature icon switches and size settings for each map. Trophy heads or relationship-colored dots, optional silhouette outlines with adjustable thickness, stars and group counts.
- Independent name visibility, minimap creature name distance and zoom-based large-map labels. Names stay anchored to their markers; the selected destination and nearby creatures have priority when labels collide. Large-map names are independent of character distance, and hover reveals suppressed names. Creature and object font sizes can be adjusted separately.
- Optional radius-based grouping of same-species names into a centered caption and count, independent of icon grouping. Marker positions remain unchanged and selected destinations keep their own names.
- Discovery radius: line of sight, whole meters, or the maximum client-loaded area. Sight detection checks the camera, obstacles and the game's mist visibility API, including demister effects.
- Optional grouping, with separate plant and creature switches. Group distance uses screen pixels, so zooming out covers more world space. Disable plant grouping for exact plant locations.
- Built-in Default, Gathering, Exploration and Household presets, plus named custom presets. Presets can be saved, loaded and deleted, including built-in presets.
- Optional sharing between MapTracker clients, disabled by default. Received discoveries are not forwarded. MapTracker markers are separate from native cartography-table pins.
- Collected plants, depleted ore and manually completed dungeons have separate visibility settings. Actual observed plant regrowth restores a marker.
- Original transparent head icons for bats, hens, chicks and young boars, wolves, asksvins and loxes, embedded in the DLL.
- Dense-map label placement uses spatial lookup, reusable buffers and cached layouts. Individually moving markers and scale changes invalidate the cache. FPS improvements still need in-game verification.
- Discovery browser searches remembered local places, including disabled catalog types, by name, type key and portal tag. Favorites survive restarts and are highlighted on the map.
- Select a navigation destination to show straight-line distance and world compass direction below the minimap or in the corner of the large map. Locate it on the large map without enabling its entire category. Targets remain independent of grouping and can be cleared.
- Optional navigation arrow at the top of the gameplay HUD, pointing toward the destination relative to the camera, with its name and distance. Toggle **Navigation → Show navigation arrow** independently of the map destination.
- Independent category visibility on each map, full presentation presets and a next-preset button.
- Groups have full diameter limits in screen pixels and world meters, stay anchored to a real member, and reveal exact member positions on click. Creature names use distance hysteresis; labels reserve space around the character arrow.
- Optional new-dungeon and per-type discovery notifications, disabled by default. Saved discoveries and rebinding existing entrances do not trigger notifications.
- A configurable scan budget, incremental marker updates, pooled nearby labels and cached visibility geometry.

## Initial settings

New profiles start with minimap dots and hidden minimap creature names, large-map trophy icons with names, silhouette outlines, a 19 px minimap icon size and 30 px large-map icon size. Minimap name distance is 70 m when names are enabled. Discovery uses all client-loaded data. General grouping is enabled with a minimum of 2 and 36 px distance; plant and creature grouping are disabled. Only hostile creature relations are enabled, sharing and other-player pins are disabled, and only completed dungeons remain visible. Portals and dungeons are remembered. Existing saved choices are preserved. Initial values are embedded in `assets/defaults/settings.json`.

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

Settings are grouped into General, Maps, Names, Icons, Detection, Grouping, Sharing, Creatures, Completed and Tools.

For **dots on the minimap and heads on the large map**, disable the minimap creature icon checkbox and enable the large-map creature icon checkbox in **Icons**. Names, stars and grouping retain their own settings.

Runtime files inside the BepInEx profile:

- `config/Slikfoul.MapTracker.cfg` — configuration.
- `config/MapTracker/catalog.json` — exported object catalog.
- `config/MapTracker/worlds/<world>_<character>.json` — discoveries for each world and character.

Saved discoveries are separate from native/manual pins. Disabling display does not delete saved discoveries.

## Discoveries and navigation

Alt + left-click an object or any location on the large map to select a navigation target. Alt-click the selected target again to clear it. Empty map space creates a temporary waypoint, outside saved discoveries. Collapsed groups reveal their members first. Navigation stops within 5 meters of the goal; interiors, teleportation and wrong floors do not trigger arrival. F10 cancels navigation, configurable under Navigation. Alt gestures consume native double/right-click behavior and do not change dungeon completion states. Mod UI and configuration labels use English; player portal tags are preserved.

Open **Discoveries** in the filter window. Search names or portal tags, use **Fav** to toggle a favorite, **Map** to center the large map and select the destination, **Target** to navigate without moving the map, and **Notify** to enable discovery notifications for that type. The list contains remembered local static locations, independent of the current catalog filter; it does not reveal undiscovered world objects. **Favorites only** narrows the list. **Clear target** ends navigation.

Use **Next preset** to cycle undeleted built-in and custom presets, and **Clear target** in Discoveries to stop navigation. Newly saved custom presets include Minimap, Large map, Names, Icons, Detection, Grouping and internal category visibility settings. Legacy presentation presets remain readable after section changes. Sharing permissions and plugin enablement are never changed by presentation presets. Favorites and selected destinations stay individual instead of disappearing inside groups.

Use the searchable filters to select object categories and types; category rows are hidden from the configuration manager while saved visibility choices remain intact. Per-map visibility, icons, sizes and names are organized under **Minimap** and **Large map**; shared font sizes and name grouping remain under **Names**, and the solid black silhouette outline remains under **Icons**. General grouping, plant grouping and creature grouping still have their existing switches. Groups form only when displayed icons overlap or nearly touch and split automatically as zoom creates room. **Maximum group span (m)** limits the distance between the furthest members, in the range **8–800**, default **400**; previously saved values are retained. It never forces separated screen icons into a group. Counts appear at the center of the representative icon. Click a large-map group to show its members at real positions; click again or an empty area to close it. A selected navigation target is displayed individually.

Enable **Names → Group creature names** for a single caption such as **Leech ×3** at the center of nearby same-species markers on either map. **Creature name group radius (m)** controls this separately from discovery and icon grouping, default **15 m**. The setting is off by default; it does not override name visibility switches or minimap name distance.

Alt-click a public player marker on the large map to navigate to that player. The destination follows the current public position, including players with matching names. Tracking stops on arrival, cancellation, disconnection or when the player stops sharing their map position. It is temporary and does not create saved discoveries or duplicate player pins.

**Notifications** are opt-in: enable new dungeons, use the **Notify** column in the object catalog before your first discovery, or use **Notify** in Discoveries for particular static types. Messages are spaced at least four seconds apart. Notifications require actual client discovery within the chosen radius or sight mode. Previously saved places, dynamic creatures, loose dropped items and already collected resources do not notify.

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
