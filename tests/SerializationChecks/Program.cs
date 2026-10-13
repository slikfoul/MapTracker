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

// Exercise production merging using both the spatial index and direct fallback.
var sharingType = typeof(MarkerFile).Assembly.GetType("MapTracker.SharedDiscoveries");
var sharingReceive = sharingType.GetMethod("Receive", lifetimeFlags);
var sharingObserve = sharingType.GetMethod("Observe", lifetimeFlags);
var sharingCleanup = sharingType.GetMethod("RemoveDuplicates", lifetimeFlags);
var sharingHandoff = sharingType.GetMethod("HandOffCreature", lifetimeFlags);
var indexType = typeof(MarkerFile).Assembly.GetType("MapTracker.SharedDiscoveryIndex");
var combined = new Dictionary<string, MarkerRecord>();
MarkerRecord SharedRecord(string id, CatalogEntry entry, Vector3 position, bool shared = false)
{
    var record = new MarkerRecord { id = id, type = entry.key, position = position, shared = shared, observed = true };
    recordEntry.SetValue(record, entry); return record;
}
object BuildIndex() => Activator.CreateInstance(indexType, recordFlags, null, new object[] { combined.Values }, null);
MarkerRecord ReceiveShared(MarkerRecord record, long peer = 1, float time = 0, object index = null) =>
    (MarkerRecord)sharingReceive.Invoke(null, new object[] { combined, record, peer, time, index });
var ownEntrance = SharedRecord("local-entrance", mineEntry, entrancePosition);
ownEntrance.completed = ownEntrance.visited = ownEntrance.favorite = true;
combined.Add(ownEntrance.id, ownEntrance);
var mergeIndex = BuildIndex();
Check(ReceiveShared(SharedRecord("ally-entrance", mineEntry, entrancePosition), index: mergeIndex) == ownEntrance && combined.Count == 1,
    "ally discovery with a different network ID merges into the same local entrance through the spatial index");
Check(!ownEntrance.shared && ownEntrance.completed && ownEntrance.visited && ownEntrance.favorite,
    "received data preserves local ownership, personal favorites and dungeon progress");
Check(ReceiveShared(SharedRecord("local-entrance", mineEntry, entrancePosition)) == ownEntrance && combined.Count == 1,
    "repeat packets and identical network IDs never duplicate a discovery");
var nearbyEntrance = ReceiveShared(SharedRecord("separate-entrance", mineEntry, entrancePosition + new Vector3(3, 0, 0)), index: mergeIndex);
ReceiveShared(SharedRecord("separate-type", otherEntry, entrancePosition), index: mergeIndex);
ReceiveShared(SharedRecord("separate-floor", mineEntry, entrancePosition + new Vector3(0, 5, 0)), index: mergeIndex);
Check(combined.Count == 4 && nearbyEntrance.shared,
    "different nearby entrances, known dungeon types and floors retain individual discoveries");
var sharedPortalEntry = new CatalogEntry { key = "prefab:portal_wood", category = "Portals" };
var sharedPortal = ReceiveShared(SharedRecord("ally-portal", sharedPortalEntry, new Vector3(-2.01f, 0, 8)), index: mergeIndex);
var namedPortal = SharedRecord("other-peer-portal", sharedPortalEntry, new Vector3(-1.99f, 0, 8));
namedPortal.portalTag = "South";
Check(ReceiveShared(namedPortal, peer: 2, index: mergeIndex) == sharedPortal && sharedPortal.portalTag == "South" && combined.Count == 5,
    "shared portal aliases across a negative-coordinate cell boundary merge and update their tag");
var oreEntry = new CatalogEntry { key = "prefab:MineRock_Copper", category = "Ore" };
var oreShared = ReceiveShared(SharedRecord("ally-ore", oreEntry, new Vector3(20, 3, 10)), index: mergeIndex);
Check(ReceiveShared(SharedRecord("local-ore-alias", oreEntry, new Vector3(20, 3, 10)), index: mergeIndex) == oreShared && combined.Count == 6,
    "ore discovered by multiple peers becomes one normal discovery record");
