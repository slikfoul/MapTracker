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

// Run the production lifetime transitions without the Unity scene/Harmony bootstrap.
var lifetimeType = typeof(MarkerFile).Assembly.GetType("MapTracker.MarkerLifetime");
var lifetimeFlags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var destroyedMethod = lifetimeType.GetMethod("Destroyed", lifetimeFlags);
var cleanupMethod = lifetimeType.GetMethod("RemoveDestroyedPortals", lifetimeFlags);
var recordFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var recordEntry = typeof(MarkerRecord).GetField("entry", recordFlags);
var recordDynamic = typeof(MarkerRecord).GetField("dynamic", recordFlags);
var tracked = new Dictionary<string, MarkerRecord>();
var removedPins = new List<string>();
Action<MarkerRecord> removePin = r => removedPins.Add(r.id);
MarkerRecord Track(string id, string category, bool collected = false, bool dynamic = false)
{
    var record = new MarkerRecord { id = id, type = "prefab:test" + id, collected = collected };
    recordEntry.SetValue(record, new CatalogEntry { category = category });
    recordDynamic.SetValue(record, dynamic);
    tracked.Add(id, record);
    return record;
}
bool Destroy(string id) => (bool)destroyedMethod.Invoke(null, new object[] { tracked, id, removePin });
Track("123:1", "Portals");
bool destructionDirty = Destroy("123:1");
Check(tracked.Count == 0 && destructionDirty && removedPins.Contains("123:1"),
    "confirmed portal destruction removes both discovery and pin, and schedules persistent saving");
var unloadedPortal = Track("123:2", "Portals"); // live defaults to false, as after unloading.
var oldGhost = Track("123:3", "Portals", collected: true);
var gatheredPlant = Track("123:4", "Plants and pickables", collected: true);
bool cleanupDirty = (bool)cleanupMethod.Invoke(null, new object[] { tracked, removePin });
Check(cleanupDirty && !tracked.ContainsKey(oldGhost.id) && tracked.ContainsKey(unloadedPortal.id) && tracked.ContainsKey(gatheredPlant.id),
    "legacy cleanup removes only confirmed destroyed portals, preserving unloaded portals and harvested plants");
Check(!(bool)cleanupMethod.Invoke(null, new object[] { tracked, removePin }), "repeating portal cleanup does not request another save");
var remainingSave = DataJson.Read<MarkerFile>(DataJson.Write(new MarkerFile { markers = tracked.Values.ToList() }));
Check(remainingSave.markers.Any(m => m.id == unloadedPortal.id) && remainingSave.markers.All(m => m.id != oldGhost.id && m.id != "123:1"),
    "removed portals stay absent in the saved marker format while unloaded portals survive restart");
var plantRecord = Track("123:5", "Plants and pickables");
Check(Destroy(plantRecord.id) && tracked.ContainsKey(plantRecord.id) && plantRecord.collected,
    "destroyed pickables still use collected-state visibility instead of the portal removal rule");
var creatureRecord = Track("123:6", "Creatures", dynamic: true);
Check(!Destroy(creatureRecord.id) && !tracked.ContainsKey(creatureRecord.id) && removedPins.Contains(creatureRecord.id),
    "dynamic creature destruction removes its pin without scheduling a static discovery save");
int beforeUnknown = tracked.Count;
Check(!Destroy("123:99") && tracked.Count == beforeUnknown, "untracked destruction leaves existing discoveries untouched");

