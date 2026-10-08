# MapTracker

Client-side Valheim map tracker for BepInEx and Harmony. Development preview **0.1.23**.

MapTracker displays discovered objects and creatures on the native minimap and large map. It does not require Jotunn or a server plugin. Current builds are development previews; multiplayer sharing and performance on crowded maps still need in-game testing.

## Features

- Searchable object catalog built from the installed game and mods, with category filters and grouped prefab variants.
- Per-type **Show object** and **Keep discovered** choices. Portals and dungeons are remembered by default; creature positions remain temporary.
- Separate dungeon types, entrance icons, visited colors and click-to-toggle completion. Portal tags are displayed, and grouped portal names open on click.
- Independent creature icon switches and size settings for each map. Trophy heads or relationship-colored dots, optional silhouette outlines, stars and group counts.
- Independent name visibility settings. Minimap creature names use a configurable distance of 5–200 meters, default 60; large-map names follow zoom and remain available on hover when hidden.
- Discovery radius: line of sight, whole meters, or the maximum client-loaded area. Sight detection checks the camera, obstacles and the game's mist visibility API, including demister effects.
- Optional grouping, with separate plant and creature switches. Group distance uses screen pixels, so zooming out covers more world space. Disable plant grouping for exact plant locations.
- Built-in Default, Gathering, Exploration and Household presets, plus named custom presets. Presets can be saved, loaded and deleted, including built-in presets.
- Optional sharing between MapTracker clients, disabled by default. Received discoveries are not forwarded. MapTracker markers are separate from native cartography-table pins.
- Collected plants, depleted ore and manually completed dungeons have separate visibility settings. Actual observed plant regrowth restores a marker.
- Original transparent head icons for bats, hens, chicks and young boars, wolves, asksvins and loxes, embedded in the DLL.
- Dense-map label placement uses spatial lookup, reusable buffers and cached layouts. Individually moving markers and scale changes invalidate the cache. FPS improvements still need in-game verification.

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
