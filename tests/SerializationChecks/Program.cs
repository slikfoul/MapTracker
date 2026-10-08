using MapTracker;
using UnityEngine;

static void Check(bool result, string scenario)
{
    if (!result) throw new Exception("FAIL: " + scenario);
    System.Console.WriteLine("PASS: " + scenario);
}
var original = new MarkerFile
{
    world = "123", character = "456",
    markers = new List<MarkerRecord>
    {
        new() { id = "123:42", type = "portal_wood", position = new Vector3(-123.25f, 17.5f, 2048),
            collected = true, visited = true, completed = true, imported = true, shared = true,
            sourcePeer = 987654321012345678, portalTag = "Дом \"Север\"", creatureLevel = 3, creatureRelation = 1 }
    }
};
string json = DataJson.Write(original, true);
var restored = DataJson.Read<MarkerFile>(json);
Check(restored.markers.Count == 1 && restored.markers[0].position.x == -123.25f && restored.markers[0].position.z == 2048, "actual marker DTO preserves list and position coordinates");
Check(restored.markers[0].completed && restored.markers[0].visited && restored.markers[0].collected && restored.markers[0].imported, "dungeon and collection states survive saving");
Check(restored.markers[0].portalTag == original.markers[0].portalTag && restored.markers[0].sourcePeer == original.markers[0].sourcePeer, "Unicode, escaped tags and 64-bit peer identifiers survive");
Check(restored.markers[0].creatureLevel == 3 && restored.markers[0].creatureRelation == 1, "live creature level and relationship survive the network format");
Check(!json.Contains("normalized") && !json.Contains("pin") && !json.Contains("entry") && !json.Contains("character\": {"), "runtime Unity references and recursive Vector3 properties are excluded");
var catalog = new CatalogDocument { entries = new List<CatalogEntry> { new() { key = "ore", prefab = "MineRock_Copper", name = "$ore", category = "Ore", components = new[] { "MineRock" }, products = new[] { "CopperOre" } } }, warnings = new List<string> { "sample" } };
var savedCatalog = DataJson.Read<CatalogDocument>(DataJson.Write(catalog));
Check(savedCatalog.entries.Count == 1 && savedCatalog.entries[0].products.Single() == "CopperOre" && savedCatalog.warnings.Count == 1, "actual catalog DTO preserves entries and nested arrays");
Check(savedCatalog.entries[0].key == "ore" && savedCatalog.entries[0].name == "$ore" && savedCatalog.entries[0].prefab == "MineRock_Copper" && savedCatalog.entries[0].category == "Ore",
    "cached catalog retains the names, type keys and categories needed to restore saved pins during startup");
var old = DataJson.Read<MarkerFile>("{\"schema\":1,\"world\":\"123\",\"character\":\"456\"}");
Check(old.markers != null && old.markers.Count == 0, "old metadata-only saves remain readable");