// Regression: one real entrance persisted with two different network IDs.
var dungeonDiscoveries = typeof(MarkerFile).Assembly.GetType("MapTracker.DungeonDiscoveries");
var dungeonCleanup = dungeonDiscoveries.GetMethod("RemoveDuplicates", lifetimeFlags);
var dungeonRebind = dungeonDiscoveries.GetMethod("Rebind", lifetimeFlags);
var mineEntry = new CatalogEntry { key = "location:Mistlands_DvergrTownEntrance2", category = "Dungeons", dungeon = true };
var entrancePosition = new Vector3(4671.713f, 53.56597f, -6144.693f);
var dungeonRecords = new Dictionary<string, MarkerRecord>();
MarkerRecord RememberDungeon(string id, CatalogEntry entry, Vector3 position, bool shared = false)
{
    var record = new MarkerRecord { id = id, type = entry.key, position = position, shared = shared };
    recordEntry.SetValue(record, entry); dungeonRecords.Add(id, record); return record;
}
bool CleanDungeons() => (bool)dungeonCleanup.Invoke(null, new object[] { dungeonRecords, removePin });
MarkerRecord RebindDungeon(string id, CatalogEntry entry, Vector3 position)
{
    var record = (MarkerRecord)dungeonRebind.Invoke(null, new object[] { dungeonRecords, id, entry, position, removePin });
    if (record != null) dungeonRecords.Add(id, record); // Same insertion as Observe.
    return record;
}
var previousMine = RememberDungeon("1310990846:1108223", mineEntry, entrancePosition);
previousMine.completed = true; previousMine.visited = true; previousMine.favorite = true;
RememberDungeon("1:1051203", mineEntry, entrancePosition);
Check(CleanDungeons() && dungeonRecords.Count == 1 && dungeonRecords.Values.Single().completed && dungeonRecords.Values.Single().visited && dungeonRecords.Values.Single().favorite,
    "duplicate dungeon IDs at the reported entrance collapse to one marker and retain completion/visit state");
Check(removedPins.Contains(dungeonRecords.ContainsKey(previousMine.id) ? "1:1051203" : previousMine.id) && !CleanDungeons(),
    "duplicate pins are retired and repeated cleanup is idempotent");
var mineSave = DataJson.Read<MarkerFile>(DataJson.Write(new MarkerFile { markers = dungeonRecords.Values.ToList() }));
Check(mineSave.markers.Count == 1 && mineSave.markers[0].completed && mineSave.markers[0].visited,
    "cleaned dungeon discoveries and their merged progress survive production serialization");
var reboundMine = RebindDungeon("99:42", mineEntry, entrancePosition);
Check(reboundMine != null && dungeonRecords.Count == 1 && dungeonRecords.ContainsKey("99:42") && reboundMine.completed && reboundMine.visited && reboundMine.favorite,
    "rediscovery with a new network ID rebinds a native dungeon instead of creating a second marker");
RememberDungeon("nearby-mine", mineEntry, entrancePosition + new Vector3(3, 0, 0));
var otherEntry = new CatalogEntry { key = "location:SunkenCrypt4", category = "Dungeons", dungeon = true };
RememberDungeon("other-kind", otherEntry, entrancePosition);
RememberDungeon("other-floor", mineEntry, entrancePosition + new Vector3(0, 5, 0));
RememberDungeon("shared-mine", mineEntry, entrancePosition, shared: true);
RememberDungeon("unresolved", new CatalogEntry { key = "unresolved" }, entrancePosition);
var mobEntry = new CatalogEntry { key = "prefab:Dverger", category = "Creatures", creature = true };
RememberDungeon("mob-1", mobEntry, entrancePosition);
RememberDungeon("mob-2", mobEntry, entrancePosition);
Check(!CleanDungeons() && dungeonRecords.Count == 8,
    "nearby distinct entrances, dungeon kinds, floors, remote records and actual creatures remain separate");
Check(RebindDungeon("unknown-kind", new CatalogEntry { key = "location:OtherDungeon", dungeon = true }, entrancePosition) == null,
    "new observations cannot refine a known dungeon into a different type");
var legacyEntry = new CatalogEntry { key = "legacy:Cave", category = "Dungeons", dungeon = true };
var legacyDungeon = RememberDungeon("legacy-import", legacyEntry, new Vector3(10, 1, 20));
legacyDungeon.imported = true; legacyDungeon.completed = true;
var refinedDungeon = RebindDungeon("live-import", mineEntry, new Vector3(10.5f, 1, 20));
Check(refinedDungeon == legacyDungeon && refinedDungeon.type == mineEntry.key && refinedDungeon.completed && !refinedDungeon.imported &&
    !dungeonRecords.ContainsKey("legacy-import") && removedPins.Contains("legacy-import"),
    "legacy entrance imports refine to an observed native type without losing progress or keeping the old pin");
