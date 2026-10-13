using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace MapTracker
{
    [Serializable]
    public sealed class MarkerRecord
    {
        public string id, type;
        public Vector3 position;
        public bool collected, visited, completed;
        public long pickedTime;
        public string portalTag;
        public bool imported;
        public bool favorite;
        public bool shared;
        public bool observed;
        public long sourcePeer;
        public int creatureLevel, creatureRelation;
        [NonSerialized] internal float expiresAt;
        [NonSerialized] internal float remoteSeenUntil;
        [NonSerialized] internal Dictionary<long, float> remoteObservers;
        [NonSerialized] internal Vector3 remoteCreaturePosition;
        [NonSerialized] internal int remoteCreatureLevel, remoteCreatureRelation;
        [NonSerialized] internal bool live, dynamic, sightVisible;
        [NonSerialized] internal int observationScan;
        [NonSerialized] internal Character character;
        [NonSerialized] internal CatalogEntry entry;
        [NonSerialized] internal Minimap.PinData pin;
        [NonSerialized] internal CreatureStars stars;
        [NonSerialized] internal DungeonIcon dungeonIcon;
        [NonSerialized] internal CreatureMapIcon creatureIcon;
        [NonSerialized] internal TargetMarkerHighlight targetHighlight;
        [NonSerialized] internal CreatureNearbyName nearbyName;
        [NonSerialized] internal List<MarkerRecord> group;
        [NonSerialized] internal ClusterBadge badge;
        [NonSerialized] internal NativeNameVisibility nameVisibility;
        [NonSerialized] internal string nameCache;
        [NonSerialized] internal bool nearbyNameShown;
        [NonSerialized] internal bool largeNameShown;
        [NonSerialized] internal bool nameGroupFollower;
        [NonSerialized] internal List<MarkerRecord> nameGroupMembers;
        [NonSerialized] internal Vector2 nameGroupScreen;
        [NonSerialized] internal string nameGroupCaption, nameGroupBase;
        [NonSerialized] internal int nameGroupCount;
    }
    [Serializable]
    public sealed class MarkerFile
    {
        public int schema = 1;
        public string world, character;
        public List<MarkerRecord> markers = new List<MarkerRecord>();
    }

    internal sealed class Tracking : IDisposable
    {
        private readonly Plugin host;
        private readonly Dictionary<string, MarkerRecord> records = new Dictionary<string, MarkerRecord>();
        private static readonly FieldInfo Instances = AccessTools.Field(typeof(ZNetScene), "m_instances");
        private static readonly FieldInfo NativePins = AccessTools.Field(typeof(Minimap), "m_pins");
        private static readonly FieldInfo PinUpdateRequired = AccessTools.Field(typeof(Minimap), "m_pinUpdateRequired");
        private static readonly MethodInfo RockEmpty = AccessTools.Method(typeof(MineRock), "AllDestroyed");
        private static readonly MethodInfo Rock5Empty = AccessTools.Method(typeof(MineRock5), "AllDestroyed");
        private List<KeyValuePair<ZDO, ZNetView>> snapshot;
        private SharedDiscoveryIndex discoveryIndex;
        private readonly List<KeyValuePair<ZDO, ZNetView>> snapshotBuffer = new List<KeyValuePair<ZDO, ZNetView>>();
        private readonly HashSet<MarkerRecord> pinChanges = new HashSet<MarkerRecord>();
        private readonly List<string> expiredRecords = new List<string>();
        private bool fullPinRefresh;
        private string language;
        internal readonly DiscoveryTools Discoveries;
        internal IEnumerable<MarkerRecord> Records => records.Values;
        internal bool Contains(MarkerRecord record) => records.TryGetValue(record.id, out var r) && r == record;
        internal MarkerRecord FindNavigationObject(string id, string type, Vector3? position = null) => position.HasValue ?
            SharedDiscoveries.Find(records, id, type, position.Value) : NavigationTargets.FindObject(records, id, type);
        internal void DiscoveryChanged(MarkerRecord record) { record.nameCache = null; dirty = true; SettingsChanged(); }
        private int cursor;
        private int scanSerial;
        private float nextScan, nextSave;
        private bool dirty, loaded;
        private string world, character, savePath;
        private Minimap map;
        private ZDOMan manager;
        private Teleport pendingEntrance;
        private readonly Dictionary<Minimap.PinData, SharedPinMask> hiddenSharedPins = new Dictionary<Minimap.PinData, SharedPinMask>();
        private readonly List<Minimap.PinData> releasedSharedPins = new List<Minimap.PinData>();
        private readonly MarkerGrouping grouping = new MarkerGrouping();
        private readonly CreatureNameGroups creatureNameGroups = new CreatureNameGroups();
        private readonly CreatureHoverName creatureHover = new CreatureHoverName();
        private readonly List<MarkerRecord> nearbyNames = new List<MarkerRecord>();
        private readonly List<LabelBox> nearbyNameBoxes = new List<LabelBox>();
        private readonly LabelLayout.Selection nearbySelection = new LabelLayout.Selection();
        private readonly LargeMapLabelLayout largeLabels = new LargeMapLabelLayout();
        private readonly PlayerMarkerLayer playerLayer = new PlayerMarkerLayer();
        private readonly SightVisibility sight = new SightVisibility();
        private readonly PortalReconciliation portalReconciliation = new PortalReconciliation();
        private readonly List<MarkerRecord> portalCandidates = new List<MarkerRecord>();
        private readonly List<ZDO> nearbyPortalData = new List<ZDO>();
        private float nextPortalCheck;
        private float pendingDeadline;
        private float nextEarlyRefresh;
        private bool definitionsResolved;
        internal Tracking(Plugin plugin) { host = plugin; Discoveries = new DiscoveryTools(this, plugin); }
        internal void SettingsChanged() { fullPinRefresh = true; grouping.Invalidate(); creatureNameGroups.Invalidate(); }
        private int nameDiagnosticSamples;
        private float nextNameDiagnostic;
        internal void ResetNameDiagnostics() { nameDiagnosticSamples = 0; nextNameDiagnostic = Time.realtimeSinceStartup + 2; }

        internal void Tick()
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null || ZNetScene.instance == null)
            {
                if (loaded) Reset();
                return;
            }
            string newWorld = ZNet.instance.GetWorldUID().ToString();
            string newCharacter = player.GetPlayerID().ToString();
            if (!loaded || newWorld != world || newCharacter != character)
            {
                Reset(); world = newWorld; character = newCharacter;
                savePath = Path.Combine(Paths.ConfigPath, "MapTracker", "worlds", world + "_" + character + ".json");
                Load(); loaded = true;
            }
            if (manager != ZDOMan.instance)
            {
                if (manager != null) manager.m_onZDODestroyed -= Destroyed;
                manager = ZDOMan.instance;
                if (manager != null) manager.m_onZDODestroyed += Destroyed;
            }
            if (map != Minimap.instance)
            {
                RemovePins(); map = Minimap.instance;
                ResolveSavedDefinitions(); RefreshPins();
            }
            if (!host.Catalog.Ready)
            {
                if (Time.realtimeSinceStartup >= nextEarlyRefresh)
                { ResolveSavedDefinitions(); RefreshPins(); nextEarlyRefresh = Time.realtimeSinceStartup + 0.25f; }
                return;
            }
            if (!definitionsResolved) { ResolveSavedDefinitions(); RefreshPins(); definitionsResolved = true; }
            string currentLanguage = Localization.instance?.GetSelectedLanguage();
            if (language != currentLanguage) { language = currentLanguage; SettingsChanged(); }
            if (fullPinRefresh) { fullPinRefresh = false; foreach (var r in records.Values) r.nameCache = null; RefreshPins(); }
            if (snapshot == null && Time.realtimeSinceStartup >= nextScan)
            {
                scanSerial++;
                var instances = Instances.GetValue(ZNetScene.instance) as Dictionary<ZDO, ZNetView>;
                snapshotBuffer.Clear();
                if (instances != null) foreach (var pair in instances) snapshotBuffer.Add(pair);
                snapshot = snapshotBuffer;
                discoveryIndex = new SharedDiscoveryIndex(records.Values);
                cursor = 0;
            }
            if (snapshot != null)
            {
                long scanStart = System.Diagnostics.Stopwatch.GetTimestamp();
                int end = snapshot.Count;
                for (; cursor < end; cursor++)
                {
                    if (cursor > 0 && !ScanTimeBudget.WithinBudget(scanStart, host.ScanBudget.Value)) break;
                    var pair = snapshot[cursor];
                    if (pair.Value == null || pair.Value.GetZDO() != pair.Key) continue;
                    try { if (!Observe(pair.Key, pair.Value)) break; }
                    catch (Exception e) { host.Report("Could not inspect loaded object " + pair.Key.GetPrefab(), e); }
                }
                if (cursor >= snapshot.Count)
                {
                    foreach (var record in records.Values)
                        if (!record.shared && record.observationScan != scanSerial)
                        {
                            if (record.live || record.sightVisible) pinChanges.Add(record);
                            record.live = false; record.sightVisible = false;
                            SharedDiscoveries.HandOffCreature(record, Time.realtimeSinceStartup);
                        }
                    foreach (string id in records.Where(kv => kv.Value.dynamic && !kv.Value.shared && !kv.Value.live && kv.Value.remoteSeenUntil <= Time.realtimeSinceStartup).Select(kv => kv.Key).ToList())
                    {
                        RemovePin(records[id]); records.Remove(id);
                    }
                    snapshot = null; discoveryIndex = null; nextScan = Time.realtimeSinceStartup + (host.SightDetection ? 0.15f : 0.5f);
                    RefreshPins(false);
                }
            }
            expiredRecords.Clear();
            foreach (var pair in records)
                if (pair.Value.shared && pair.Value.dynamic && Time.realtimeSinceStartup > pair.Value.expiresAt) expiredRecords.Add(pair.Key);
            foreach (string id in expiredRecords)
            { RemovePin(records[id]); records.Remove(id); }
            foreach (var record in records.Values)
                if (!record.dynamic && record.remoteSeenUntil > 0 && record.remoteSeenUntil <= Time.realtimeSinceStartup)
                { record.remoteSeenUntil = 0; record.remoteObservers?.Clear(); if (record.pin != null && !Visible(record)) RemovePin(record); }
            bool positionsChanged = false;
            foreach (var record in records.Values)
                if (record.dynamic && record.pin != null && record.character != null)
                {
                    Vector3 position = record.character.transform.position;
                    record.position = position;
                    if (record.pin.m_pos != position) { record.pin.m_pos = position; positionsChanged = true; }
                }
            if (positionsChanged) RequestPinRefresh();
            ReconcilePortals(player);
            Discoveries.Tick();
            if (pendingEntrance != null)
            {
                if (!player.IsTeleporting() && player.InInterior()) { MarkVisited(pendingEntrance); pendingEntrance = null; }
                else if (Time.realtimeSinceStartup > pendingDeadline) pendingEntrance = null;
            }
            if (dirty && Time.realtimeSinceStartup >= nextSave) Save();
        }

        private bool Observe(ZDO zdo, ZNetView view)
        {
            if (host.DetectionDistance > 0 && Player.m_localPlayer != null &&
                Utils.DistanceXZ(view.transform.position, Player.m_localPlayer.transform.position) > host.DetectionDistance) return true;
            CatalogEntry entry;
            bool location = zdo.GetPrefab() == "LocationProxy".GetStableHashCode();
            if (location)
            {
                if (!host.Catalog.Locations.TryGetValue(zdo.GetInt(ZDOVars.s_location), out entry)) return true;
            }
            else if (!host.Catalog.Prefabs.TryGetValue(zdo.GetPrefab(), out entry)) return true;
            Character ch = view.GetComponent<Character>();
            if (ch != null && (ch.IsPlayer() || ch.IsDead())) return true;
            // Never scan rooms or resources in interiors; only external entrances are retained.
            if (!location && view.transform.position.y > 3000f) return true;
            bool dynamic = entry.creature || view.GetComponent<ItemDrop>() != null;
            bool shareDetection = host.Enabled.Value && host.ShareMarkers.Value &&
                (!dynamic || entry.creature && host.ShareCreatures.Value);
            if (!entry.dungeon && !entry.Selected.Value && !shareDetection && !records.ContainsKey(zdo.m_uid.ToString())) return true;
            string id = zdo.m_uid.ToString();
            Vector3 position = view.transform.position;
            if (entry.dungeon)
            {
                Teleport entrance = view.GetComponentsInChildren<Teleport>(true).FirstOrDefault(t => t.transform.position.y < 3000f);
                if (entrance != null) position = entrance.transform.position;
            }
            bool visibleNow = false;
            if (host.SightDetection)
            {
                if (!sight.Check(view, position, Player.m_localPlayer, out visibleNow)) return false;
                if (!visibleNow && !records.ContainsKey(id)) return true;
            }
            bool newIdentity = !records.TryGetValue(id, out var record);
            bool newDiscovery = false;
            if (newIdentity)
            {
                if (entry.dungeon)
                    record = DungeonDiscoveries.Rebind(records, id, entry, position, RemovePin);
                if (record == null && !dynamic)
                    record = discoveryIndex != null ? discoveryIndex.Find(entry.key, position) : SharedDiscoveries.Find(records, id, entry.key, position);
                if (record != null)
                {
                    if (records.ContainsKey(record.id))
                    { records.Remove(record.id); record.id = id; record.type = entry.key; record.entry = entry; }
                    dirty = true;
                }
                else
                {
                    record = new MarkerRecord { id = id, type = entry.key, entry = entry, position = position, dynamic = dynamic };
                    newDiscovery = true;
                }
                records.Add(id, record); if (!dynamic) dirty = true;
                discoveryIndex?.Add(record);
            }
            bool presentationChanged = newIdentity || !record.live || record.entry != entry || record.sightVisible != visibleNow || dynamic;
            record.entry = entry; record.character = ch; record.dynamic = dynamic; record.live = true;
            record.observationScan = scanSerial;
            record.sightVisible = visibleNow;
            if (record.shared && (!host.SightDetection || visibleNow)) { record.shared = false; record.sourcePeer = 0; dirty = true; }
            if (view.GetComponent<TeleportWorld>() is TeleportWorld portal)
            {
                string tag = portal.GetText();
                if (record.portalTag != tag) { record.portalTag = tag; dirty = presentationChanged = true; }
            }
            if (record.position != position) { record.position = position; presentationChanged = true; if (!dynamic) dirty = true; }
            bool collected = false;
            Pickable plant = view.GetComponent<Pickable>();
            if (plant != null)
            {
                collected = plant.GetPicked();
                record.pickedTime = zdo.GetLong(ZDOVars.s_pickedTime);
            }
            else if (view.GetComponent<MineRock>() is MineRock rock && RockEmpty != null) collected = (bool)RockEmpty.Invoke(rock, null);
            else if (view.GetComponent<MineRock5>() is MineRock5 rock5 && Rock5Empty != null) collected = (bool)Rock5Empty.Invoke(rock5, null);
            if (record.collected != collected) { record.collected = collected; presentationChanged = true; if (!dynamic) dirty = true; }
            if (presentationChanged) { record.nameCache = null; pinChanges.Add(record); }
            if (newDiscovery) Discoveries.Detected(record);
            return true;
        }

        private void Destroyed(ZDO zdo)
        {
            try
            {
                if (!records.TryGetValue(zdo.m_uid.ToString(), out var record)) return;
                record.nameCache = null;
                if (record.entry == null) record.entry = host.Catalog.GetSavedDefinition(record.type);
                dirty |= MarkerLifetime.Destroyed(records, record.id, RemovePin);
                RefreshPins();
            }
            catch (Exception e) { host.Report("Destroyed-object tracking failed", e); }
        }

        private void ReconcilePortals(Player player)
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextPortalCheck) return;
            nextPortalCheck = now + 1f;
            if (player.IsTeleporting() || player.InInterior() || manager == null ||
                ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            { portalReconciliation.Clear(); return; }
            portalCandidates.Clear();
            foreach (var record in records.Values)
                if (!record.shared && !record.dynamic && record.entry?.category == "Portals") portalCandidates.Add(record);
            if (portalCandidates.Count == 0) return;

            // Read network data independently of selection, detection radius and
            // line of sight. Uninstantiated but valid portals must also survive.
            nearbyPortalData.Clear();
            manager.FindSectorObjects(ZoneSystem.GetZone(player.transform.position), new SimulationDistance(1, 0), nearbyPortalData);
            bool changed = false;
            foreach (var record in portalCandidates)
            {
                bool ready = Utils.DistanceXZ(record.position, player.transform.position) <= 32f &&
                    ZNetScene.InActiveArea(record.position, ZNet.instance.GetReferencePosition()) &&
                    ZNetScene.instance.IsAreaReady(record.position);
                bool exists = false;
                if (ready)
                {
                    string[] parts = record.id.Split(':');
                    if (parts.Length != 2 || !long.TryParse(parts[0], out var user) || !uint.TryParse(parts[1], out var id))
                        ready = false;
                    else
                    {
                        var remembered = manager.GetZDO(new ZDOID(user, id));
                        exists = remembered != null && remembered.IsValid() && IsPortalData(remembered);
                        if (!exists)
                            foreach (var zdo in nearbyPortalData)
                            {
                                if (!zdo.IsValid() || (zdo.GetPosition() - record.position).sqrMagnitude >= 1f) continue;
                                if (IsPortalData(zdo)) { exists = true; break; }
                            }
                    }
                }
                if (!portalReconciliation.Check(record.id, ready, exists, now)) continue;
                RemovePin(record); records.Remove(record.id); dirty = changed = true;
            }
            if (changed) RefreshPins();
        }
        private bool IsPortalData(ZDO zdo) =>
            (host.Catalog.Prefabs.TryGetValue(zdo.GetPrefab(), out var definition) && definition.category == "Portals") ||
            Game.instance.PortalPrefabHash.Contains(zdo.GetPrefab());

        private bool Visible(MarkerRecord record)
        {
            var entry = record.entry;
            if (!host.Enabled.Value || entry == null) return false;
            // Selecting a remembered destination explicitly reveals that one local
            // record, without enabling its whole catalog category or changing filters.
            if (!record.dynamic && !record.shared && Discoveries.IsTarget(record)) return true;
            if (!entry.Selected.Value) return false;
            if (record.shared && !host.OtherPlayerPins.Value) return false;
            bool remoteVisible = host.OtherPlayerPins.Value && record.remoteSeenUntil > Time.realtimeSinceStartup;
            if (host.SightDetection && !DiscoveryVisibility.ShowSight(!record.dynamic && (entry.KeepVisible?.Value ?? true),
                record.sightVisible && record.live, record.shared, remoteVisible)) return false;
            if (!record.dynamic && !DiscoveryVisibility.Show(entry.KeepVisible?.Value ?? true,
                record.live, record.shared, host.DetectionDistance,
                Player.m_localPlayer != null ? Utils.DistanceXZ(record.position, Player.m_localPlayer.transform.position) : float.MaxValue, remoteVisible)) return false;
            if (entry.creature)
            {
                if (record.shared)
                    return record.creatureRelation == 1 ? host.Hostile.Value : record.creatureRelation == 2 ? host.Friendly.Value :
                        record.creatureRelation == 3 ? host.Animals.Value : record.creatureRelation == 4 && host.Tamed.Value;
                Character ch = record.character;
                if (ch == null || ch.IsDead()) return false;
                if (!remoteVisible && host.DetectionDistance > 0 && Utils.DistanceXZ(ch.transform.position, Player.m_localPlayer.transform.position) > host.DetectionDistance) return false;
                if (ch.IsTamed()) return host.Tamed.Value;
                if (ch.GetBaseAI() is AnimalAI) return host.Animals.Value;
                return BaseAI.IsEnemy(ch, Player.m_localPlayer) ? host.Hostile.Value : host.Friendly.Value;
            }
            if (entry.dungeon) return !record.completed || host.ShowCompleted.Value;
            if (!record.collected) return true;
            if (entry.category == "Ore") return host.ShowMined.Value;
            return host.ShowPickedPlants.Value;
        }
        internal string Name(MarkerRecord record)
        {
            if (record.nameCache != null) return record.nameCache;
            string label = record.entry.Label;
            if (record.entry.category == "Portals" && !string.IsNullOrWhiteSpace(record.portalTag)) label += ": " + record.portalTag;
            if (record.favorite) label = "[Fav] " + label;
            return record.nameCache = record.collected ? label + " — " + Plugin.Text("Собрано", "Collected") : label;
        }
        private string DisplayName(MarkerRecord record) => record.nameGroupMembers != null ? record.nameGroupCaption :
            Name(record) + (record.group != null ? " ×" + record.group.Count : "");
        internal List<MarkerRecord> OwnDiscoveries()
        {
            var result = records.Values.Where(r => !r.dynamic && !r.shared && r.entry != null).ToList();
            foreach (var record in result) record.observed = record.live && (!host.SightDetection || record.sightVisible);
            return result;
        }
        internal List<MarkerRecord> OwnCreatures()
        {
            var result = new List<MarkerRecord>();
            foreach (var record in records.Values)
            {
                Character ch = record.character;
                if (!record.dynamic || record.shared || !record.live || !record.entry.creature || ch == null || ch.IsDead() ||
                    (host.SightDetection && !record.sightVisible) ||
                    (host.DetectionDistance > 0 && Utils.DistanceXZ(ch.transform.position, Player.m_localPlayer.transform.position) > host.DetectionDistance)) continue;
                record.position = ch.transform.position; record.creatureLevel = ch.GetLevel();
                record.creatureRelation = ch.IsTamed() ? 4 : ch.GetBaseAI() is AnimalAI ? 3 : BaseAI.IsEnemy(ch, Player.m_localPlayer) ? 1 : 2;
                result.Add(record);
            }
            return result;
        }
        internal void ReceiveCreatures(long sender, List<MarkerRecord> incoming)
        {
            if (!loaded) return;
            foreach (var record in incoming)
            {
                if (record == null || !ValidSharedRecord(record) || record.creatureRelation < 1 || record.creatureRelation > 4 || record.creatureLevel < 1 || record.creatureLevel > 100) continue;
                var entry = host.Catalog.Entries.FirstOrDefault(e => e.key == record.type);
                if (entry == null || !entry.creature) continue;
                if (records.TryGetValue(record.id, out var existing))
                {
                    SharedDiscoveries.ObserveCreature(existing, record, sender, Time.realtimeSinceStartup);
                    if (!existing.shared) continue;
                    existing.position = record.position; existing.creatureLevel = record.creatureLevel; existing.creatureRelation = record.creatureRelation;
                    existing.sourcePeer = sender; existing.expiresAt = Time.realtimeSinceStartup + 4;
                    if (existing.pin != null) existing.pin.m_pos = existing.position;
                }
                else
                {
                    record.entry = entry; record.shared = true; record.sourcePeer = sender; record.dynamic = true;
                    record.expiresAt = Time.realtimeSinceStartup + 4; records[record.id] = record;
                    SharedDiscoveries.ObserveCreature(record, record, sender, Time.realtimeSinceStartup);
                }
            }
            RefreshPins();
        }
        private static bool ValidSharedRecord(MarkerRecord record)
        {
            return !string.IsNullOrEmpty(record.id) && record.id.Length <= 256 && !string.IsNullOrEmpty(record.type) && record.type.Length <= 256 &&
                !float.IsNaN(record.position.x) && !float.IsNaN(record.position.y) && !float.IsNaN(record.position.z) &&
                !float.IsInfinity(record.position.x) && !float.IsInfinity(record.position.y) && !float.IsInfinity(record.position.z) &&
                Mathf.Abs(record.position.x) <= 20000 && Mathf.Abs(record.position.z) <= 20000 && Mathf.Abs(record.position.y) <= 3000;
        }
        internal void ReceiveDiscoveries(long sender, List<MarkerRecord> incoming)
        {
            if (!loaded) return;
            var index = discoveryIndex ?? new SharedDiscoveryIndex(records.Values);
            foreach (var record in incoming)
            {
                if (record == null || !ValidSharedRecord(record)) continue;
                CatalogEntry entry = host.Catalog.Entries.FirstOrDefault(e => e.key == record.type);
                if (entry == null && record.imported && record.type.StartsWith("legacy:", StringComparison.Ordinal))
                    entry = host.Catalog.GetLegacyDungeon(record.type.Substring("legacy:".Length));
                if (entry == null || entry.creature || entry.category == "Dropped items") continue;
                record.entry = entry;
                SharedDiscoveries.Receive(records, record, sender, Time.realtimeSinceStartup, index); dirty = true;
            }
            RefreshPins();
        }
        private void RefreshPins(bool all = true)
        {
            if (map == null) return;
            bool changed = false;
            foreach (var record in all ? (IEnumerable<MarkerRecord>)records.Values : pinChanges)
            {
                if (!records.TryGetValue(record.id, out var current) || current != record) continue;
                if (!Visible(record)) { changed |= record.pin != null; RemovePin(record); continue; }
                string name = Name(record);
                if (record.pin != null && record.pin.m_name != name) { RemovePin(record); changed = true; }
                if (record.pin == null)
                {
                    record.pin = map.AddPin(record.position, Minimap.PinType.Icon3, name, false, record.collected || record.completed);
                    changed = true;
                    if (!record.entry.creature && record.entry.Icon != null) record.pin.m_icon = record.entry.Icon;
                }
                if (record.pin.m_pos != record.position) { record.pin.m_pos = record.position; changed = true; }
                bool check = record.collected || record.completed;
                if (record.pin.m_checked != check) { record.pin.m_checked = check; changed = true; }
            }
            pinChanges.Clear();
            if (changed || all) RequestPinRefresh();
        }
        internal void RequestPinRefresh() { if (Minimap.instance != null) PinUpdateRequired.SetValue(Minimap.instance, true); }
        internal void DetectionChanged()
        {
            foreach (var record in records.Values) record.sightVisible = false;
            snapshot = null; nextScan = 0;
            RefreshPins(); RequestPinRefresh();
        }
        internal void StylePins()
        {
            if (map == null || map != Minimap.instance) { ApplySharedPinVisibility(Minimap.instance); return; }
            bool visible = map.m_mode == Minimap.MapMode.Large ? host.LargeMap.Value : host.SmallMap.Value;
            var area = map.m_mode == Minimap.MapMode.Large ? map.m_mapImageLarge.rectTransform : map.m_mapImageSmall.rectTransform;
            var canvas = area.GetComponentInParent<Canvas>()?.rootCanvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            foreach (var record in records.Values)
            {
                var pin = record.pin;
                if (pin?.m_uiElement == null) continue;
                bool onMap = visible && host.Enabled.Value && (Discoveries.IsNavigationTarget(record) || host.MapCategories.Visible(record.entry.category, map.m_mode == Minimap.MapMode.Large)) && MarkerPresentation.OnMap(area, camera, pin.m_uiElement);
                if (pin.m_uiElement.gameObject.activeSelf != onMap) pin.m_uiElement.gameObject.SetActive(onMap);
                if (!onMap) { record.nearbyName?.Hide(); continue; }
                bool personalTarget = Discoveries.IsTarget(record), sharedTarget = Discoveries.IsSharedObject(record);
                if (record.targetHighlight == null) record.targetHighlight = new TargetMarkerHighlight();
                record.targetHighlight.Apply(pin.m_uiElement, personalTarget, sharedTarget);
                if (pin.m_NamePinData?.PinNameGameObject != null)
                    pin.m_NamePinData.PinNameGameObject.SetActive(visible && host.Enabled.Value && !host.HideLargeMapNames.Value &&
                        map.m_mode == Minimap.MapMode.Large && MapLabelZoom.Visible(map.LargeZoom, map.m_minZoom, map.m_showNamesZoom));
                if (pin.m_iconElement != null)
                    pin.m_iconElement.color = personalTarget || sharedTarget ? TargetHighlight.Tint(personalTarget, sharedTarget) : record.collected || record.completed ? Color.gray :
                        record.visited ? new Color(0.45f, 0.75f, 1f) : record.entry.creature ?
                        (record.shared ? SharedCreatureColor(record.creatureRelation) : CreatureColor(record.character)) : record.favorite || Discoveries.IsTarget(record) ? new Color(1f, 0.8f, 0.25f) : Color.white;
                if (record.entry.creature)
                {
                    bool large = map.m_mode == Minimap.MapMode.Large;
                    float zoom = large ? map.LargeZoom : map.SmallZoom;
                    float baseZoom = large ? 0.1f : 0.01f;
                    float size = large ? Mathf.Clamp(7f * Mathf.Sqrt(baseZoom / Mathf.Max(zoom, 0.0001f)), 3f, 12f) :
                        Mathf.Clamp(10f * Mathf.Sqrt(baseZoom / Mathf.Max(zoom, 0.0001f)), 6f, 16f);
                    Sprite sprite = record.entry.Icon ?? map.m_icons.FirstOrDefault(i => i.m_name == Minimap.PinType.Death).m_icon;
                    bool showHead = sprite != null && CreatureDisplay.ShowHead(large,
                        Player.m_localPlayer != null ? Utils.DistanceXZ(record.position, Player.m_localPlayer.transform.position) : float.MaxValue,
                        zoom, map.m_minZoom, map.m_showNamesZoom, large ? host.CreatureIcons.Value : host.MinimapCreatureIcons.Value);
                    if (showHead) size = large ? host.LargeMapCreatureIconSize.Value : host.MinimapCreatureIconSize.Value;
                    pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
                    pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);
                    if (pin.m_iconElement != null) pin.m_iconElement.enabled = !showHead;
                    if (record.creatureIcon == null) record.creatureIcon = new CreatureMapIcon();
                    record.creatureIcon.Update(pin.m_uiElement, sprite, size, showHead, host.ContrastCreatureIcons.Value, host.CreatureOutlineThickness.Value);
                    if (record.stars == null) record.stars = new CreatureStars();
                    int starCount = record.shared ? record.creatureLevel - 1 : record.character != null ? record.character.GetLevel() - 1 : 0;
                    record.stars.Update(pin.m_uiElement, starCount, size);
                }
                else if (record.entry.dungeon)
                {
                    float size = map.m_mode == Minimap.MapMode.Large ? 24f : 18f;
                    pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
                    pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);
                    if (record.dungeonIcon == null) record.dungeonIcon = new DungeonIcon();
                    record.dungeonIcon.Update(pin, record.entry.prefab);
                }
            }
            grouping.Apply(records.Values, map, host, Name);
            creatureNameGroups.Apply(records.Values, map, host);
            largeLabels.Apply(records.Values, map, host);
            EnforceNameVisibility();
            grouping.DrawTooltip(map, host, Name);
            ApplySharedPinVisibility(map);
        }
        private void ApplySharedPinVisibility(Minimap currentMap)
        {
            if (currentMap != null && host.Enabled.Value && !host.OtherPlayerPins.Value && NativePins.GetValue(currentMap) is List<Minimap.PinData> nativePins)
                foreach (var shared in nativePins)
                {
                    if (shared.m_ownerID == 0) continue;
                    if (!hiddenSharedPins.TryGetValue(shared, out var mask))
                    { mask = new SharedPinMask(); hiddenSharedPins.Add(shared, mask); }
                    mask.Hide(shared);
                }
        }
        internal void PrepareSharedPins() { ReleaseSharedMasks(false); }
        internal bool HasGroupDetails(MarkerRecord record) => grouping.ShowsDetails(record);
        internal void KeepSharedPinsHidden()
        {
            ReleaseSharedMasks(false);
            foreach (var pair in hiddenSharedPins) pair.Value.Hide(pair.Key);
            EnforceNameVisibility();
            UpdateNearbyNames();
            Discoveries.DrawNavigation(map);
            creatureHover.Apply(records.Values, map, host, Name);
            playerLayer.Apply(Minimap.instance, host.Enabled.Value && !host.Window.Open);
            DiagnoseMinimapNames();
        }
        private void DiagnoseMinimapNames()
        {
            if (map == null || Player.m_localPlayer == null || map.m_mode == Minimap.MapMode.Large ||
                !map.m_smallRoot.activeInHierarchy || host.HideMinimapNames.Value || nameDiagnosticSamples >= 4 ||
                Time.realtimeSinceStartup < nextNameDiagnostic) return;
            var active = records.Values.Where(r => r.entry?.creature == true && r.pin?.m_uiElement != null &&
                r.pin.m_uiElement.gameObject.activeInHierarchy).ToList();
            if (active.Count == 0) return;
            var player = Player.m_localPlayer.transform.position;
            var nearest = active.OrderBy(r => NearbyNameDistance(r, player)).First();
            float distance = NearbyNameDistance(nearest, player);
            var canvas = map.m_smallRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            host.NameDiagnostic("mode=" + map.m_mode + ", visibleCreatures=" + active.Count +
                ", nameRadius=" + host.MinimapNameDistance.Value + "m, withinNameRadius=" + active.Count(r => CreatureDisplay.ShowNearbyName(NearbyNameDistance(r, player), host.MinimapNameDistance.Value)) +
                ", nearest=" + Name(nearest) + ", distance=" + distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                "m, " + (nearest.nearbyName?.RenderState(camera) ?? "label=not-created"));
            nameDiagnosticSamples++; nextNameDiagnostic = Time.realtimeSinceStartup + 8;
        }
        private void EnforceNameVisibility()
        {
            if (map == null || map != Minimap.instance) return;
            bool namesAllowed = host.Enabled.Value && host.LargeMap.Value && !host.HideLargeMapNames.Value && map.m_mode == Minimap.MapMode.Large &&
                MapLabelZoom.Visible(map.LargeZoom, map.m_minZoom, map.m_showNamesZoom);
            foreach (var record in records.Values)
            {
                if (record.pin == null || (record.nameVisibility == null && record.pin.m_NamePinData?.PinNameGameObject == null)) continue;
                NameTextStyle.Apply(record.pin.m_NamePinData?.PinNameText,
                    record.entry?.creature == true ? host.CreatureNameSize.Value : host.ObjectNameSize.Value);
                if (record.nameVisibility == null) record.nameVisibility = new NativeNameVisibility();
                record.nameVisibility.Apply(record.pin.m_NamePinData?.PinNameGameObject,
                    namesAllowed && record.largeNameShown && record.pin.m_uiElement != null && record.pin.m_uiElement.gameObject.activeInHierarchy);
            }
        }
        private void UpdateNearbyNames()
        {
            if (map == null) return;
            var player = Player.m_localPlayer;
            bool enabled = host.Enabled.Value && host.SmallMap.Value && !host.HideMinimapNames.Value &&
                map.m_mode != Minimap.MapMode.Large && map.m_smallRoot.activeInHierarchy && player != null;
            nearbyNames.Clear(); nearbyNameBoxes.Clear();
            foreach (var record in records.Values)
            {
                if (record.entry?.creature != true) continue;
                bool show = enabled && !record.nameGroupFollower && record.pin?.m_uiElement != null && record.pin.m_uiElement.gameObject.activeInHierarchy &&
                    (Discoveries.IsNavigationTarget(record) || MarkerPresentation.Nearby(record.nearbyNameShown, NearbyNameDistance(record, player.transform.position), host.MinimapNameDistance.Value, host.NameMargin.Value));
                record.nearbyNameShown = show;
                if (!show) { record.nearbyName?.Hide(); continue; }
                if (record.nearbyName == null) record.nearbyName = new CreatureNearbyName();
                record.nearbyName.Show(record.pin.m_uiElement, map, DisplayName(record), host.CreatureNameSize.Value,
                    record.nameGroupMembers != null ? (Vector2?)record.nameGroupScreen : null);
                nearbyNames.Add(record);
            }
            if (nearbyNames.Count == 0) return;
            nearbyNames.Sort((a, b) =>
            {
                return MarkerPresentation.NameOrder(Discoveries.IsNavigationTarget(a), NearbyNameDistance(a, player.transform.position), a.id,
                    Discoveries.IsNavigationTarget(b), NearbyNameDistance(b, player.transform.position), b.id);
            });
            var canvas = map.m_smallRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            nearbyNameBoxes.Add(MarkerPresentation.PlayerArea(map, false, camera, canvas != null ? canvas.scaleFactor : 1));
            var viewport = MarkerPresentation.MapArea(map.m_mapImageSmall.rectTransform, camera);
            int accepted = 0;
            for (int i = 0; i < nearbyNames.Count; i++)
            {
                var record = nearbyNames[i]; var bounds = record.nearbyName.Bounds(camera);
                if (!LabelLayout.Inside(bounds, viewport)) { record.nearbyName.Hide(); continue; }
                nearbyNames[accepted++] = record; nearbyNameBoxes.Add(bounds);
            }
            nearbyNames.RemoveRange(accepted, nearbyNames.Count - accepted);
            bool[] shown = nearbySelection.Choose(nearbyNameBoxes, 3 * (canvas != null ? canvas.scaleFactor : 1));
            for (int i = 0; i < nearbyNames.Count; i++)
                if (!shown[i + 1]) nearbyNames[i].nearbyName.Hide();
        }
        internal static float NearbyNameDistance(MarkerRecord record, Vector3 player)
        {
            float nearest = Utils.DistanceXZ(record.position, player);
            if (record.group != null)
                foreach (var member in record.group) nearest = Mathf.Min(nearest, Utils.DistanceXZ(member.position, player));
            if (record.nameGroupMembers != null)
                foreach (var member in record.nameGroupMembers) nearest = Mathf.Min(nearest, Utils.DistanceXZ(member.position, player));
            return nearest;
        }
        private void ReleaseSharedMasks(bool force)
        {
            releasedSharedPins.Clear();
            foreach (var pair in hiddenSharedPins)
            {
                var pin = pair.Key;
                if (force || !host.Enabled.Value || host.OtherPlayerPins.Value || pin.m_ownerID == 0 ||
                    (pin.m_uiElement == null && pin.m_NamePinData?.PinNameGameObject == null))
                { pair.Value.Restore(); releasedSharedPins.Add(pin); }
            }
            foreach (var pin in releasedSharedPins) hiddenSharedPins.Remove(pin);
        }
        private bool NavigationAvailable()
        {
            if (!host.Enabled.Value || map == null || map.m_mode != Minimap.MapMode.Large || host.Window.Open || Player.m_localPlayer == null) return false;
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            bool typing = selected != null && (selected.GetComponent<UnityEngine.UI.InputField>()?.isFocused == true ||
                selected.GetComponent<TMPro.TMP_InputField>()?.isFocused == true);
            return !typing && Chat.instance?.HasFocus() != true && !global::Console.IsVisible() &&
                !TextInput.IsVisible() && !InventoryGui.IsVisible() && !Menu.IsVisible();
        }
        private NavigationBindingAction NavigationBinding(KeyCode button) => NavigationAvailable() ?
            MapTargetGesture.BindingAction(host.SelectNavigationKey.Value, host.ShareNavigationKey.Value, button, Input.GetKey, host.ShareMarkers.Value) :
            NavigationBindingAction.None;
        internal bool NavigationMouse(KeyCode button) => NavigationBinding(button) == NavigationBindingAction.Select;
        internal bool ShareNavigationMouse(KeyCode button) => NavigationBinding(button) == NavigationBindingAction.Share;
        private static bool UnhookedNavigationKey(KeyCode key) => key != KeyCode.None &&
            key != KeyCode.Mouse0 && key != KeyCode.Mouse1 && key != KeyCode.Mouse2;
        internal void HandleNavigationKey()
        {
            if (!NavigationAvailable()) return;
            var shareKey = host.ShareNavigationKey.Value.MainKey;
            bool share = UnhookedNavigationKey(shareKey) && Input.GetKeyDown(shareKey) && ShareNavigationMouse(shareKey);
            var selectKey = host.SelectNavigationKey.Value.MainKey;
            bool select = !share && UnhookedNavigationKey(selectKey) && Input.GetKeyDown(selectKey) && NavigationMouse(selectKey);
            if (!share && !select) return;
            var canvas = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.RectangleContainsScreenPoint(map.m_mapImageLarge.rectTransform, ZInput.pointerPosition, camera))
                MapClick(navigation: select, shareTarget: share);
        }
        internal bool MapClick(bool toggle = true, bool navigation = false, bool clearSelected = false, bool shareTarget = false)
        {
            if (host.Window.Open) return false;
            if (map == null) return true;
            Vector2 pointer = ZInput.pointerPosition;
            if (host.Window.ContainsMapButtonPointer()) return false;
            MarkerRecord closest = null; float distance = float.MaxValue;
            float iconScale = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas.scaleFactor ?? 1;
            foreach (var record in records.Values)
            {
                if (record.pin?.m_uiElement == null || !record.pin.m_uiElement.gameObject.activeInHierarchy) continue;
                float d = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, record.pin.m_uiElement.position), pointer);
                float reach = record.entry.creature ? Mathf.Max(18, record.pin.m_uiElement.rect.width * 0.5f * iconScale) : 18;
                if (d <= reach && d < distance) { distance = d; closest = record; }
            }
            bool waypointHit = Discoveries.IsWaypointTarget && Discoveries.WaypointUI != null &&
                Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, Discoveries.WaypointUI.position), pointer) <= 18 * iconScale;
            bool sharedWaypointHit = !waypointHit && Discoveries.SharedWaypointUI != null &&
                Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, Discoveries.SharedWaypointUI.position), pointer) <= 18 * iconScale;
            if (sharedWaypointHit)
            {
                if (clearSelected && NavigationAvailable()) { Discoveries.ClearSharedTarget(); return false; }
                if (navigation && toggle) { Discoveries.SelectPosition(map, Discoveries.SharedTarget.position, Discoveries.SharedTarget.nameCache, Discoveries.SharedTarget.entry); return false; }
                return false; // Received navigation is never forwarded as another shared target.
            }
            ZNet.PlayerInfo clickedPlayer = default; string playerName = null; float playerDistance = float.MaxValue;
            bool playerHit = (navigation || clearSelected || shareTarget) && !waypointHit && PlayerNavigation.TryPick(map, pointer, out clickedPlayer, out playerName, out playerDistance) && playerDistance <= distance;
            if (shareTarget)
            {
                MarkerRecord selected = waypointHit ? Discoveries.Target : playerHit ?
                    new MarkerRecord { position = clickedPlayer.m_position, nameCache = playerName, type = "navigation:player" } : closest;
                if (selected != null) host.sharing.SendNavigationTarget(selected.position, selected.nameCache ?? Name(selected),
                    selected.type?.StartsWith("navigation:", StringComparison.Ordinal) == true ? selected.entry?.key ?? selected.type : selected.type ?? selected.entry?.key, selected.id);
                else if (DiscoveryTools.TryMapPosition(map, pointer, out var position))
                    host.sharing.SendNavigationTarget(position, "Map destination");
                return false;
            }
            if (clearSelected && NavigationAvailable() && (waypointHit ||
                (playerHit ? Discoveries.IsPlayerTarget(clickedPlayer) : closest != null && Discoveries.IsTarget(closest))))
            { grouping.Close(); Discoveries.ClearTarget(); RequestPinRefresh(); return false; }
            if (clearSelected && NavigationAvailable() && closest != null && Discoveries.IsSharedObject(closest))
            { Discoveries.ClearSharedTarget(); return false; }
            var targetAction = MapTargetGesture.Resolve(navigation, toggle,
                closest != null || waypointHit || playerHit, waypointHit || (playerHit ? Discoveries.IsPlayerTarget(clickedPlayer) : closest != null && Discoveries.IsTarget(closest)), !playerHit && closest?.group != null && !grouping.IsExpanded(closest));
            if (targetAction != MapTargetAction.None)
            {
                if (targetAction == MapTargetAction.Expand) grouping.Toggle(closest);
                else if (targetAction == MapTargetAction.Select)
                { grouping.Close(); if (playerHit) Discoveries.SelectPlayer(clickedPlayer, playerName); else Discoveries.Select(closest); }
                else if (targetAction == MapTargetAction.Clear) { grouping.Close(); Discoveries.ClearTarget(); }
                else if (targetAction == MapTargetAction.Place) { grouping.Close(); Discoveries.SelectPoint(map, pointer); }
                RequestPinRefresh(); return false;
            }
            if (waypointHit) return false; // Temporary navigation pins are owned by MapTracker.
            if (closest == null) { if (toggle) { grouping.Close(); RequestPinRefresh(); } return true; }
            if (toggle && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                grouping.Close();
                RequestPinRefresh();
                host.Window.Focus(closest.entry);
                return false;
            }
            if (toggle && closest.group != null)
            {
                grouping.Toggle(closest);
                RequestPinRefresh();
                return false;
            }
            if (toggle) { grouping.Close(); RequestPinRefresh(); }
            if (toggle && closest.entry.dungeon)
            {
                closest.completed = !closest.completed; closest.shared = false; closest.sourcePeer = 0;
                dirty = true; RefreshPins(); Save();
            }
            return false;
        }
        private static Color CreatureColor(Character ch)
        {
            if (ch == null) return Color.white;
            if (ch.IsTamed()) return Color.cyan;
            if (ch.GetBaseAI() is AnimalAI) return Color.yellow;
            return BaseAI.IsEnemy(ch, Player.m_localPlayer) ? Color.red : Color.green;
        }
        private static Color SharedCreatureColor(int relation)
        { return relation == 1 ? Color.red : relation == 2 ? Color.green : relation == 3 ? Color.yellow : Color.cyan; }
        internal void Enter(Teleport entrance)
        {
            pendingEntrance = entrance; pendingDeadline = Time.realtimeSinceStartup + 20;
        }
        private void MarkVisited(Teleport entrance)
        {
            MarkerRecord closest = records.Values.Where(r => r.entry?.dungeon == true)
                .OrderBy(r => Vector3.Distance(r.position, entrance.transform.position)).FirstOrDefault();
            if (closest != null && Vector3.Distance(closest.position, entrance.transform.position) < 20f)
            { closest.visited = true; closest.shared = false; closest.sourcePeer = 0; dirty = true; RefreshPins(); Save(); }
        }
        private void Load()
        {
            if (!File.Exists(savePath)) return;
            try
            {
                var file = DataJson.Read<MarkerFile>(File.ReadAllText(savePath, Encoding.UTF8));
                if (file == null || file.schema != 1 || file.world != world || file.character != character) return;
                if (file.markers == null) return;
                foreach (var record in file.markers)
                {
                    if (record == null || string.IsNullOrEmpty(record.id) || string.IsNullOrEmpty(record.type)) continue;
                    record.entry = host.Catalog.GetSavedDefinition(record.type);
                    if (record.imported && record.type.StartsWith("legacy:", StringComparison.Ordinal))
                        record.entry = host.Catalog.GetLegacyDungeon(record.type.Substring("legacy:".Length));
                    records[record.id] = record;
                }
                dirty |= MarkerLifetime.RemoveDestroyedPortals(records, RemovePin);
                dirty |= DungeonDiscoveries.RemoveDuplicates(records, RemovePin);
                dirty |= SharedDiscoveries.RemoveDuplicates(records, RemovePin);
            }
            catch (Exception e) { host.Report("Saved markers could not be loaded; original file retained", e); savePath = null; }
        }
        private void ResolveSavedDefinitions()
        {
            foreach (var record in records.Values)
            {
                var entry = host.Catalog.GetSavedDefinition(record.type);
                if (entry != null && entry != record.entry) { RemovePin(record); record.entry = entry; record.nameCache = null; }
            }
            dirty |= MarkerLifetime.RemoveDestroyedPortals(records, RemovePin);
            dirty |= DungeonDiscoveries.RemoveDuplicates(records, RemovePin);
            dirty |= SharedDiscoveries.RemoveDuplicates(records, RemovePin);
        }
        private bool Save()
        {
            if (!dirty) return true;
            if (string.IsNullOrEmpty(savePath)) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                var file = new MarkerFile { world = world, character = character, markers = records.Values.Where(r => !r.dynamic).ToList() };
                string temp = savePath + ".tmp";
                string json = DataJson.Write(file, true);
                if (json.IndexOf("\"markers\"", StringComparison.Ordinal) < 0) throw new InvalidOperationException("Serializer omitted the saved markers.");
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(savePath)) File.Replace(temp, savePath, savePath + ".bak"); else File.Move(temp, savePath);
                dirty = false; nextSave = Time.realtimeSinceStartup + 10;
                return true;
            }
            catch (Exception e) { host.Report("Saving markers failed", e); nextSave = Time.realtimeSinceStartup + 30; return false; }
        }
        private void RemovePin(MarkerRecord record)
        {
            record.nearbyName?.Dispose(); record.nearbyName = null;
            record.nameVisibility?.Dispose(); record.nameVisibility = null;
            if (record.pin != null && map != null) map.RemovePin(record.pin);
            record.pin = null;
        }
        private void RemovePins() { foreach (var record in records.Values) RemovePin(record); }
        private void Reset()
        {
            Save(); RemovePins(); records.Clear(); pinChanges.Clear(); snapshotBuffer.Clear(); snapshot = null; discoveryIndex = null; pendingEntrance = null;
            Discoveries.Reset();
            creatureNameGroups.Reset();
            grouping.Dispose(); creatureHover.Dispose();
            if (manager != null) manager.m_onZDODestroyed -= Destroyed;
            manager = null; loaded = false; dirty = false; map = null;
            portalReconciliation.Clear(); portalCandidates.Clear(); nearbyPortalData.Clear(); nextPortalCheck = 0;
            definitionsResolved = false; nextEarlyRefresh = 0;
            ResetNameDiagnostics();
        }
        public void Dispose() { Reset(); ReleaseSharedMasks(true); playerLayer.Dispose(); }
    }

    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class MarkerStylePatch
    {
        private static void Prefix() { Plugin.Instance?.Tracking?.PrepareSharedPins(); }
        private static void Postfix() { Plugin.Instance?.Tracking?.StylePins(); }
    }
    [HarmonyPatch(typeof(Minimap), "OnMapLeftClick")]
    internal static class DungeonClickPatch
    {
        private static bool Prefix()
        {
            var tracking = Plugin.Instance?.Tracking;
            if (tracking?.ShareNavigationMouse(KeyCode.Mouse0) == true) return tracking.MapClick(shareTarget: true);
            return tracking?.MapClick(navigation: tracking.NavigationMouse(KeyCode.Mouse0)) ?? true;
        }
    }
    [HarmonyPatch(typeof(Minimap), "OnMapDblClick")]
    internal static class MarkerDoubleClickPatch
    {
        private static bool Prefix()
        {
            var tracking = Plugin.Instance?.Tracking;
            if (tracking?.ShareNavigationMouse(KeyCode.Mouse0) == true) return false;
            return tracking?.MapClick(false, tracking.NavigationMouse(KeyCode.Mouse0)) ?? true;
        }
    }
    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    internal static class MarkerRightClickPatch
    {
        private static bool Prefix()
        {
            var tracking = Plugin.Instance?.Tracking;
            if (tracking?.ShareNavigationMouse(KeyCode.Mouse1) == true) return tracking.MapClick(shareTarget: true);
            bool navigation = tracking?.NavigationMouse(KeyCode.Mouse1) == true;
            return tracking?.MapClick(navigation, navigation, clearSelected: true) ?? true;
        }
    }
    [HarmonyPatch(typeof(Minimap), "OnMapMiddleClick")]
    internal static class NavigationMiddleClickPatch
    {
        private static bool Prefix()
        {
            var tracking = Plugin.Instance?.Tracking;
            if (tracking?.ShareNavigationMouse(KeyCode.Mouse2) == true) return tracking.MapClick(shareTarget: true);
            return tracking?.NavigationMouse(KeyCode.Mouse2) != true || tracking.MapClick(navigation: true);
        }
    }
    [HarmonyPatch(typeof(Teleport), "Interact")]
    internal static class DungeonEnterPatch
    {
        private static void Prefix(Humanoid character, ref bool __state)
        {
            __state = character == Player.m_localPlayer && !character.InInterior();
        }
        private static void Postfix(Teleport __instance, bool __result, bool __state)
        {
            if (__result && __state) Plugin.Instance?.Tracking?.Enter(__instance);
        }
    }
}
