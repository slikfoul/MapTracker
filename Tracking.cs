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
        public bool shared;
        public long sourcePeer;
        public int creatureLevel, creatureRelation;
        [NonSerialized] internal float expiresAt;
        [NonSerialized] internal bool live, dynamic, sightVisible;
        [NonSerialized] internal int observationScan;
        [NonSerialized] internal Character character;
        [NonSerialized] internal CatalogEntry entry;
        [NonSerialized] internal Minimap.PinData pin;
        [NonSerialized] internal CreatureStars stars;
        [NonSerialized] internal DungeonIcon dungeonIcon;
        [NonSerialized] internal CreatureMapIcon creatureIcon;
        [NonSerialized] internal CreatureNearbyName nearbyName;
        [NonSerialized] internal List<MarkerRecord> group;
        [NonSerialized] internal ClusterBadge badge;
        [NonSerialized] internal NativeNameVisibility nameVisibility;
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
        private readonly CreatureHoverName creatureHover = new CreatureHoverName();
        private readonly List<MarkerRecord> nearbyNames = new List<MarkerRecord>();
        private readonly List<LabelBox> nearbyNameBoxes = new List<LabelBox>();
        private readonly LabelLayout.Placement nearbyPlacement = new LabelLayout.Placement();
        private readonly LargeMapLabelLayout largeLabels = new LargeMapLabelLayout();
        private readonly PlayerMarkerLayer playerLayer = new PlayerMarkerLayer();
        private readonly SightVisibility sight = new SightVisibility();
        private float pendingDeadline;
        private float nextEarlyRefresh;
        private bool definitionsResolved;
        internal Tracking(Plugin plugin) { host = plugin; }
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
            if (snapshot == null && Time.realtimeSinceStartup >= nextScan)
            {
                scanSerial++;
                var instances = Instances.GetValue(ZNetScene.instance) as Dictionary<ZDO, ZNetView>;
                snapshot = instances?.ToList() ?? new List<KeyValuePair<ZDO, ZNetView>>();
                cursor = 0;
            }
            if (snapshot != null)
            {
                int end = Math.Min(snapshot.Count, cursor + 128);
                for (; cursor < end; cursor++)
                {
                    var pair = snapshot[cursor];
                    if (pair.Value == null || pair.Value.GetZDO() != pair.Key) continue;
                    try { if (!Observe(pair.Key, pair.Value)) break; }
                    catch (Exception e) { host.Report("Could not inspect loaded object " + pair.Key.GetPrefab(), e); }
                }
                if (cursor >= snapshot.Count)
                {
                    foreach (var record in records.Values)
                        if (!record.shared && record.observationScan != scanSerial) { record.live = false; record.sightVisible = false; }
                    foreach (string id in records.Where(kv => kv.Value.dynamic && !kv.Value.shared && !kv.Value.live).Select(kv => kv.Key).ToList())
                    {
                        RemovePin(records[id]); records.Remove(id);
                    }
                    snapshot = null; nextScan = Time.realtimeSinceStartup + (host.SightDetection ? 0.15f : 0.5f);
                    RefreshPins();
                }
            }
            foreach (string id in records.Where(kv => kv.Value.shared && kv.Value.dynamic && Time.realtimeSinceStartup > kv.Value.expiresAt).Select(kv => kv.Key).ToList())
            { RemovePin(records[id]); records.Remove(id); }
            bool positionsChanged = false;
            foreach (var record in records.Values)
                if (record.dynamic && record.pin != null && record.character != null)
                {
                    Vector3 position = record.character.transform.position;
                    if (record.pin.m_pos != position) { record.pin.m_pos = position; positionsChanged = true; }
                }
            if (positionsChanged) RequestPinRefresh();
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
            if (!entry.dungeon && entry.Selected.Value == false && !records.ContainsKey(zdo.m_uid.ToString())) return true;
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
            if (!records.TryGetValue(id, out var record))
            {
                if (entry.dungeon)
                    record = records.Values.FirstOrDefault(r => r.imported && r.entry?.dungeon == true &&
                        Utils.DistanceXZ(r.position, view.transform.position) < 2f);
                if (record != null)
                {
                    records.Remove(record.id); record.id = id; record.type = entry.key; record.entry = entry;
                }
                else record = new MarkerRecord { id = id, type = entry.key, entry = entry, position = view.transform.position, dynamic = dynamic };
                records.Add(id, record); if (!dynamic) dirty = true;
            }
            record.entry = entry; record.character = ch; record.dynamic = dynamic; record.live = true;
            record.observationScan = scanSerial;
            record.sightVisible = visibleNow;
            if (record.shared && (!host.SightDetection || visibleNow)) { record.shared = false; record.sourcePeer = 0; dirty = true; }
            if (view.GetComponent<TeleportWorld>() is TeleportWorld portal)
            {
                string tag = portal.GetText();
                if (record.portalTag != tag) { record.portalTag = tag; dirty = true; }
            }
            if (record.position != position) { record.position = position; if (!dynamic) dirty = true; }
            bool collected = false;
            Pickable plant = view.GetComponent<Pickable>();
            if (plant != null)
            {
                collected = plant.GetPicked();
                record.pickedTime = zdo.GetLong(ZDOVars.s_pickedTime);
            }
            else if (view.GetComponent<MineRock>() is MineRock rock && RockEmpty != null) collected = (bool)RockEmpty.Invoke(rock, null);
            else if (view.GetComponent<MineRock5>() is MineRock5 rock5 && Rock5Empty != null) collected = (bool)Rock5Empty.Invoke(rock5, null);
            if (record.collected != collected) { record.collected = collected; if (!dynamic) dirty = true; }
            return true;
        }

        private void Destroyed(ZDO zdo)
        {
            try
            {
                if (!records.TryGetValue(zdo.m_uid.ToString(), out var record)) return;
                if (record.dynamic) { RemovePin(record); records.Remove(record.id); return; }
                // This event confirms network destruction. Instance unloading does not raise it.
                record.collected = true; record.live = false; dirty = true;
                RefreshPins();
            }
            catch (Exception e) { host.Report("Destroyed-object tracking failed", e); }
        }

        private bool Visible(MarkerRecord record)
        {
            var entry = record.entry;
            if (!host.Enabled.Value || entry == null || !entry.Selected.Value) return false;
            if (record.shared && !host.OtherPlayerPins.Value) return false;
            if (host.SightDetection && !DiscoveryVisibility.ShowSight(!record.dynamic && (entry.KeepVisible?.Value ?? true),
                record.sightVisible && record.live, record.shared)) return false;
            if (!record.dynamic && !DiscoveryVisibility.Show(entry.KeepVisible?.Value ?? true,
                record.live, record.shared, host.DetectionDistance,
                Player.m_localPlayer != null ? Utils.DistanceXZ(record.position, Player.m_localPlayer.transform.position) : float.MaxValue)) return false;
            if (entry.creature)
            {
                if (record.shared)
                    return record.creatureRelation == 1 ? host.Hostile.Value : record.creatureRelation == 2 ? host.Friendly.Value :
                        record.creatureRelation == 3 ? host.Animals.Value : record.creatureRelation == 4 && host.Tamed.Value;
                Character ch = record.character;
                if (ch == null || ch.IsDead()) return false;
                if (host.DetectionDistance > 0 && Utils.DistanceXZ(ch.transform.position, Player.m_localPlayer.transform.position) > host.DetectionDistance) return false;
                if (ch.IsTamed()) return host.Tamed.Value;
                if (ch.GetBaseAI() is AnimalAI) return host.Animals.Value;
                return BaseAI.IsEnemy(ch, Player.m_localPlayer) ? host.Hostile.Value : host.Friendly.Value;
            }
            if (entry.dungeon) return !record.completed || host.ShowCompleted.Value;
            if (!record.collected) return true;
            if (entry.category == "Ore") return host.ShowMined.Value;
            return host.ShowPickedPlants.Value;
        }
        private string Name(MarkerRecord record)
        {
            string label = record.entry.Label;
            if (record.entry.category == "Portals" && !string.IsNullOrWhiteSpace(record.portalTag)) label += ": " + record.portalTag;
            if (record.collected) return label + " — " + Plugin.Text("Собрано", "Collected");
            return label;
        }
        internal List<MarkerRecord> OwnDiscoveries()
        {
            return records.Values.Where(r => !r.dynamic && !r.shared && r.entry != null && r.entry.Selected.Value).ToList();
        }
        internal List<MarkerRecord> OwnCreatures()
        {
            var result = new List<MarkerRecord>();
            foreach (var record in records.Values)
            {
                Character ch = record.character;
                if (!record.dynamic || record.shared || !record.entry.creature || ch == null || !Visible(record)) continue;
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
                    if (!existing.shared) continue;
                    existing.position = record.position; existing.creatureLevel = record.creatureLevel; existing.creatureRelation = record.creatureRelation;
                    existing.sourcePeer = sender; existing.expiresAt = Time.realtimeSinceStartup + 4;
                    if (existing.pin != null) existing.pin.m_pos = existing.position;
                }
                else
                {
                    record.entry = entry; record.shared = true; record.sourcePeer = sender; record.dynamic = true;
                    record.expiresAt = Time.realtimeSinceStartup + 4; records[record.id] = record;
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
            foreach (var record in incoming)
            {
                if (record == null || !ValidSharedRecord(record)) continue;
                CatalogEntry entry = host.Catalog.Entries.FirstOrDefault(e => e.key == record.type);
                if (entry == null && record.imported && record.type.StartsWith("legacy:", StringComparison.Ordinal))
                    entry = host.Catalog.GetLegacyDungeon(record.type.Substring("legacy:".Length));
                if (entry == null || entry.creature || entry.category == "Dropped items") continue;
                if (records.TryGetValue(record.id, out var existing) && !existing.shared) continue;
                if (existing != null) RemovePin(existing);
                record.entry = entry; record.shared = true; record.sourcePeer = sender;
                record.dynamic = false; records[record.id] = record; dirty = true;
            }
            RefreshPins();
        }
        private void RefreshPins()
        {
            if (map == null) return;
            foreach (var record in records.Values)
            {
                if (!Visible(record)) { RemovePin(record); continue; }
                string name = Name(record);
                if (record.pin != null && record.pin.m_name != name) RemovePin(record);
                if (record.pin == null)
                {
                    record.pin = map.AddPin(record.position, Minimap.PinType.Icon3, name, false, record.collected || record.completed);
                    if (!record.entry.creature && record.entry.Icon != null) record.pin.m_icon = record.entry.Icon;
                }
                record.pin.m_pos = record.position;
                record.pin.m_checked = record.collected || record.completed;
            }
            RequestPinRefresh();
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
            foreach (var record in records.Values)
            {
                var pin = record.pin;
                if (pin?.m_uiElement == null) continue;
                pin.m_uiElement.gameObject.SetActive(visible && host.Enabled.Value);
                if (pin.m_NamePinData?.PinNameGameObject != null)
                    pin.m_NamePinData.PinNameGameObject.SetActive(visible && host.Enabled.Value && !host.HideLargeMapNames.Value &&
                        map.m_mode == Minimap.MapMode.Large && MapLabelZoom.Visible(map.LargeZoom, map.m_minZoom, map.m_showNamesZoom));
                if (pin.m_iconElement != null)
                    pin.m_iconElement.color = record.collected || record.completed ? Color.gray :
                        record.visited ? new Color(0.45f, 0.75f, 1f) : record.entry.creature ?
                        (record.shared ? SharedCreatureColor(record.creatureRelation) : CreatureColor(record.character)) : Color.white;
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
                    record.creatureIcon.Update(pin.m_uiElement, sprite, pin.m_icon, size,
                        record.shared ? SharedCreatureColor(record.creatureRelation) : CreatureColor(record.character), showHead, host.ContrastCreatureIcons.Value);
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
            largeLabels.Apply(records.Values, map, host);
            EnforceNameVisibility();
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
        internal void KeepSharedPinsHidden()
        {
            ReleaseSharedMasks(false);
            foreach (var pair in hiddenSharedPins) pair.Value.Hide(pair.Key);
            EnforceNameVisibility();
            UpdateNearbyNames();
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
                if (record.nameVisibility == null) record.nameVisibility = new NativeNameVisibility();
                record.nameVisibility.Apply(record.pin.m_NamePinData?.PinNameGameObject,
                    namesAllowed && record.pin.m_uiElement != null && record.pin.m_uiElement.gameObject.activeInHierarchy);
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
                if (!record.entry.creature) continue;
                bool show = enabled && record.pin?.m_uiElement != null && record.pin.m_uiElement.gameObject.activeInHierarchy &&
                    CreatureDisplay.ShowNearbyName(NearbyNameDistance(record, player.transform.position), host.MinimapNameDistance.Value);
                if (!show) { record.nearbyName?.Hide(); continue; }
                if (record.nearbyName == null) record.nearbyName = new CreatureNearbyName();
                record.nearbyName.Show(record.pin.m_uiElement, map, Name(record) + (record.group != null ? " ×" + record.group.Count : ""));
                nearbyNames.Add(record);
            }
            if (nearbyNames.Count < 2) return;
            nearbyNames.Sort((a, b) =>
            {
                float da = NearbyNameDistance(a, player.transform.position), db = NearbyNameDistance(b, player.transform.position);
                int order = da.CompareTo(db); return order != 0 ? order : StringComparer.Ordinal.Compare(a.id, b.id);
            });
            var canvas = map.m_smallRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            foreach (var record in nearbyNames) nearbyNameBoxes.Add(record.nearbyName.Bounds(camera));
            LabelBox[] placed = nearbyPlacement.Place(nearbyNameBoxes, 3 * (canvas != null ? canvas.scaleFactor : 1));
            for (int i = 0; i < nearbyNames.Count; i++)
                nearbyNames[i].nearbyName.Move(camera, new Vector2(placed[i].X - nearbyNameBoxes[i].X, placed[i].Y - nearbyNameBoxes[i].Y));
        }
        private static float NearbyNameDistance(MarkerRecord record, Vector3 player)
        {
            float nearest = Utils.DistanceXZ(record.position, player);
            if (record.group != null)
                foreach (var member in record.group) nearest = Mathf.Min(nearest, Utils.DistanceXZ(member.position, player));
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
        internal bool MapClick(bool toggle = true)
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
            }
            catch (Exception e) { host.Report("Saved markers could not be loaded; original file retained", e); savePath = null; }
        }
        private void ResolveSavedDefinitions()
        {
            foreach (var record in records.Values)
            {
                var entry = host.Catalog.GetSavedDefinition(record.type);
                if (entry != null && entry != record.entry) { RemovePin(record); record.entry = entry; }
            }
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
            Save(); RemovePins(); records.Clear(); snapshot = null; pendingEntrance = null;
            grouping.Dispose(); creatureHover.Dispose();
            if (manager != null) manager.m_onZDODestroyed -= Destroyed;
            manager = null; loaded = false; dirty = false; map = null;
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
        private static bool Prefix() { return Plugin.Instance?.Tracking?.MapClick() ?? true; }
    }
    [HarmonyPatch(typeof(Minimap), "OnMapDblClick")]
    internal static class MarkerDoubleClickPatch
    {
        private static bool Prefix() { return Plugin.Instance?.Tracking?.MapClick(false) ?? true; }
    }
    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    internal static class MarkerRightClickPatch
    {
        private static bool Prefix() { return Plugin.Instance?.Tracking?.MapClick(false) ?? true; }
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