var extraLegacy = RememberDungeon("extra-legacy", legacyEntry, entrancePosition);
extraLegacy.imported = true; extraLegacy.visited = true;
Check(CleanDungeons() && !dungeonRecords.ContainsKey(extraLegacy.id) && dungeonRecords["99:42"].type == mineEntry.key &&
    dungeonRecords["99:42"].visited && dungeonRecords.ContainsKey("other-kind"),
    "legacy/native duplicates keep the specific native entrance while preserving other known dungeon kinds");

var reconciliationType = typeof(MarkerFile).Assembly.GetType("MapTracker.PortalReconciliation");
var reconciliationCheck = reconciliationType.GetMethod("Check", recordFlags);
var reconciliationClear = reconciliationType.GetMethod("Clear", recordFlags);
var reconciliation = Activator.CreateInstance(reconciliationType, true);
bool Reconcile(string id, bool ready, bool exists, float time) =>
    (bool)reconciliationCheck.Invoke(reconciliation, new object[] { id, ready, exists, time });
Check(!Reconcile("ghost", true, false, 0), "one ready-area absence cannot remove an old portal");
bool prematurelyRemoved = false;
for (int time = 1; time < 12; time++) prematurelyRemoved |= Reconcile("ghost", true, false, time);
Check(!prematurelyRemoved && Reconcile("ghost", true, false, 12),
    "a continuously missing portal is retired only after twelve seconds of ready-area checks");
bool protectedPortals = true;
for (int time = 0; time < 20; time++)
    protectedPortals &= !Reconcile("unloaded", false, false, time) && !Reconcile("real", true, true, time);
Check(protectedPortals, "repeated checks preserve both unloaded areas and existing network portals");
for (int time = 0; time < 11; time++) Reconcile("late-arrival", true, false, time);
Check(!Reconcile("late-arrival", true, true, 11) && !Reconcile("late-arrival", true, false, 12),
    "a portal arriving during synchronization cancels previous missing evidence");
for (int time = 0; time < 11; time++) Reconcile("left-area", true, false, time);
Check(!Reconcile("left-area", false, false, 11) && !Reconcile("left-area", true, false, 20),
    "leaving range, unloading or teleporting resets the missing-portal observation window");
