# MapTracker

Discover, remember and navigate to useful places on Valheim's native minimap and large map.

MapTracker adds configurable markers for creatures, portals, dungeon entrances, resources, plants and other world objects. Choose what to display, keep important discoveries on the map and switch between presets for different activities.

**Client-side. Requires BepInEx. No server plugin or Jotunn required.**

## Features

- Searchable object catalog with categories, type filters and support for registered objects from installed mods.
- Separate **Show object** and **Keep discovered** controls for each type.
- Remembered portals with their tags, dungeon entrance icons, visited states and manual completion marks.
- Independent creature icons or relationship-colored dots on each map, with adjustable icon sizes, names and stars.
- Separate category visibility for the minimap and large map.
- Discovery radius in whole meters, the full client-loaded area, or a line-of-sight mode using camera, obstacle and mist checks.
- Optional grouping with separate plant and creature switches, screen/world size limits and exact member locations on click.
- Built-in and custom presets that can be saved, loaded and deleted. Presets store only map-filter choices.
- Optional notifications for newly discovered dungeons and selected static object types.
- Optional marker sharing between MapTracker clients.

## Installation

**Mod manager:** install MapTracker in your Valheim profile. The required BepInEx dependency is declared in the package.

**Manual:** install BepInEx for Valheim, then copy the package's `BepInEx` folder into your Valheim game folder.

An in-game BepInEx configuration manager is optional and useful for adjusting display, discovery and grouping settings. Object filters and presets are available through MapTracker's own interface.

## Getting started

1. Enter a world and let MapTracker collect the object catalog.
2. Open the large map and click the **gear button** to open filters.
3. Search for an object or choose a category. Enable **Show object** for the types you want to track.
4. Enable **Keep discovered** for static types whose known locations should remain visible after you leave.

The catalog search selects object types across all categories.

## Creature markers

Choose icons or dots independently for each map in the **Minimap** and **Large map** configuration sections. Icon sizes, solid black silhouette outline thickness, name visibility and minimap name distance are configurable. Set creature and other object font sizes separately under **Names**. Stars and group counts remain available in either mode.

In icon mode, the center of the head marks the creature's position, with no offset relationship dot. The relationship colors below apply in dot mode.

Creature names stay beside their own markers. On the minimap, names use the configured distance from your character. On the large map, names depend on zoom and available screen space, including faraway creatures when you zoom in on them. The selected destination takes priority; where names would overlap, the closest ones retain their labels and the others remain available on hover. Names disappear at distant map zoom, leaving markers, stars and counts.

- **Red:** hostile creatures.
- **Green:** friendly creatures.
- **Yellow:** neutral wildlife.
- **Cyan:** tamed creatures.

New profiles start with minimap dots and hidden minimap creature names, and large-map creature icons with names at close zoom. Existing settings are preserved when updating.

## Discovery and remembered locations

New discoveries come from objects loaded by your client; MapTracker does not scan unloaded parts of the world. Previously remembered locations can remain visible outside that area.

Portals and dungeons are remembered by default. Destroyed portals are removed when their destruction is observed. When revisiting an old portal location, stay nearby briefly while its loaded state is checked. Creatures remain temporary markers.

Line-of-sight detection uses camera, obstacle and mist checks, including mist-clearing effects such as the wisplight. Controls for collected plants, depleted deposits and completed dungeons determine whether those markers remain visible.

## Groups

Enable general grouping and choose whether plants and creatures may group. Overlapping icons group automatically when zooming out and split when zooming in gives them room. **Maximum group span (m)** limits the full group span in the world, from 8 to 800 meters, default 400; it does not force separated screen icons into groups. Existing saved values are preserved. Counts appear in the center of each group icon, which stays anchored to a real object.

**Names → Group creature names** is enabled by default to reduce repeated names. Nearby creatures of the same type share one centered name and count, such as **Leech ×3**, while their marker positions stay unchanged. This works independently of icon grouping. **Creature name group radius (m)** controls the area, default 15 meters. Existing saved choices are preserved; use Reset to apply these defaults. The option respects each map's name visibility setting; selected navigation targets retain their individual names.

Click a group on the large map to reveal its members at their actual locations. Click the group again or an empty map area to close it. Disable plant grouping when you want every plant shown separately.

Favorites and selected navigation targets remain individual markers.

## Presets

The built-in presets are **Default**, **Gathering**, **Exploration** and **Household**. Use **Presets** to load a set, save your current choices under a name or delete a set, including a built-in one.

Presets store only the per-type **Show object**, **Keep discovered** and **Notify** choices in the map filter window. Configuration manager preferences such as sizes, names, grouping, detection radius, creature relationships and sharing stay unchanged. Older presets remain readable; their old configuration preferences are ignored. Resaving an older preset removes those preferences from its stored data.

## Navigation

**Alt + left-click on the large map** to navigate to a MapTracker marker, public player or any map location. Empty map space creates a temporary destination marker. Repeat the gesture on the selected target to stop navigation. Right-click a target to cancel it. Change the selection button or keyboard combination in **Navigation → Select navigation key**; Alt + left-click is the default. Keyboard bindings work while pointing at the large map. Ordinary middle-click keeps the game's ping, and scrolling keeps map zoom. Shared navigation has its own configurable binding in Navigation → Share navigation key, default Alt + middle-click. Dungeon completion states remain unchanged.