ReceiveShared(SharedRecord("separate-ore", oreEntry, new Vector3(20.2f, 3, 10)), index: mergeIndex);
Check(combined.Count == 7, "adjacent separate ore objects are not collapsed by the dungeon tolerance");
sharingObserve.Invoke(null, new object[] { ownEntrance, 1L, true, 10f, 40f });
sharingObserve.Invoke(null, new object[] { ownEntrance, 2L, true, 20f, 40f });
sharingObserve.Invoke(null, new object[] { ownEntrance, 1L, false, 25f, 40f });
var remoteDeadline = typeof(MarkerRecord).GetField("remoteSeenUntil", recordFlags);
Check((float)remoteDeadline.GetValue(ownEntrance) == 60,
    "one peer leaving an area does not erase another peer's current observation");
var savedCombined = DataJson.Write(new MarkerFile { world = "shared-world", character = "recipient", markers = combined.Values.ToList() });
var persistedCombined = DataJson.Read<MarkerFile>(savedCombined);
Check(persistedCombined.markers.Count == 7 && persistedCombined.markers.Any(r => r.id == oreShared.id && r.shared) &&
    !savedCombined.Contains("remoteSeenUntil") && !savedCombined.Contains("remoteObservers"),
    "received static discoveries survive saving while runtime observation timers never persist");
combined.Add("legacy-remote-duplicate", SharedRecord("legacy-remote-duplicate", mineEntry, entrancePosition, true));
var mobDuplicateA = SharedRecord("network-mob-a", mobEntry, entrancePosition);
var mobDuplicateB = SharedRecord("network-mob-b", mobEntry, entrancePosition);
recordDynamic.SetValue(mobDuplicateA, true); recordDynamic.SetValue(mobDuplicateB, true);
combined.Add(mobDuplicateA.id, mobDuplicateA); combined.Add(mobDuplicateB.id, mobDuplicateB);
Check((bool)sharingCleanup.Invoke(null, new object[] { combined, removePin }) && combined.Count == 9 &&
    combined.ContainsKey(ownEntrance.id) && !combined.ContainsKey("legacy-remote-duplicate") &&
    combined.ContainsKey(mobDuplicateA.id) && combined.ContainsKey(mobDuplicateB.id),
    "legacy remote duplicates collapse into the local entrance while distinct co-located monsters remain separate");
Check(!(bool)sharingCleanup.Invoke(null, new object[] { combined, removePin }), "shared discovery cleanup is idempotent");
var remoteMobUpdate = new MarkerRecord { position = entrancePosition + new Vector3(2, 0, 0), creatureLevel = 2, creatureRelation = 1 };
sharingType.GetMethod("ObserveCreature", lifetimeFlags).Invoke(null, new object[] { mobDuplicateA, remoteMobUpdate, 2L, 100f });
Check((bool)sharingHandoff.Invoke(null, new object[] { mobDuplicateA, 101f }) && mobDuplicateA.shared && mobDuplicateA.sourcePeer == 2,
    "a creature leaving local scan range transfers to fresh ally observation without a duplicate marker");
Check(mobDuplicateA.position.x == remoteMobUpdate.position.x && mobDuplicateA.creatureLevel == 2 && mobDuplicateA.creatureRelation == 1,
    "handoff immediately uses the ally's latest coordinates, level and relationship even when local transmission is disabled");
Check(!(bool)sharingHandoff.Invoke(null, new object[] { mobDuplicateB, 101f }) && !mobDuplicateB.shared &&
    !(bool)sharingHandoff.Invoke(null, new object[] { ownEntrance, 25f }),
    "creature handoff requires a fresh observer and never converts static discoveries");
var deltaType = typeof(MarkerFile).Assembly.GetType("MapTracker.CreatureNetworkDelta");
var delta = Activator.CreateInstance(deltaType, true);
var deltaSelect = deltaType.GetMethod("Select", recordFlags);
var transmissionRecords = new List<MarkerRecord> { mobDuplicateA, mobDuplicateB };
int DeltaCount(long peer, float time) => ((List<MarkerRecord>)deltaSelect.Invoke(delta, new object[] { peer, transmissionRecords, time })).Count;
Check(DeltaCount(1, 0) == 2 && DeltaCount(1, 0.5f) == 0 && DeltaCount(1, 1) == 0,
    "stationary creature snapshots are sent once rather than repeated every half second");
Check(DeltaCount(2, 1) == 2 && DeltaCount(1, 1.5f) == 2,
    "new peers get an initial snapshot and stationary creatures retain periodic freshness heartbeats");