Reconcile("paused", true, false, 0); Reconcile("paused", true, false, 1);
Check(!Reconcile("paused", true, false, 20), "a long gap between checks restarts validation rather than deleting on resume");
for (int time = 0; time < 11; time++) Reconcile("world-change", true, false, time);
reconciliationClear.Invoke(reconciliation, null);
Check(!Reconcile("world-change", true, false, 30), "world changes clear pending portal reconciliation");

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
    string initialSettingsPath = Path.Combine(presetCheckFolder, "initial-settings.cfg");
    var initialSettings = new BepInEx.Configuration.ConfigFile(initialSettingsPath, false) { SaveOnConfigSet = false };
    var initialDescription = new BepInEx.Configuration.ConfigDescription("initial settings test");
    var defaultLargeIcons = DefaultSettings.Bind(initialSettings, "Display", "Icons", "Creature icons", false, initialDescription);
    var defaultMiniIcons = ConfigSections.CopyIfMissing(initialSettings, defaultLargeIcons, "Minimap creature icons",
        DefaultSettings.Value("Icons", "Minimap creature icons", true), initialDescription, copySourceIfMissing: false);
    Check(defaultLargeIcons.Value && !defaultMiniIcons.Value && !(bool)defaultMiniIcons.DefaultValue,
        "new profiles use large-map icons and minimap dots, with matching reset defaults");
    var defaultGroupSpan = DefaultSettings.Bind(initialSettings, "General", "Grouping", "Group distance", 400, initialDescription);
    Check(defaultGroupSpan.Value == 400 && (int)defaultGroupSpan.DefaultValue == 400,
        "new profiles and the reset button use a four-hundred-meter group span");
    Check(DefaultSettings.Bind(initialSettings, "Display", "Names", "Hide minimap names", false, initialDescription).Value &&
        DefaultSettings.Bind(initialSettings, "Names", "Names", "Minimap name distance", 60, initialDescription).Value == 70 &&
        DefaultSettings.Bind(initialSettings, "Icons", "Icons", "Minimap creature icon size", 26, initialDescription).Value == 19 &&
        !DefaultSettings.Bind(initialSettings, "Creatures", "Grouping", "Group creatures", true, initialDescription).Value &&
        DefaultSettings.Bind(initialSettings, "General", "Grouping", "Group threshold", 3, initialDescription).Value == 2 &&
        !DefaultSettings.Bind(initialSettings, "General", "Sharing", "Other player pins", true, initialDescription).Value,
        "initial names, sizes, grouping and sharing match the configured reference profile");
    initialSettings.Save();
    File.WriteAllText(initialSettingsPath, "[Names]\nHide minimap names = false\nMinimap name distance = 125\n[Icons]\nCreature icons = false\nMinimap creature icons = true\n[Grouping]\nGroup creatures = true\nGroup distance = 300\n");
    var existingSettings = new BepInEx.Configuration.ConfigFile(initialSettingsPath, false) { SaveOnConfigSet = false };
    var existingLargeIcons = DefaultSettings.Bind(existingSettings, "Display", "Icons", "Creature icons", true, initialDescription);
    var existingGroupSpan = DefaultSettings.Bind(existingSettings, "General", "Grouping", "Group distance", 400, initialDescription);
    Check(existingGroupSpan.Value == 300 && (int)existingGroupSpan.DefaultValue == 400,
        "the new default preserves a player's chosen group span while reset uses four hundred");
    Check(!DefaultSettings.Bind(existingSettings, "Display", "Names", "Hide minimap names", true, initialDescription).Value &&
        DefaultSettings.Bind(existingSettings, "Names", "Names", "Minimap name distance", 60, initialDescription).Value == 125 &&
        !existingLargeIcons.Value && ConfigSections.CopyIfMissing(existingSettings, existingLargeIcons, "Minimap creature icons",
            DefaultSettings.Value("Icons", "Minimap creature icons", true), initialDescription, copySourceIfMissing: false).Value &&
        DefaultSettings.Bind(existingSettings, "Creatures", "Grouping", "Group creatures", false, initialDescription).Value,
        "new initial defaults preserve existing players' saved names, icon modes, distances and grouping");
    Check(DefaultSettings.Value("Types", "prefab:giant_brain", false) &&
        !DefaultSettings.Value("Types", "prefab:giant_brain_frac", true) &&
        DefaultSettings.Value("Types", "prefab:UnknownModdedCreature", true),
        "initial type exceptions preserve chosen skeleton remains while unknown mod types retain category defaults");
    if (args.Length > 1)
    {
        var referenceConfig = new BepInEx.Configuration.ConfigFile(args[1], false) { SaveOnConfigSet = false };
        var referenceCatalog = DataJson.Read<CatalogDocument>(File.ReadAllText(args[0]));
        var referenceDefault = BuiltInFilterPresets.Build(referenceCatalog.entries, BuiltInFilterPreset.Default);
        Check(referenceCatalog.entries.All(entry => referenceDefault.types[entry.key].selected ==
            referenceConfig.Bind("Types", entry.key.Replace('=', '_').Replace('[', '_').Replace(']', '_'), false).Value),
            "initial selections match every type in the complete reference profile catalog");
        using var initialResource = typeof(DefaultSettings).Assembly.GetManifestResourceStream("MapTracker.DefaultSettings.json");
        using var initialReader = new StreamReader(initialResource);
        var initialValues = DataJson.Read<Dictionary<string, Dictionary<string, string>>>(initialReader.ReadToEnd());
        Check(initialValues.Where(section => section.Key != "Types" && section.Key != "Keep discovered markers").All(section =>
            section.Value.All(setting => section.Key == "Grouping" && setting.Key == "Group distance" ? setting.Value == "400" :
                referenceConfig.Bind(section.Key, setting.Key, "").Value == setting.Value)),
            "initial values retain the reference profile except the explicitly requested four-hundred-meter group default");
    }
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