// Exercise the same grouped filter builder used by the window, with localization
// supplied explicitly so this check does not initialize Unity's localization singleton.
var filterType = typeof(MarkerFile).Assembly.GetType("MapTracker.FilterChoice");
var buildFilters = filterType.GetMethod("Build", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var sampleEntries = new List<CatalogEntry>
{
    new() { key = "skeleton1", name = "Skeleton", category = "Creatures" },
    new() { key = "skeleton2", name = "Skeleton", category = "Creatures" },
    new() { key = "skeletonpoison", name = "Rancid Remains", category = "Creatures" },
    new() { key = "rock1", name = "Stone", category = "Ore" },
    new() { key = "rock2", name = "Stone", category = "Ore" },
    new() { key = "copper", name = "Copper", category = "Ore" },
    new() { key = "stoneitem", name = "Stone", category = "Dropped items" },
    new() { key = "projectile", name = "projectile", category = "Other", technical = true }
};
var choices = ((System.Collections.IEnumerable)buildFilters.Invoke(null, new object[] { sampleEntries, new Func<CatalogEntry, string>(e => e.name) })).Cast<object>().ToList();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var membersField = filterType.GetField("Members", flags);
var labelField = filterType.GetField("Label", flags);
Check(choices.Count == 5, "duplicate names merge, distinct creatures/resources/categories remain separate, technical effects are excluded");
var skeletonChoice = choices.Single(c => (string)labelField.GetValue(c) == "Skeleton");
var skeletonMembers = (List<CatalogEntry>)membersField.GetValue(skeletonChoice);
Check(skeletonMembers.Count == 2 && skeletonMembers.Any(e => e.key == "skeleton2"), "grouped checkbox retains every prefab variant");
var dungeonNamesType = typeof(MarkerFile).Assembly.GetType("MapTracker.DungeonNames");
var dungeonNameMethod = dungeonNamesType.GetMethod("Get", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
string DungeonName(string prefab) => (string)dungeonNameMethod.Invoke(null, new object[] { prefab, false });
var dungeonSamples = new List<CatalogEntry>
{
    new() { key = "mountain", prefab = "MountainCave02", name = "Cave", category = "Dungeons", dungeon = true },
    new() { key = "troll", prefab = "TrollCave02", name = "Cave", category = "Dungeons", dungeon = true },
    new() { key = "mine1", prefab = "Mistlands_DvergrTownEntrance1", name = "Cave", category = "Dungeons", dungeon = true },
    new() { key = "mine2", prefab = "Mistlands_DvergrTownEntrance2", name = "Cave", category = "Dungeons", dungeon = true }
};
var dungeonChoices = ((System.Collections.IEnumerable)buildFilters.Invoke(null, new object[] { dungeonSamples, new Func<CatalogEntry, string>(e => DungeonName(e.prefab)) })).Cast<object>().ToList();
Check(dungeonChoices.Count == 3 && DungeonName("MountainCave02") == "Frost Caves" && DungeonName("Mistlands_DvergrTownEntrance1") == "Infested Mines",
    "generic Cave entrance text does not merge different dungeon types; mine layout variants share one filter");
Check(DungeonName("ModdedDungeon") == null && DungeonName("Hildir_cave") != DungeonName("MountainCave02"),
    "unknown dungeons retain their game definition; Hildir caves remain distinct from Frost Caves");
var visibilityType = typeof(MarkerFile).Assembly.GetType("MapTracker.DiscoveryVisibility");
var visibilityMethod = visibilityType.GetMethod("Show", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var keepDefaultMethod = visibilityType.GetMethod("KeepByDefault", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
bool KeepsByDefault(bool dungeon, string category) => (bool)keepDefaultMethod.Invoke(null, new object[] { dungeon, category });
Check(KeepsByDefault(true, "Dungeons") && KeepsByDefault(false, "Portals") &&
    new[] { "Ore", "Plants and pickables", "Containers", "Player structures", "Locations", "Trees", "Other" }.All(category => !KeepsByDefault(false, category)),
    "only dungeon entrances and portals retain remembered markers by default");
bool VisibleDiscovery(bool keep, bool loaded, bool remote, float radius, float distance) =>
    (bool)visibilityMethod.Invoke(null, new object[] { keep, loaded, remote, radius, distance });
Check(VisibleDiscovery(true, false, false, 50, 1000) && !VisibleDiscovery(false, false, false, 0, 0),
    "remembered portals remain after leaving; nearby-only discoveries hide when unloaded");
Check(VisibleDiscovery(false, true, false, 50, 50) && !VisibleDiscovery(false, true, false, 50, 51) && VisibleDiscovery(false, true, false, 0, 1000),
    "nearby-only visibility follows the detection radius, including its boundary and zero limit");
Check(!VisibleDiscovery(false, false, true, 0, 1) && VisibleDiscovery(true, false, true, 50, 1000),
    "remote discoveries obey the per-type memory choice and are not mistaken for client-loaded objects");
var sightVisibilityMethod = visibilityType.GetMethod("ShowSight", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
bool VisibleSight(bool keep, bool seen, bool remote) => (bool)sightVisibilityMethod.Invoke(null, new object[] { keep, seen, remote });
Check(VisibleSight(false, true, false) && !VisibleSight(false, false, false) && VisibleSight(true, false, false),
    "line-of-sight discoveries require visibility unless a local remembered marker is enabled");
Check(!VisibleSight(false, true, true) && !VisibleSight(true, false, true),
    "received remote markers cannot bypass the local line-of-sight mode");
var radiusType = typeof(MarkerFile).Assembly.GetType("MapTracker.RadiusSetting");
var radiusBind = radiusType.GetMethod("Bind", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var radiusDistance = radiusType.GetMethod("Distance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
string radiusCheckPath = Path.Combine(AppContext.BaseDirectory, "radius-config-check.cfg");
foreach (var scenario in new[] { (Previous: "123.456", Expected: 123), (Previous: "0", Expected: 301), (Previous: "1024", Expected: 300), (Previous: "301", Expected: 300) })
{
    File.WriteAllText(radiusCheckPath, "[General]\nDetection radius = " + scenario.Previous + "\n");
    var radiusConfig = new BepInEx.Configuration.ConfigFile(radiusCheckPath, false) { SaveOnConfigSet = false };
    var radiusEntry = (BepInEx.Configuration.ConfigEntry<int>)radiusBind.Invoke(null, new object[] { radiusConfig, null });
    Check(radiusEntry.Value == scenario.Expected, "legacy radius " + scenario.Previous + " converts to an integer without losing infinity semantics");
    radiusConfig.Save();
    var radiusReloaded = new BepInEx.Configuration.ConfigFile(radiusCheckPath, false) { SaveOnConfigSet = false };
    var restoredRadius = (BepInEx.Configuration.ConfigEntry<int>)radiusBind.Invoke(null, new object[] { radiusReloaded, null });
    Check(restoredRadius.Value == scenario.Expected, "converted radius survives a subsequent configuration reload");
}
Check((float)radiusDistance.Invoke(null, new object[] { 1 }) == 1f && (float)radiusDistance.Invoke(null, new object[] { 300 }) == 300f && (float)radiusDistance.Invoke(null, new object[] { 301 }) == 0f,
    "slider increases from one meter to 300 meters, with client maximum only at its right endpoint");
File.WriteAllText(radiusCheckPath, "[Internal]\nRadius slider revision = 2\n[General]\nDetection radius = 0\n");
var sightRadiusConfig = new BepInEx.Configuration.ConfigFile(radiusCheckPath, false) { SaveOnConfigSet = false };
var sightRadiusEntry = (BepInEx.Configuration.ConfigEntry<int>)radiusBind.Invoke(null, new object[] { sightRadiusConfig, null });
Check(sightRadiusEntry.Value == 0 && sightRadiusEntry.Description.AcceptableValues.IsValid(0) &&
    (float)radiusDistance.Invoke(null, new object[] { 0 }) == 0f, "the new leftmost zero is line-of-sight, with no artificial meter cap");
sightRadiusConfig.Save();
var sightRadiusReloaded = new BepInEx.Configuration.ConfigFile(radiusCheckPath, false) { SaveOnConfigSet = false };
Check(((BepInEx.Configuration.ConfigEntry<int>)radiusBind.Invoke(null, new object[] { sightRadiusReloaded, null })).Value == 0,
    "line-of-sight selection survives a restart without becoming infinity");
File.WriteAllText(radiusCheckPath, "[Internal]\nRadius slider revision = 1\n[General]\nDetection radius = 0\n");
var oldZeroConfig = new BepInEx.Configuration.ConfigFile(radiusCheckPath, false) { SaveOnConfigSet = false };
Check(((BepInEx.Configuration.ConfigEntry<int>)radiusBind.Invoke(null, new object[] { oldZeroConfig, null })).Value == 301,
    "a legacy zero still migrates to unlimited rather than opting the user into sight detection");
if (args.Length > 0)
{
    var actualCatalog = DataJson.Read<CatalogDocument>(File.ReadAllText(args[0]));
    var actualChoices = ((System.Collections.IEnumerable)buildFilters.Invoke(null, new object[] { actualCatalog.entries, new Func<CatalogEntry, string>(e => e.name ?? e.prefab) })).Cast<object>().ToList();
    var identities = new HashSet<string>();
    int retained = 0;
    foreach (var choice in actualChoices)
    {
        var members = (List<CatalogEntry>)membersField.GetValue(choice);
        if (!identities.Add(members[0].category + "\n" + (string)labelField.GetValue(choice))) throw new Exception("Duplicate grouped identity in installed catalog.");
        retained += members.Count;
    }
    Check(retained == actualCatalog.entries.Count(e => !e.technical), "all filterable prefab definitions are retained in the full installed catalog");
    Check(true, "no duplicate row identities across the full installed catalog");
    System.Console.WriteLine("Catalog audit: " + actualCatalog.entries.Count + " definitions -> " + actualChoices.Count + " grouped choices.");
}

// Use the actual game-bundled JSON and production store, including atomic overwrite.
var builtInSamples = new List<CatalogEntry>
{
    new() { key = "portal", category = "Portals" },
    new() { key = "dungeon", category = "Dungeons", dungeon = true },
    new() { key = "enemy", category = "Creatures", creature = true },
    new() { key = "copper", category = "Ore", products = new[] { "CopperOre", "Stone" } },
    new() { key = "plainRock", category = "Ore", products = new[] { "Stone" } },
    new() { key = "camp", category = "Locations" },
    new() { key = "trader", category = "Other", components = new[] { "Trader" } },
    new() { key = "altar", category = "Other", components = new[] { "OfferingBowl" } },
    new() { key = "ownHive", category = "Player structures", components = new[] { "Beehive" } },
    new() { key = "sap", category = "Sap collectors" },
    new() { key = "raspberry", prefab = "RaspberryBush", category = "Plants and pickables", products = new[] { "Raspberry" } },
    new() { key = "blueberry", prefab = "BlueberryBush", category = "Plants and pickables" },
    new() { key = "thistle", prefab = "Pickable_Thistle", category = "Plants and pickables" },
    new() { key = "dandelion", prefab = "Pickable_Dandelion", category = "Plants and pickables" },
    new() { key = "mushroom", prefab = "Pickable_Mushroom_JotunPuffs", category = "Plants and pickables" },
    new() { key = "modHerb", prefab = "ModPlant", category = "Plants and pickables", products = new[] { "SageHerb" } },
    new() { key = "beehive", category = "Wild beehives" },
    new() { key = "looseBerry", prefab = "Raspberry", category = "Dropped items" },
    new() { key = "stone", prefab = "Pickable_Stone", category = "Plants and pickables" },
    new() { key = "treasure", prefab = "Pickable_DvergrMineTreasure", category = "Plants and pickables", products = new[] { "Coins" } },
    new() { key = "technical", prefab = "vfx_bush", category = "Plants and pickables", technical = true }
};
var defaultPreset = BuiltInFilterPresets.Build(builtInSamples, BuiltInFilterPreset.Default);
Check(defaultPreset.types.Where(p => p.Value.selected).Select(p => p.Key).Order().SequenceEqual(new[] { "copper", "dungeon", "enemy", "portal" }) &&
    defaultPreset.types["portal"].keep && defaultPreset.types["dungeon"].keep && !defaultPreset.types["enemy"].keep,
    "Default includes useful ore and hostile creatures, remembers portals and dungeons, and excludes ordinary stone");
var gatheringPreset = BuiltInFilterPresets.Build(builtInSamples, BuiltInFilterPreset.Gathering);
Check(gatheringPreset.types.Where(p => p.Value.selected).Select(p => p.Key).Order().SequenceEqual(
    new[] { "beehive", "blueberry", "dandelion", "modHerb", "mushroom", "raspberry", "thistle" }) &&
    gatheringPreset.types["beehive"].keep && builtInSamples.Where(e => e.category == "Plants and pickables").All(e => !gatheringPreset.types[e.key].keep) &&
    !gatheringPreset.hostile && gatheringPreset.groupPlants == false,
    "Gathering shows botanical pickables nearby without remembering them, retains wild beehives, and excludes loose items and effects");
var explorationPreset = BuiltInFilterPresets.Build(builtInSamples, BuiltInFilterPreset.Exploration);
Check(explorationPreset.types.Where(p => p.Value.selected).Select(p => p.Key).Order().SequenceEqual(
    new[] { "altar", "camp", "dungeon", "portal", "trader" }) && explorationPreset.types.All(p => p.Value.keep == p.Value.selected),
    "Exploration remembers locations, dungeon entrances, portals, traders and offering altars");
var householdPreset = BuiltInFilterPresets.Build(builtInSamples, BuiltInFilterPreset.Household);
Check(householdPreset.types.Where(p => p.Value.selected).Select(p => p.Key).Order().SequenceEqual(
    new[] { "enemy", "ownHive", "portal", "sap" }) && householdPreset.tamed && !householdPreset.hostile &&
    !householdPreset.friendly && !householdPreset.animals && !householdPreset.types["enemy"].keep,
    "Household shows tamed creatures only, with remembered built beehives, sap collectors and portals");
string presetCheckFolder = Path.Combine(AppContext.BaseDirectory, "MapTrackerPresetChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(presetCheckFolder);
try
{
    string sectionsCheckPath = Path.Combine(presetCheckFolder, "sections.cfg");
    File.WriteAllText(sectionsCheckPath, "[General]\nMinimap = false\nOther player pins = false\nGroup distance = 19\n[Display]\nCreature icons = false\nHide large map names = true\n");
    var sectionsConfig = new BepInEx.Configuration.ConfigFile(sectionsCheckPath, false) { SaveOnConfigSet = false };
    var sectionDescription = new BepInEx.Configuration.ConfigDescription("test");
    var movedMap = ConfigSections.Bind(sectionsConfig, "General", "Maps", "Minimap", true, sectionDescription);
    var movedShared = ConfigSections.Bind(sectionsConfig, "General", "Sharing", "Other player pins", true, sectionDescription);
    var movedIcons = ConfigSections.Bind(sectionsConfig, "Display", "Icons", "Creature icons", true, sectionDescription);
    var miniIcons = ConfigSections.CopyIfMissing(sectionsConfig, movedIcons, "Minimap creature icons", true, sectionDescription);
    Check(!miniIcons.Value && (bool)miniIcons.DefaultValue,
        "splitting creature icon controls preserves the previous disabled choice with a true reset default");
    var movedNames = ConfigSections.Bind(sectionsConfig, "Display", "Names", "Hide large map names", false, sectionDescription);
    var movedGroupDistance = ConfigSections.Bind(sectionsConfig, "General", "Grouping", "Group distance", 36, sectionDescription);
    Check(!movedMap.Value && !movedShared.Value && !movedIcons.Value && movedNames.Value && movedGroupDistance.Value == 19 &&
        (bool)movedMap.DefaultValue && (int)movedGroupDistance.DefaultValue == 36,
        "section reorganization preserves disabled choices and integer settings while retaining correct reset defaults");
    movedIcons.Value = true;
    Check(!miniIcons.Value && movedIcons.Value, "large-map icons can be enabled while minimap retains dots");
    sectionsConfig.Save();
    var sectionsReloaded = new BepInEx.Configuration.ConfigFile(sectionsCheckPath, false) { SaveOnConfigSet = false };
    var restoredLargeIcons = ConfigSections.Bind(sectionsReloaded, "Display", "Icons", "Creature icons", true, sectionDescription);
    var restoredMiniIcons = ConfigSections.CopyIfMissing(sectionsReloaded, restoredLargeIcons, "Minimap creature icons", true, sectionDescription);
    Check(restoredLargeIcons.Value && !restoredMiniIcons.Value,
        "independent minimap and large-map icon modes survive a restart without recopying the legacy choice");
    Check(!ConfigSections.Bind(sectionsReloaded, "General", "Maps", "Minimap", true, sectionDescription).Value &&
        ConfigSections.Bind(sectionsReloaded, "General", "Grouping", "Group distance", 36, sectionDescription).Value == 19 &&
        !File.ReadAllText(sectionsCheckPath).Contains("[Display]") && !File.ReadAllText(sectionsCheckPath).Contains("[General]"),
        "moved settings survive restart and old sections do not leave duplicate controls");
    File.WriteAllText(sectionsCheckPath, "[General]\nMinimap = false\n[Maps]\nMinimap = true\n");
    var duplicatedSections = new BepInEx.Configuration.ConfigFile(sectionsCheckPath, false) { SaveOnConfigSet = false };
    Check(ConfigSections.Bind(duplicatedSections, "General", "Maps", "Minimap", true, sectionDescription).Value,
        "a setting saved in the new section takes precedence over a stale old-section value");
    string presetCheckPath = Path.Combine(presetCheckFolder, "presets.json");
    var presetStore = new FilterPresetStore(presetCheckPath);
    Check(presetStore.Read().Count == 0, "new profile has no custom presets");
    presetStore.DeleteBuiltIn(BuiltInFilterPreset.Gathering);
    Check(new FilterPresetStore(presetCheckPath).BuiltInDeleted(BuiltInFilterPreset.Gathering) &&
        !presetStore.BuiltInDeleted(BuiltInFilterPreset.Default),
        "built-in deletion survives reload without removing other built-ins");
    presetStore.Save(new FilterPreset { name = "  Руда / север  ", hostile = false, animals = true,
        groupPlants = false,
        types = new() { ["prefab:MineRock_Copper"] = new() { selected = true, keep = false },
            ["prefab:portal_wood"] = new() { selected = false, keep = true } } });
    var savedPreset = new FilterPresetStore(presetCheckPath).Read().Single();
    Check(savedPreset.name == "Руда / север" && savedPreset.types["prefab:MineRock_Copper"].selected &&
        !savedPreset.types["prefab:MineRock_Copper"].keep && savedPreset.types["prefab:portal_wood"].keep &&
        !savedPreset.hostile && savedPreset.animals && savedPreset.groupPlants == false,
        "custom preset preserves type, relationship and plant grouping filters across store reload");
    presetStore.Save(new FilterPreset { name = "руда / СЕВЕР", friendly = true });
    Check(presetStore.Read().Count == 1 && presetStore.Read()[0].friendly && File.Exists(presetCheckPath + ".bak"),
        "saving the same name replaces the preset atomically and retains a backup");
    presetStore.Save(new FilterPreset { name = "Данжи" });
    Check(presetStore.Delete("РУДА / север") && presetStore.Read().Single().name == "Данжи" && !presetStore.Delete("missing"),
        "deletion removes only the selected preset, with case-insensitive names");
    Check(new FilterPresetStore(presetCheckPath).BuiltInDeleted(BuiltInFilterPreset.Gathering),
        "custom preset save, overwrite and deletion do not resurrect a deleted built-in");
    bool invalidNameRejected = false;
    try { presetStore.Save(new FilterPreset { name = " " }); } catch (ArgumentException) { invalidNameRejected = true; }
    Check(invalidNameRejected && presetStore.Read().Count == 1, "blank names cannot alter stored presets");
    File.WriteAllText(presetCheckPath, "{\"schema\":1,\"presets\":[{\"name\":\"Legacy\",\"types\":{}}]}");
    Check(presetStore.Read().Single().groupPlants == null && !presetStore.BuiltInDeleted(BuiltInFilterPreset.Gathering),
        "legacy preset documents remain readable without changing existing plant grouping");
    File.WriteAllText(presetCheckPath, "{invalid");
    bool corruptRejected = false;
    try { presetStore.Save(new FilterPreset { name = "new" }); } catch { corruptRejected = true; }
    Check(corruptRejected && File.ReadAllText(presetCheckPath) == "{invalid", "invalid storage is preserved rather than silently replaced");
}
finally
{
    foreach (var presetCheckFile in Directory.GetFiles(presetCheckFolder)) File.Delete(presetCheckFile);
    Directory.Delete(presetCheckFolder);
}