mobDuplicateA.position += new Vector3(0.1f, 0, 0);
Check(DeltaCount(1, 2) == 1, "a moving creature transmits its new position without resending unchanged neighbors");
mobDuplicateB.creatureLevel = 3;
Check(DeltaCount(1, 2.5f) == 1, "a creature level change is transmitted before its heartbeat");
mobDuplicateA.creatureRelation = 4;
Check(DeltaCount(1, 2.6f) == 1, "taming or relationship changes are transmitted before the heartbeat");
deltaType.GetMethod("RemovePeer", recordFlags).Invoke(delta, new object[] { 1L });
Check(DeltaCount(1, 2.7f) == 2, "a returning peer receives all current creatures after its transmission cache is retired");
deltaType.GetMethod("Reset", recordFlags).Invoke(delta, null);
Check(DeltaCount(2, 2.8f) == 2, "turning creature transmission off clears its delta state for later re-enabling");

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
bool VisibleDiscovery(bool keep, bool loaded, bool remote, float radius, float distance, bool freshRemote = false) =>
    (bool)visibilityMethod.Invoke(null, new object[] { keep, loaded, remote, radius, distance, freshRemote });
Check(VisibleDiscovery(true, false, false, 50, 1000) && !VisibleDiscovery(false, false, false, 0, 0),
    "remembered portals remain after leaving; nearby-only discoveries hide when unloaded");
Check(VisibleDiscovery(false, true, false, 50, 50) && !VisibleDiscovery(false, true, false, 50, 51) && VisibleDiscovery(false, true, false, 0, 1000),
    "nearby-only visibility follows the detection radius, including its boundary and zero limit");
Check(!VisibleDiscovery(false, false, true, 0, 1) && VisibleDiscovery(true, false, true, 50, 1000),
    "remote discoveries obey the per-type memory choice and are not mistaken for client-loaded objects");
var sightVisibilityMethod = visibilityType.GetMethod("ShowSight", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
bool VisibleSight(bool keep, bool seen, bool remote, bool freshRemote = false) => (bool)sightVisibilityMethod.Invoke(null, new object[] { keep, seen, remote, freshRemote });
Check(VisibleSight(false, true, false) && !VisibleSight(false, false, false) && VisibleSight(true, false, false),
    "line-of-sight discoveries require visibility unless a local remembered marker is enabled");
Check(!VisibleSight(false, true, true) && VisibleSight(true, false, true) && VisibleSight(false, false, true, true),
    "line-of-sight mode accepts retained shared discoveries and fresh observations from an ally");
Check(VisibleDiscovery(false, false, true, 30, 5000, true) && !VisibleDiscovery(false, false, true, 30, 5000) &&
    VisibleDiscovery(true, false, true, 30, 5000),
    "fresh ally observations ignore receiver distance while retention remains governed by Keep discovered");
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
    gatheringPreset.types["beehive"].keep && builtInSamples.Where(e => e.category == "Plants and pickables").All(e => !gatheringPreset.types[e.key].keep),
    "Gathering shows botanical pickables nearby without remembering them, retains wild beehives, and excludes loose items and effects");
var explorationPreset = BuiltInFilterPresets.Build(builtInSamples, BuiltInFilterPreset.Exploration);
Check(explorationPreset.types.Where(p => p.Value.selected).Select(p => p.Key).Order().SequenceEqual(
    new[] { "altar", "camp", "dungeon", "portal", "trader" }) && explorationPreset.types.All(p => p.Value.keep == p.Value.selected),
    "Exploration remembers locations, dungeon entrances, portals, traders and offering altars");