var queryType = typeof(MarkerFile).Assembly.GetType("MapTracker.DiscoveryQuery");
var searchDiscoveries = queryType.GetMethod("Search", lifetimeFlags);
var directionMethod = queryType.GetMethod("Direction", lifetimeFlags);
var queryRecords = new List<MarkerRecord>
{
    new() { id = "north", type = "portal", portalTag = "Home", position = new Vector3(0, 0, 100), favorite = true },
    new() { id = "near", type = "mine", position = new Vector3(0, 0, 10) },
    new() { id = "remote", type = "mine", shared = true },
    new() { id = "enemy", type = "mob" }
};
foreach (var r in queryRecords) recordEntry.SetValue(r, new CatalogEntry { key = r.type, name = r.type, creature = r.id == "enemy" });
List<MarkerRecord> FindDiscoveries(string term, bool favorites = false) =>
    (List<MarkerRecord>)searchDiscoveries.Invoke(null, new object[] { queryRecords, term, favorites, Vector3.zero, new Func<MarkerRecord, string>(r => r.type) });
Check(FindDiscoveries(" HOME ").Single().id == "north", "discovery search finds portal tags with trimmed case-insensitive input");
Check(FindDiscoveries("", true).Single().id == "north" && FindDiscoveries("").Count == 2 && FindDiscoveries("")[0].favorite,
    "discovery lists prioritize favorites, include remembered places and exclude shared data and creatures");
Check(FindDiscoveries("mine").Single().id == "near", "discovery search matches known object types rather than only current filters");
Check(DataJson.Read<MarkerFile>(DataJson.Write(new MarkerFile { markers = queryRecords })).markers[0].favorite,
    "favorite flags persist in existing world marker files");
Check((string)directionMethod.Invoke(null, new object[] { Vector3.zero, new Vector3(0, 0, 100) }) == "N" &&
    (string)directionMethod.Invoke(null, new object[] { Vector3.zero, new Vector3(-100, 0, -100) }) == "SW",
    "navigation bearings match world directions on both axes");
var nearbyRule = typeof(MarkerFile).Assembly.GetType("MapTracker.MarkerPresentation").GetMethod("Nearby", lifetimeFlags);
bool Nearby(bool shown, float distance) => (bool)nearbyRule.Invoke(null, new object[] { shown, distance, 70, 5 });
Check(!Nearby(false, 72) && Nearby(true, 72) && !Nearby(true, 76) && Nearby(false, 70),
    "distance hysteresis retains a shown name across the boundary without revealing more distant new names");
