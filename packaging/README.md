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
- Built-in and custom presets that can be saved, loaded and deleted. New custom presets also store map presentation settings.
- A **Discoveries** browser with search, persistent favorites and navigation targets.
- Optional notifications for newly discovered dungeons and selected static object types.
- Optional marker sharing between MapTracker clients.

## Installation

**Mod manager:** install MapTracker in your Valheim profile. The required BepInEx dependency is declared in the package.

**Manual:** install BepInEx for Valheim, then copy the package's `BepInEx` folder into your Valheim game folder.

An in-game BepInEx configuration manager is optional and useful for adjusting display, discovery and grouping settings. The object filters and discovery browser are available through MapTracker's own interface.

## Getting started

1. Enter a world and let MapTracker collect the object catalog.
2. Open the large map and click the **gear button** to open filters.
3. Search for an object or choose a category. Enable **Show object** for the types you want to track.
4. Enable **Keep discovered** for static types whose known locations should remain visible after you leave.

The catalog search selects object types. **Discoveries** searches recorded locations, including portal tags.

## Creature markers

Choose icons or dots independently for each map in the **Minimap** and **Large map** configuration sections. Icon sizes, solid black silhouette outline thickness, name visibility and minimap name distance are configurable. Set creature and other object font sizes separately under **Names**. Stars and group counts remain available in either mode.

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

For fewer repeated names, enable **Names → Group creature names**. Nearby creatures of the same type share one centered name and count, such as **Leech ×3**, while their marker positions stay unchanged. This works independently of icon grouping. **Creature name group radius (m)** controls the area, default 15 meters. The option is off by default and respects each map's name visibility setting; selected navigation targets retain their individual names.

Click a group on the large map to reveal its members at their actual locations. Click the group again or an empty map area to close it. Disable plant grouping when you want every plant shown separately.

Favorites and selected navigation targets remain individual markers.

## Presets

The built-in presets are **Default**, **Gathering**, **Exploration** and **Household**. Use **Presets** to load a set, save your current choices under a name or delete a set, including a built-in one.

New custom presets store object selections, retention choices, creature relations and map presentation settings. Older presets remain readable and keep your current presentation settings. Presets do not change sharing permissions or disable the mod.

## Discoveries and navigation

**Alt + left-click on the large map** to navigate to a MapTracker marker or any map location. Empty map space creates a temporary destination marker. Alt-click the selected object or destination marker again to stop navigation. Dungeon completion states remain unchanged.

For a collapsed group, Alt-click reveals the members first; then Alt-click the particular object you want. Ordinary clicks retain their group and dungeon actions.

Open **Discoveries** from the filter window. Search by name, object type or portal tag, or enable **Favorites only**.

- **Fav:** toggle a persistent favorite.
- **Map:** center the large map on the place and select it as a destination.
- **Target:** select a destination without moving the map.
- **Clear target:** stop navigation.

Navigation shows straight-line distance and compass direction below the minimap or in the corner of the large map. Selecting a destination reveals that one remembered marker without enabling its whole object category.

A gold arrow at the top of the gameplay screen points toward the destination relative to the camera: up means ahead, down means behind, and left/right means turn that way. The destination name and distance appear below the arrow. It hides while the large map or menus are open. Toggle **Navigation → Show navigation arrow** to hide this HUD without clearing your map destination.

You can also Alt-click a player marker on the large map. The destination follows that player's public map position and updates the distance and arrow. Tracking stops if the player disconnects or hides their position; it also ends on arrival or cancellation. Player targets are temporary and use the game's existing player markers.

Navigation ends automatically when you come within **5 meters** of the destination. It does not finish while teleporting or inside a dungeon. Object destinations also account for height to avoid completing on the wrong floor; arbitrary map points use horizontal distance.

Press **F10** to cancel navigation, or use **Clear target** in Discoveries. Change the key in **Navigation → Cancel navigation key** in the configuration manager. Cancellation is disabled while typing or using menus. The **Next preset** button cycles available presets.

## Notifications

Notifications are disabled by default. Enable new-dungeon notifications in configuration, choose **Notify** for static types in the catalog, or use **Notify** in Discoveries.

Notifications require an actual new local discovery. Previously saved places, creatures, loose dropped items and already collected resources do not produce static discovery notifications.

## Multiplayer

Marker sharing is optional and disabled by default. Participating players need MapTracker installed. MapTracker sharing is separate from vanilla cartography-table pins; native/manual pins are not converted into MapTracker discoveries.

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