var householdPreset = BuiltInFilterPresets.Build(builtInSamples, BuiltInFilterPreset.Household);
Check(householdPreset.types.Where(p => p.Value.selected).Select(p => p.Key).Order().SequenceEqual(
    new[] { "enemy", "ownHive", "portal", "sap" }) && !householdPreset.types["enemy"].keep,
    "Household selects creature and resource types without changing configurator relationship filters");
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
    presetStore.Save(new FilterPreset { name = "  Руда / север  ",
        types = new() { ["prefab:MineRock_Copper"] = new() { selected = true, keep = false },
            ["prefab:portal_wood"] = new() { selected = false, keep = true } } });
    var savedPreset = new FilterPresetStore(presetCheckPath).Read().Single();
    Check(savedPreset.name == "Руда / север" && savedPreset.types["prefab:MineRock_Copper"].selected &&
        !savedPreset.types["prefab:MineRock_Copper"].keep && savedPreset.types["prefab:portal_wood"].keep,
        "custom preset preserves display and keep-discovered choices across store reload");
    presetStore.Save(new FilterPreset { name = "руда / СЕВЕР", types = new() { ["prefab:portal_wood"] = new() { selected = true } } });
    Check(presetStore.Read().Count == 1 && presetStore.Read()[0].types["prefab:portal_wood"].selected && File.Exists(presetCheckPath + ".bak"),
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
    Check(presetStore.Read().Single().types.Count == 0 && !presetStore.BuiltInDeleted(BuiltInFilterPreset.Gathering),
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
string filterScopeFolder = Path.Combine(AppContext.BaseDirectory, "MapTrackerFilterScope-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(filterScopeFolder);
try
{
    var scopeConfig = new BepInEx.Configuration.ConfigFile(Path.Combine(filterScopeFolder, "settings.cfg"), false) { SaveOnConfigSet = false };
    var scopeSize = scopeConfig.Bind("Minimap", "Minimap creature icon size", 35);
    var scopeNames = scopeConfig.Bind("Names", "Creature name size", 24);
    var scopeGroup = scopeConfig.Bind("Grouping", "Group plants", true);
    var scopeHostile = scopeConfig.Bind("Creatures", "Hostile", false);
    var scopeSharing = scopeConfig.Bind("Sharing", "Share discoveries", false);
    var scopeEntry = new CatalogEntry { key = "portal" };
    typeof(CatalogEntry).GetField("Selected", recordFlags).SetValue(scopeEntry, scopeConfig.Bind("Types", "portal", false));
    typeof(CatalogEntry).GetField("KeepVisible", recordFlags).SetValue(scopeEntry, scopeConfig.Bind("Remember", "portal", false));
    string legacyPresetJson = "{\"name\":\"Old\",\"hostile\":true,\"groupPlants\":false,\"types\":{\"portal\":{\"selected\":true,\"keep\":true}},\"display\":{\"Minimap\":{\"Minimap creature icon size\":\"12\"},\"Names\":{\"Creature name size\":\"10\"},\"Sharing\":{\"Share discoveries\":\"true\"}}}";
    var legacyScopedPreset = DataJson.Read<FilterPreset>(legacyPresetJson);
    var applyTypeFilters = typeof(MarkerFile).Assembly.GetType("MapTracker.FilterPresets").GetMethod("ApplyTypes", lifetimeFlags);
    applyTypeFilters.Invoke(null, new object[] { new[] { scopeEntry }, legacyScopedPreset, null });
    Check(scopeConfig.Bind("Types", "portal", false).Value && scopeConfig.Bind("Remember", "portal", false).Value,
        "legacy presets still apply Show object and Keep discovered selections from the map filters");
    Check(scopeSize.Value == 35 && scopeNames.Value == 24 && scopeGroup.Value && !scopeHostile.Value && !scopeSharing.Value,
        "preset loading preserves configurator sizes, text, grouping, creature relationships and sharing settings");
    string cleanPresetJson = DataJson.Write(legacyScopedPreset);
    Check(!cleanPresetJson.Contains("display") && !cleanPresetJson.Contains("hostile") && !cleanPresetJson.Contains("groupPlants") &&
        cleanPresetJson.Contains("types"), "new preset serialization contains only its name and map filter choices");
    var scopeStore = new FilterPresetStore(Path.Combine(filterScopeFolder, "presets.json"));
    legacyScopedPreset.types["portal"].notify = true;
    var notifiedTypes = new List<string>();
    Action<IEnumerable<CatalogEntry>, bool> notifyScope = (entries, value) => { if (value) notifiedTypes.AddRange(entries.Select(e => e.key)); };
    applyTypeFilters.Invoke(null, new object[] { new[] { scopeEntry }, legacyScopedPreset, notifyScope });
    Check(notifiedTypes.SequenceEqual(new[] { "portal" }), "presets retain the per-type Notify checkbox available in the map filter window");
    scopeStore.Save(legacyScopedPreset);
    Check(scopeStore.Read().Single().types["portal"].selected && scopeStore.Read().Single().types["portal"].keep && scopeStore.Read().Single().types["portal"].notify == true,
        "resaving a legacy preset retains filter selections while removing configurator data");
    var migratedConfig = new BepInEx.Configuration.ConfigFile(Path.Combine(filterScopeFolder, "migrated.cfg"), false) { SaveOnConfigSet = false };
    migratedConfig.Bind("Icons", "Minimap creature icon size", 27);
    migratedConfig.Bind("Names", "Minimap name distance", 95);
    migratedConfig.Bind("Maps", "Large map", false);
    var migratedSize = ConfigSections.Bind(migratedConfig, "Icons", "Minimap", "Minimap creature icon size", 19, new BepInEx.Configuration.ConfigDescription(""));
    var migratedDistance = ConfigSections.Bind(migratedConfig, "Names", "Minimap", "Minimap name distance", 70, new BepInEx.Configuration.ConfigDescription(""));
    var migratedLargeMap = ConfigSections.Bind(migratedConfig, "Maps", "Large map", "Large map", true, new BepInEx.Configuration.ConfigDescription(""));
    Check(migratedSize.Value == 27 && migratedDistance.Value == 95 && !migratedLargeMap.Value &&
        !migratedConfig.Any(p => p.Key.Section == "Maps" || p.Key.Section == "Icons"),
        "configuration section migration still preserves settings independently of filter presets");
}
finally { foreach (string file in Directory.GetFiles(filterScopeFolder)) File.Delete(file); Directory.Delete(filterScopeFolder); }

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
var navigationButton = typeof(MarkerFile).Assembly.GetType("MapTracker.MapTargetGesture").GetMethod("Matches", lifetimeFlags);
bool ButtonMatches(BepInEx.Configuration.KeyboardShortcut shortcut, KeyCode button, params KeyCode[] held) =>
    (bool)navigationButton.Invoke(null, new object[] { shortcut, button, (Func<KeyCode, bool>)(key => held.Contains(key)) });
Check(ButtonMatches(new BepInEx.Configuration.KeyboardShortcut(KeyCode.Mouse2), KeyCode.Mouse2) &&
    !ButtonMatches(new BepInEx.Configuration.KeyboardShortcut(KeyCode.Mouse2), KeyCode.Mouse0),
    "middle-click navigation does not replace ordinary left-click map actions");
Check(ButtonMatches(new BepInEx.Configuration.KeyboardShortcut(KeyCode.Mouse0, KeyCode.LeftAlt), KeyCode.Mouse0, KeyCode.LeftAlt) &&
    !ButtonMatches(new BepInEx.Configuration.KeyboardShortcut(KeyCode.Mouse0, KeyCode.LeftAlt), KeyCode.Mouse0) &&
    ButtonMatches(new BepInEx.Configuration.KeyboardShortcut(KeyCode.Mouse0, KeyCode.LeftAlt), KeyCode.Mouse0, KeyCode.RightAlt),
    "rebinding to a mouse combination requires its modifier and can restore Alt-left-click selection");
Check(ButtonMatches(new BepInEx.Configuration.KeyboardShortcut(KeyCode.G, KeyCode.LeftControl), KeyCode.G, KeyCode.LeftControl) &&
    !ButtonMatches(new BepInEx.Configuration.KeyboardShortcut(KeyCode.None), KeyCode.None),
    "keyboard navigation combinations are supported and an unbound selection key stays disabled");
string navigationConfigFolder = Path.Combine(Path.GetTempPath(), "MapTrackerNavigationChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(navigationConfigFolder);
try
{
    string configPath = Path.Combine(navigationConfigFolder, "navigation.cfg");
    var config = new BepInEx.Configuration.ConfigFile(configPath, false);
    var selectionKey = config.Bind("Navigation", "Select navigation key", new BepInEx.Configuration.KeyboardShortcut(KeyCode.Mouse2));
    selectionKey.Value = new BepInEx.Configuration.KeyboardShortcut(KeyCode.Mouse0, KeyCode.LeftAlt);
    config.Save();
    var reloaded = new BepInEx.Configuration.ConfigFile(configPath, false);
    var restoredKey = reloaded.Bind("Navigation", "Select navigation key", new BepInEx.Configuration.KeyboardShortcut(KeyCode.Mouse2));
    Check(ButtonMatches(restoredKey.Value, KeyCode.Mouse0, KeyCode.LeftAlt) && !ButtonMatches(restoredKey.Value, KeyCode.Mouse2),
        "changed navigation mouse button and modifier survive a configuration reload");
}
finally { foreach (string path in Directory.GetFiles(navigationConfigFolder)) File.Delete(path); Directory.Delete(navigationConfigFolder); }
var sharedGoal = new NavigationShare { world = "123", name = "Portal: North", type = "prefab:portal_wood", objectId = "123:42", position = new Vector3(100, 20, -50) };
var restoredSharedGoal = DataJson.Read<NavigationShare>(DataJson.Write(sharedGoal));
Check(restoredSharedGoal.name == sharedGoal.name && restoredSharedGoal.type == sharedGoal.type && restoredSharedGoal.objectId == sharedGoal.objectId && restoredSharedGoal.position.x == 100 && restoredSharedGoal.position.z == -50,
    "shared navigation packet preserves destination coordinates and label without Unity object references");
var sharedGoalRule = typeof(MarkerFile).Assembly.GetType("MapTracker.NavigationShare").GetMethod("Allowed", lifetimeFlags);
bool AllowedSharedGoal(NavigationShare goal, string world = "123", bool enabled = true, bool sharing = true) =>
    (bool)sharedGoalRule.Invoke(null, new object[] { goal, world, enabled, sharing });
Check(AllowedSharedGoal(restoredSharedGoal) && !AllowedSharedGoal(restoredSharedGoal, sharing: false) &&
    !AllowedSharedGoal(restoredSharedGoal, enabled: false) && !AllowedSharedGoal(restoredSharedGoal, world: "other"),
    "shared navigation respects sharing opt-in, enabled state and the current world");
var oldSharedGoal = DataJson.Read<NavigationShare>("{\"schema\":1,\"world\":\"123\",\"name\":\"Goal\",\"position\":{\"x\":100,\"y\":0,\"z\":200}}");
Check(oldSharedGoal.type == null && AllowedSharedGoal(oldSharedGoal), "earlier shared targets without icon metadata remain valid");
sharedGoal.type = new string('x', 257);
Check(!AllowedSharedGoal(sharedGoal), "shared icon metadata is bounded and cannot carry an oversized type identifier");
sharedGoal.type = "prefab:portal_wood";
sharedGoal.position.x = float.NaN;
Check(!AllowedSharedGoal(sharedGoal), "invalid coordinates cannot create a shared destination");
sharedGoal.position = new Vector3(20001, 0, 0);
Check(!AllowedSharedGoal(sharedGoal), "shared navigation rejects destinations beyond world bounds");
sharedGoal.position = Vector3.zero; sharedGoal.name = new string('x', 161);
Check(!AllowedSharedGoal(sharedGoal), "shared destination labels have a bounded network size");
bool incompleteGoalRejected = false;
try { DataJson.Read<NavigationShare>("{\"schema\":1,\"world\":\"123\",\"name\":\"Goal\"}"); }
catch (Newtonsoft.Json.JsonSerializationException) { incompleteGoalRejected = true; }
Check(incompleteGoalRejected, "a malformed shared target cannot silently navigate to zero coordinates");
var targetStateType = typeof(MarkerFile).Assembly.GetType("MapTracker.NavigationTargets");
var findSharedObject = targetStateType.GetMethod("FindObject", lifetimeFlags);
var goalObject = new MarkerRecord { id = "123:42", type = "prefab:portal_wood" };
var goalObjects = new Dictionary<string, MarkerRecord> { [goalObject.id] = goalObject };
Check(ReferenceEquals(findSharedObject.Invoke(null, new object[] { goalObjects, "123:42", "prefab:portal_wood" }), goalObject) &&
    findSharedObject.Invoke(null, new object[] { goalObjects, "123:42", "creature:Leech" }) == null &&
    findSharedObject.Invoke(null, new object[] { goalObjects, "missing", "prefab:portal_wood" }) == null,
    "shared targets reuse the exact object marker without confusing different identities or types");
var sameSharedPoint = targetStateType.GetMethod("SamePoint", lifetimeFlags);
Check((bool)sameSharedPoint.Invoke(null, new object[] { new MarkerRecord { position = new Vector3(7, 0, 9) }, new MarkerRecord { position = new Vector3(7, 0, 9) } }) &&
    !(bool)sameSharedPoint.Invoke(null, new object[] { new MarkerRecord { position = new Vector3(7, 0, 9) }, new MarkerRecord { position = new Vector3(70, 0, 90) } }),
    "coincident personal and shared map points can use one mixed-color marker while different goals stay separate");
var iconPolicy = typeof(MarkerFile).Assembly.GetType("MapTracker.NavigationTargetIcon").GetMethod("HasIcon", lifetimeFlags);
bool GoalIcon(string category)
{
    var sample = new MarkerRecord(); recordEntry.SetValue(sample, new CatalogEntry { category = category });
    return (bool)iconPolicy.Invoke(null, new object[] { sample });
}
Check(!GoalIcon("Navigation") && new[] { "Portals", "Plants and pickables", "Sap roots", "Locations", "Creatures", "Players" }.All(GoalIcon),
    "empty map destinations omit caption icons while actual object categories retain theirs");
var highlightRule = typeof(MarkerFile).Assembly.GetType("MapTracker.TargetHighlight").GetMethod("Tint", lifetimeFlags);
Color PersonalTint = (Color)highlightRule.Invoke(null, new object[] { true, false });
Color SharedTint = (Color)highlightRule.Invoke(null, new object[] { false, true });
Color MixedTint = (Color)highlightRule.Invoke(null, new object[] { true, true });
Check(PersonalTint.r > SharedTint.r && SharedTint.b > PersonalTint.b &&
    Math.Abs(MixedTint.r - (PersonalTint.r + SharedTint.r) * 0.5f) < 0.001f &&
    Math.Abs(MixedTint.b - (PersonalTint.b + SharedTint.b) * 0.5f) < 0.001f,
    "personal, shared and coincident targets have three distinct highlight states");
var targetState = Activator.CreateInstance(targetStateType, nonPublic: true);
var personalField = targetStateType.GetField("Personal", recordFlags);
var sharedField = targetStateType.GetField("Shared", recordFlags);
var personalGoal = new MarkerRecord { id = "personal", position = new Vector3(7, 0, 9) };
personalField.SetValue(targetState, personalGoal);
targetStateType.GetMethod("SetShared", recordFlags).Invoke(targetState, new object[] { new Vector3(100, 0, 200), "Leech" });
var receivedGoal = (MarkerRecord)sharedField.GetValue(targetState);
Check(ReferenceEquals(personalField.GetValue(targetState), personalGoal) && receivedGoal.position.x == 100 && personalGoal.position.x == 7,
    "receiving a shared destination preserves the existing personal target and its position");
targetStateType.GetMethod("SetShared", recordFlags).Invoke(targetState, new object[] { new Vector3(300, 0, 400), "Mine" });
Check(((MarkerRecord)sharedField.GetValue(targetState)).position.x == 300 && ReferenceEquals(personalField.GetValue(targetState), personalGoal),
    "a new shared destination replaces only the shared slot");
targetStateType.GetMethod("ClearShared", recordFlags).Invoke(targetState, null);
Check(sharedField.GetValue(targetState) == null && ReferenceEquals(personalField.GetValue(targetState), personalGoal),
    "cancelling the shared target leaves personal navigation active");
string TargetAction(bool navigation, bool single, bool hit, bool same = false, bool collapsed = false) =>
    targetGesture.Invoke(null, new object[] { navigation, single, hit, same, collapsed }).ToString();
Check(TargetAction(true, true, true) == "Select", "Navigation-bound click selects a precise map object as a navigation target");
Check(TargetAction(true, true, true, true) == "Clear" && TargetAction(true, true, false) == "Place",
    "Clicking the selected marker clears navigation while empty space places a destination");
Check(TargetAction(true, true, true, false, true) == "Expand" && TargetAction(true, true, true, false, false) == "Select",
    "collapsed groups expand before the navigation button selects a real individual member");
Check(TargetAction(true, false, true) == "Consume" && TargetAction(true, false, false) == "Consume",
    "Double-click on the navigation-bound button is consumed without creating or deleting native pins");
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