string displayTestFolder = Path.Combine(AppContext.BaseDirectory, "DisplayPresetChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(displayTestFolder);
try
{
    var displayConfig = new BepInEx.Configuration.ConfigFile(Path.Combine(displayTestFolder, "settings.cfg"), false) { SaveOnConfigSet = false };
    var miniSize = displayConfig.Bind("Icons", "Minimap creature icon size", 19, new BepInEx.Configuration.ConfigDescription("", new BepInEx.Configuration.AcceptableValueRange<int>(12, 48)));
    var bigIcons = displayConfig.Bind("Icons", "Creature icons", true);
    var miniPlants = displayConfig.Bind("Map categories", "Minimap: Plants and pickables", false);
    var nameRange = displayConfig.Bind("Names", "Minimap name distance", 70);
    var sharingSetting = displayConfig.Bind("Sharing", "Share discoveries", false);
    var enabledSetting = displayConfig.Bind("General", "Enabled", true);
    var capturedDisplay = PresetDisplay.Capture(displayConfig);
    var fullPreset = new FilterPreset { name = "Full", display = capturedDisplay };
    var displayStore = new FilterPresetStore(Path.Combine(displayTestFolder, "presets.json"));
    displayStore.Save(fullPreset);
    miniSize.Value = 35; bigIcons.Value = false; miniPlants.Value = true; nameRange.Value = 125;
    PresetDisplay.Apply(displayConfig, displayStore.Read().Single().display);
    Check(miniSize.Value == 19 && bigIcons.Value && !miniPlants.Value && nameRange.Value == 70,
        "full display presets restore independent sizes, icon modes, category layers and name distance after reload");
    Check(!capturedDisplay.ContainsKey("Sharing") && !capturedDisplay.ContainsKey("General"),
        "display presets cannot change sharing permissions or disable the plugin");
    PresetDisplay.Apply(displayConfig, new Dictionary<string, Dictionary<string, string>> { ["Sharing"] = new() { ["Share discoveries"] = "true" }, ["General"] = new() { ["Enabled"] = "false" } });
    Check(!sharingSetting.Value && enabledSetting.Value, "unknown or non-presentation preset settings are ignored");
    PresetDisplay.Apply(displayConfig, null);
    Check(miniSize.Value == 19 && !miniPlants.Value, "legacy filter presets retain current map presentation");
    bool invalidDisplayRejected = false;
    try { PresetDisplay.Apply(displayConfig, new Dictionary<string, Dictionary<string, string>> { ["Icons"] = new() { ["Creature icons"] = "false", ["Minimap creature icon size"] = "999" } }); }
    catch (ArgumentException) { invalidDisplayRejected = true; }
    Check(invalidDisplayRejected && bigIcons.Value && miniSize.Value == 19, "out-of-range display presets fail before applying any settings");
    displayConfig.Save();
    var reloadedDisplay = new BepInEx.Configuration.ConfigFile(Path.Combine(displayTestFolder, "settings.cfg"), false) { SaveOnConfigSet = false };
    Check(!reloadedDisplay.Bind("Map categories", "Minimap: Plants and pickables", true).Value,
        "category map-layer choices persist without affecting discovery selections");
    var perMapConfig = new BepInEx.Configuration.ConfigFile(Path.Combine(displayTestFolder, "per-map.cfg"), false) { SaveOnConfigSet = false };
    perMapConfig.Bind("Icons", "Minimap creature icon size", 27);
    perMapConfig.Bind("Names", "Minimap name distance", 95);
    perMapConfig.Bind("Maps", "Large map", false);
    var perMapSize = ConfigSections.Bind(perMapConfig, "Icons", "Minimap", "Minimap creature icon size", 19, new BepInEx.Configuration.ConfigDescription(""));
    var perMapDistance = ConfigSections.Bind(perMapConfig, "Names", "Minimap", "Minimap name distance", 70, new BepInEx.Configuration.ConfigDescription(""));
    var perMapLarge = ConfigSections.Bind(perMapConfig, "Maps", "Large map", "Large map", true, new BepInEx.Configuration.ConfigDescription(""));
    Check(perMapSize.Value == 27 && perMapDistance.Value == 95 && !perMapLarge.Value && !perMapConfig.Any(p => p.Key.Section == "Maps" || p.Key.Section == "Icons"),
        "per-map configuration sections preserve saved sizes, distances and disabled large-map visibility without duplicate rows");
    PresetDisplay.Apply(perMapConfig, new Dictionary<string, Dictionary<string, string>> {
        ["Icons"] = new() { ["Minimap creature icon size"] = "31" }, ["Names"] = new() { ["Minimap name distance"] = "125" },
        ["Maps"] = new() { ["Large map"] = "true" } });
    Check(perMapSize.Value == 31 && perMapDistance.Value == 125 && perMapLarge.Value,
        "legacy presentation presets still apply to the reorganized map sections");
    PresetDisplay.Apply(perMapConfig, new Dictionary<string, Dictionary<string, string>> {
        ["Icons"] = new() { ["Minimap creature icon size"] = "22" }, ["Minimap"] = new() { ["Minimap creature icon size"] = "29" } });
    Check(perMapSize.Value == 29 && PresetDisplay.Capture(perMapConfig).ContainsKey("Minimap") &&
        DefaultSettings.Value("Minimap", "Hide minimap names", false) && !DefaultSettings.Value("Minimap", "Minimap creature icons", true),
        "new map-specific preset entries take precedence and retain the established first-install defaults");
}
finally { foreach (string path in Directory.GetFiles(displayTestFolder)) File.Delete(path); Directory.Delete(displayTestFolder); }

var notificationRule = typeof(MarkerFile).Assembly.GetType("MapTracker.DiscoveryNotification").GetMethod("Allowed", lifetimeFlags);
var notificationSample = new MarkerRecord { id = "new", type = "location:mine" };
recordEntry.SetValue(notificationSample, new CatalogEntry { dungeon = true });
bool Notify(bool dungeonMode, bool selected) => (bool)notificationRule.Invoke(null, new object[] { notificationSample, dungeonMode, selected });
Check(Notify(true, false) && !Notify(false, false) && Notify(false, true), "notification settings explicitly opt into dungeons or selected static types");
notificationSample.shared = true;
Check(!Notify(true, true), "received discoveries cannot generate local discovery notifications");
notificationSample.shared = false; notificationSample.imported = true;
Check(!Notify(true, true), "imported legacy markers do not generate new-place notifications");
notificationSample.imported = false; notificationSample.collected = true;
Check(!Notify(true, true), "already collected resources do not generate discovery notifications");
notificationSample.collected = false; recordDynamic.SetValue(notificationSample, true);
Check(!Notify(true, true), "temporary dynamic positions do not produce repeated static discovery notifications");
var targetGesture = typeof(MarkerFile).Assembly.GetType("MapTracker.MapTargetGesture").GetMethod("Resolve", lifetimeFlags);
string TargetAction(bool alt, bool left, bool hit, bool same = false, bool collapsed = false) =>
    targetGesture.Invoke(null, new object[] { alt, left, hit, same, collapsed }).ToString();
Check(TargetAction(true, true, true) == "Select", "Alt-left-click selects a precise map object as a navigation target");
Check(TargetAction(true, true, true, true) == "Clear" && TargetAction(true, true, false) == "Place",
    "Alt-clicking the selected marker clears navigation while empty space places a destination");
Check(TargetAction(true, true, true, false, true) == "Expand" && TargetAction(true, true, true, false, false) == "Select",
    "collapsed groups expand before Alt-click selects a real individual member");
Check(TargetAction(true, false, true) == "Consume" && TargetAction(true, false, false) == "Consume",
    "Alt-double/right-click is consumed without creating or deleting native pins");
Check(TargetAction(false, true, true) == "None" && TargetAction(false, false, false) == "None",
    "ordinary map clicks retain the existing dungeon, group and native-pin behavior");
var arrivalRule = typeof(MarkerFile).Assembly.GetType("MapTracker.NavigationArrival").GetMethod("Reached", lifetimeFlags);
bool ReachedGoal(Vector3 origin, Vector3 destination, bool interior = false, bool teleporting = false, bool point = false) =>
    (bool)arrivalRule.Invoke(null, new object[] { origin, destination, interior, teleporting, point });
Check(ReachedGoal(Vector3.zero, new Vector3(3, 0, 4)) && !ReachedGoal(Vector3.zero, new Vector3(3, 0, 4.1f)),
    "arrival cancels navigation at the five-meter boundary, not before reaching it");
Check(!ReachedGoal(Vector3.zero, Vector3.zero, interior: true) && !ReachedGoal(Vector3.zero, Vector3.zero, teleporting: true),
    "interior rooms and teleport transitions cannot falsely finish a surface destination");
Check(!ReachedGoal(new Vector3(0, 40, 0), Vector3.zero) && ReachedGoal(new Vector3(0, 40, 0), Vector3.zero, point: true),
    "object destinations protect floors while arbitrary map points ignore unknown terrain height");
var englishUi = typeof(Plugin).GetMethod("Text", lifetimeFlags);
Check((string)englishUi.Invoke(null, new object[] { "Русский текст", "English UI" }) == "English UI",
    "mod-owned UI text always uses English without modifying the game's language");
var headingRule = typeof(MarkerFile).Assembly.GetType("MapTracker.NavigationHeading").GetMethod("Relative", lifetimeFlags);
float Heading(float x, float z, float cameraX = 0, float cameraZ = 1, float playerX = 0, float playerZ = 1) =>
    (float)headingRule.Invoke(null, new object[] { x, z, cameraX, cameraZ, playerX, playerZ });
Check(Heading(0, 10) == 0 && Heading(10, 0) == 90 && Heading(-10, 0) == -90 && Math.Abs(Heading(0, -10)) == 180,
    "navigation distinguishes ahead, behind, left and right for a north-facing camera");
Check(Heading(10, 0, 1, 0) == 0 && Heading(0, 10, 1, 0) == -90,
    "turning the camera changes guidance even when the character does not move");
Check(Math.Abs(Heading(1, -10, -1, -10) + 11.421186f) < 0.001f,
    "navigation crosses the south heading boundary without reversing the shortest turn");
Check(Heading(10, 0, 0, 0, 1, 0) == 0 && Heading(10, 0, 0, 0, 0, 0) == 90,
    "vertical camera view falls back to character heading, then north, without zero-vector rotation");
Check(Heading(0, 0) == 0 && !float.IsNaN(Heading(0, 0, 0, 0, 0, 0)),
    "coincident navigation destinations do not create invalid arrow rotations");
var namePriority = typeof(MarkerFile).Assembly.GetType("MapTracker.MarkerPresentation").GetMethod("NameOrder", lifetimeFlags);
int NamePriority(bool targetA, float distanceA, string idA, bool targetB, float distanceB, string idB) =>
    (int)namePriority.Invoke(null, new object[] { targetA, distanceA, idA, targetB, distanceB, idB });
Check(NamePriority(true, 100, "goal", false, 1, "near") < 0 && NamePriority(false, 1, "near", true, 100, "goal") > 0,
    "selected destinations receive label space before nearby ordinary creatures");
Check(NamePriority(false, 20, "b", false, 40, "a") < 0 && NamePriority(false, 20, "a", false, 20, "b") < 0,
    "ordinary labels use nearest-first priority with deterministic ties");
var publicPosition = typeof(MarkerFile).Assembly.GetType("MapTracker.PlayerNavigation").GetMethod("TryPosition", lifetimeFlags);
var trackedPlayerId = new ZDOID(23, 5);
var otherPlayerId = new ZDOID(23, 6);
bool PlayerPosition(ZNet.PlayerInfo[] players, out Vector3 point)
{
    object[] arguments = { players, trackedPlayerId, Vector3.zero };
    bool result = (bool)publicPosition.Invoke(null, arguments); point = (Vector3)arguments[2]; return result;
}
var trackedPlayer = new ZNet.PlayerInfo { m_name = "Same name", m_characterID = trackedPlayerId, m_publicPosition = true, m_position = new Vector3(10, 20, 30) };
var otherPlayer = new ZNet.PlayerInfo { m_name = "Same name", m_characterID = otherPlayerId, m_publicPosition = true, m_position = new Vector3(50, 20, 70) };
Check(PlayerPosition(new[] { otherPlayer, trackedPlayer }, out var playerPosition) && playerPosition.x == 10 && playerPosition.z == 30,
    "player navigation follows identity rather than ambiguous duplicate names");
trackedPlayer.m_position = new Vector3(100, 20, 300);
Check(PlayerPosition(new[] { trackedPlayer }, out playerPosition) && playerPosition.x == 100 && playerPosition.z == 300,
    "moving public players update the destination coordinates");
trackedPlayer.m_publicPosition = false;
Check(!PlayerPosition(new[] { trackedPlayer }, out _) && !PlayerPosition(new[] { otherPlayer }, out _),
    "hidden or disconnected players cannot leave an active stale navigation destination");
trackedPlayer.m_publicPosition = true; trackedPlayer.m_position = new Vector3(float.NaN, 0, 0);
Check(!PlayerPosition(new[] { trackedPlayer }, out _), "invalid public player coordinates cannot drive navigation");
var nameClusterA = new MarkerRecord { id = "name-cluster-a", type = "leech" };
var nameClusterB = new MarkerRecord { id = "name-cluster-b", type = "leech" };
typeof(MarkerRecord).GetField("nameGroupMembers", recordFlags).SetValue(nameClusterA, new List<MarkerRecord> { nameClusterA, nameClusterB });
string transientNameJson = DataJson.Write(new MarkerFile { markers = new List<MarkerRecord> { nameClusterA, nameClusterB } });
Check(!transientNameJson.Contains("nameGroupMembers") && !transientNameJson.Contains("nameGroupScreen") &&
    DataJson.Read<MarkerFile>(transientNameJson).markers.Count == 2,
    "temporary name clusters never serialize cyclic UI references into character discoveries");