For a collapsed group, Alt + left-click reveals the members first; then Alt + left-click the particular object you want. Ordinary clicks retain their group and dungeon actions.

Navigation shows straight-line distance and compass direction below the minimap or in the lower-left corner of the large map. Selecting a destination reveals that one remembered marker without enabling its whole object category.

A gold arrow at the top of the gameplay screen points toward the destination relative to the camera: up means ahead, down means behind, and left/right means turn that way. The destination icon, name and distance appear below the arrow. Portal and object icons use the game catalog, creatures use their trophy or custom head icons, and dungeon destinations use their entrance symbols. Empty map points show their name and distance without a caption icon. Actual object destinations show an icon for personal and shared navigation. It hides while the large map or menus are open. Toggle **Navigation → Show navigation arrow** to hide this HUD without clearing your map destination.

You can also Alt + left-click a player marker on the large map. The destination follows that player's public map position and updates the distance and arrow. Tracking stops if the player disconnects or hides their position; it also ends on arrival or cancellation. Player targets are temporary and use the game's existing player markers.

Navigation ends automatically when you come within **5 meters** of the destination. It does not finish while teleporting or inside a dungeon. Object destinations also account for height to avoid completing on the wrong floor; arbitrary map points use horizontal distance.

Press **F10** to cancel both personal and shared navigation. Right-click an individual target to cancel only that one. Change the key in **Navigation → Cancel navigation key** in the configuration manager. Cancellation is disabled while typing or using menus. Load presets from the **Preset** button in the map filter window.

## Notifications

Notifications are disabled by default. Enable new-dungeon notifications in configuration, choose **Notify** for static types in the catalog.

Notifications require an actual new local discovery. Previously saved places, creatures, loose dropped items and already collected resources do not produce static discovery notifications.

## Multiplayer

Marker sharing is optional and disabled by default. Participating players need MapTracker installed. MapTracker sharing is separate from vanilla cartography-table pins; native/manual pins are not converted into MapTracker discoveries.

**Sharing → Share discoveries** sends static discoveries to other MapTracker users in the same world. Received dungeons, ore, roots, plants and other static objects merge into the normal discovery set. Repeated packets and discoveries of the same object do not create another marker; nearby distinct objects remain separate. Local observations and local dungeon progress take priority.

Enable **Sharing → Other player pins** to display received discoveries, then use your own **Show object** filters. **Keep discovered** retains static discoveries after the sender leaves or you restart. Fresh observations from a teammate can appear outside your detection radius, including another map zone. Receipt and storage do not depend on whether a type is currently displayed. Sending observes your detection distance or sight mode independently of display filters.

**Sharing → Share creature positions** is disabled by default and requires Share discoveries. Enable it to transmit live creature positions, levels and relationships. Receiving uses Other player pins and your own creature filters. Creatures share network identity, so two players seeing the same creature produce one marker, with local observations taking priority. Moving creatures update at most twice per second; unchanged creatures use a periodic heartbeat. Live positions expire after updates stop and are not stored as permanent creature sightings. Received data is never automatically forwarded. This checkbox does not disable explicit shared navigation.

Expanded group lists prefer space to the left of their members and avoid their icons and visible names. The list retains its chosen position and follows the group when dragging the map; pointer movement does not reposition it. Placement is checked after visible names are finalized, using their actual text bounds. Near a map edge or after a real collision the list uses another free area; if none fits, it remains hidden while the expanded markers stay visible.

With **Sharing → Share discoveries** enabled, use **Navigation → Share navigation key** (default **Alt + middle-click**) on any map location or object to share a navigation target. Configure this independently of **Select navigation key**: choose a mouse button, keyboard key or combination, or clear the binding to disable the gesture. Keyboard bindings work while pointing at the large map. A configured mouse gesture replaces that button's normal map action; if both navigation bindings match, sharing has priority. Both sender and recipients must enable Share discoveries and have shared navigation support. Personal navigation stays active. The shared HUD arrow is blue; when both destinations exist, the arrows sit side by side at the top center. Existing object markers are highlighted without a duplicate waypoint. Empty locations or objects not yet available to a recipient use a shared waypoint. Personal targets are orange, shared targets blue, and a target selected both ways uses their blended color. Only one shared destination is active at a time. Available object positions update the shared destination. Right-click its marker to cancel it; arrival also clears it. Shared destinations are temporary and are not forwarded automatically.

## Configuration and saved data

MapTracker's interface and configuration labels are in English. User-entered portal tags retain their original text. Use MapTracker's filters for object choices and an in-game configuration manager for display, discovery, grouping, navigation and notification settings. Each map has independent category visibility.

Inside your BepInEx profile:

- `config/Slikfoul.MapTracker.cfg` — settings.
- `config/MapTracker/presets.json` — custom presets and deleted built-in choices.
- `config/MapTracker/worlds/` — discoveries and favorites for each world and character.

Turning off display does not delete remembered discoveries. Marker density, enabled types and other installed mods can affect performance; a configurable scan budget controls object inspection work per frame.

## Links

- [Source code](https://github.com/slikfoul/MapTracker)
- [Report a problem or suggest a feature](https://github.com/slikfoul/MapTracker/issues)
