using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace MapTracker
{
    [Serializable]
    public sealed class CatalogEntry
    {
        public string key, prefab, name, category, source;
        public string[] components, products;
        public bool creature, dungeon, renewable, playerPiece;
        public bool technical;
        public float respawnMinutes;
        [NonSerialized] internal ConfigEntry<bool> Selected;
        [NonSerialized] internal ConfigEntry<bool> KeepVisible;
        [NonSerialized] internal Sprite Icon;
        internal string Label
        {
            get
            {
                string dungeonName = dungeon ? DungeonNames.Get(prefab, false) : null;
                if (dungeonName != null) return Plugin.Text(DungeonNames.Get(prefab, true), dungeonName);
                string localized = Plugin.Localize(name);
                bool unresolved = localized == "[" + (name ?? "").TrimStart('$') + "]";
                return string.IsNullOrWhiteSpace(localized) || localized.StartsWith("$", StringComparison.Ordinal) || unresolved || localized == prefab
                    ? ReadableName(string.IsNullOrWhiteSpace(prefab) ? key : prefab) : localized;
            }
        }
        internal string FilterLabel => category == "Dropped items" ? Label + Plugin.Text(" (на земле)", " (dropped)") :
            category == "Plants and pickables" && components != null && components.Contains("Pickable") ?
            Label + (prefab != null && prefab.EndsWith("Bush", StringComparison.OrdinalIgnoreCase) ?
                Plugin.Text(" (куст)", " (bush)") : Plugin.Text(" (сбор)", " (pickable)")) : Label;
        private static string ReadableName(string value)
        {
            switch (value)
            {
                case "Crypt2": case "Crypt3": case "Crypt4": return Plugin.Text("Погребальные камеры", "Burial Chambers");
                case "SunkenCrypt4": return Plugin.Text("Затонувший склеп", "Sunken Crypt");
                case "TrollCave02": return Plugin.Text("Пещера тролля", "Troll Cave");
                case "MountainCave02": return Plugin.Text("Морозная пещера", "Frost Cave");
                case "Mistlands_DvergrTownEntrance1": case "Mistlands_DvergrTownEntrance2": return Plugin.Text("Заражённые шахты", "Infested Mines");
                case "Mistlands_DvergrBossEntrance1": return Plugin.Text("Цитадель Королевы", "Queen's Citadel");
                case "MorgenHole1": case "MorgenHole2": case "MorgenHole3": return Plugin.Text("Пещера моргена", "Morgen Cave");
                case "Hildir_cave": return Plugin.Text("Воющая пещера", "Howling Cavern");
                case "Hildir_crypt": return Plugin.Text("Тлеющая гробница", "Smouldering Tomb");
            }
            return Regex.Replace(Regex.Replace(Regex.Replace(value ?? "", "([a-z])([A-Z])", "$1 $2"), "([a-zA-Z])([0-9])", "$1 $2").Replace('_', ' '), @"\s+", " ").Trim();
        }
    }

    [Serializable]
    public sealed class CatalogDocument
    {
        public int schema = 1;
        public string modVersion, gameVersion, generatedUtc;
        public List<CatalogEntry> entries;
        public List<string> warnings;
    }

    internal sealed class Catalog
    {
        internal readonly List<CatalogEntry> Entries = new List<CatalogEntry>();
        internal readonly Dictionary<int, CatalogEntry> Prefabs = new Dictionary<int, CatalogEntry>();
        internal readonly Dictionary<int, CatalogEntry> Locations = new Dictionary<int, CatalogEntry>();
        private readonly Dictionary<string, CatalogEntry> liveTypes = new Dictionary<string, CatalogEntry>();
        private readonly Dictionary<string, CatalogEntry> cachedTypes = new Dictionary<string, CatalogEntry>();
        internal bool Ready { get; private set; }
        internal int Processed, Total;
        internal string Status = "Waiting for game registries";
        internal string ExportPath => Path.Combine(Paths.ConfigPath, "MapTracker", "catalog.json");
        private readonly Plugin host;
        private readonly List<string> warnings = new List<string>();
        private ZNetScene scene;
        private Coroutine scan;
        private readonly ConfigEntry<int> keepDefaultsRevision;
        private readonly ConfigEntry<int> plantKeepRevision;
        internal Catalog(Plugin plugin)
        {
            host = plugin;
            keepDefaultsRevision = host.Config.Bind("Internal", "Keep discovered defaults revision", 0,
                new ConfigDescription("One-time application of the portals-and-dungeons memory preset.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            plantKeepRevision = host.Config.Bind("Internal", "Plant keep defaults revision", 0,
                new ConfigDescription("One-time reset of Plants and pickables to nearby-only markers.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
        }

        internal void Tick()
        {
            if (ZNetScene.instance == null || ObjectDB.instance == null || ZoneSystem.instance == null) return;
            if (scene == ZNetScene.instance) return;
            if (scan != null) host.StopCoroutine(scan);
            scene = ZNetScene.instance;
            Ready = false;
            Entries.Clear(); Prefabs.Clear(); Locations.Clear(); warnings.Clear(); liveTypes.Clear(); cachedTypes.Clear();
            try
            {
                if (File.Exists(ExportPath))
                {
                    var cached = DataJson.Read<CatalogDocument>(File.ReadAllText(ExportPath, Encoding.UTF8));
                    if (cached?.schema == 1 && cached.entries != null)
                        foreach (var entry in cached.entries)
                            if (entry != null && !string.IsNullOrEmpty(entry.key) && !CatalogContent.IsServiceDefinition(entry)) cachedTypes[entry.key] = entry;
                }
            }
            catch (Exception e) { host.Report("Cached catalog could not be read; collecting fresh definitions", e); }
            scan = host.StartCoroutine(Scan(scene, ZoneSystem.instance));
        }

        private IEnumerator Scan(ZNetScene current, ZoneSystem world)
        {
            // Let other plugins finish registering their prefabs before taking a snapshot.
            yield return null; yield return null;
            var definitions = current.m_prefabs.Concat(current.m_nonNetViewPrefabs).Concat(ObjectDB.instance.m_items)
                .Where(p => p != null).GroupBy(p => p.name).Select(g => g.First()).ToList();
            var locations = world.m_locations.Where(l => l.m_enable && l.m_prefab.IsValid).ToList();
            Processed = 0; Total = definitions.Count + locations.Count;
            long frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (GameObject prefab in definitions)
            {
                if (current != ZNetScene.instance) yield break;
                try { Add(ReadPrefab(prefab), Prefabs, prefab); }
                catch (Exception e) { warnings.Add(prefab.name + ": " + e.Message); }
                Status = prefab.name;
                Processed++;
                if (!ScanTimeBudget.WithinBudget(frameStart, host.ScanBudget.Value))
                { yield return null; frameStart = System.Diagnostics.Stopwatch.GetTimestamp(); }
            }
            foreach (ZoneSystem.ZoneLocation location in locations)
            {
                if (current != ZNetScene.instance) yield break;
                Status = location.m_prefabName;
                var reference = location.m_prefab;
                bool started = false;
                try { reference.LoadAsync(); started = true; }
                catch (Exception e) { warnings.Add(Status + ": " + e.Message); }
                if (started)
                {
                    try
                    {
                        float deadline = Time.realtimeSinceStartup + 15f;
                        while (!reference.IsLoaded && Time.realtimeSinceStartup < deadline)
                        {
                            if (current != ZNetScene.instance) yield break;
                            yield return null;
                        }
                        if (reference.IsLoaded && reference.Asset != null) ReadLocationSafe(location, reference.Asset);
                        else warnings.Add(Status + ": definition loading timed out");
                    }
                    finally { reference.Release(); }
                }
                Processed++;
                yield return null;
            }
            if (host.PresetRevision.Value == 0) ApplyMinimalPreset();
            keepDefaultsRevision.Value = 1;
            plantKeepRevision.Value = 1;
            Ready = true; Status = "Ready"; scan = null;
            host.Config.Save();
            Export();
        }

        private void Add(CatalogEntry entry, Dictionary<int, CatalogEntry> index, GameObject prefab = null)
        {
            if (CatalogContent.IsServiceDefinition(entry)) return;
            BindSettings(entry);
            if (!entry.creature && entry.Icon == null) entry.Icon = ObjectIconResolver.Resolve(entry, prefab);
            Entries.Add(entry); index[entry.prefab.GetStableHashCode()] = entry; liveTypes[entry.key] = entry;
        }
        internal CatalogEntry GetSavedDefinition(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (liveTypes.TryGetValue(key, out var live)) return live;
            if (Ready || !cachedTypes.TryGetValue(key, out var cached)) return null;
            if (cached.Selected == null)
            {
                BindSettings(cached);
                var prefab = string.IsNullOrEmpty(cached.prefab) ? null : ZNetScene.instance?.GetPrefab(cached.prefab);
                cached.Icon = cached.creature ? CreatureIconResolver.Resolve(prefab, cached.prefab) :
                    prefab?.GetComponent<Piece>()?.m_icon ?? prefab?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
                if (!cached.creature && cached.Icon == null) cached.Icon = ObjectIconResolver.Resolve(cached, prefab);
            }
            return cached;
        }
        private void BindSettings(CatalogEntry entry)
        {
            ClassifyLocation(entry);
            // Individual types live in the searchable selector, rather than thousands of manager rows.
            bool autosave = host.Config.SaveOnConfigSet;
            host.Config.SaveOnConfigSet = false;
            try
            {
                string key = entry.key.Replace('=', '_').Replace('[', '_').Replace(']', '_');
                host.MapCategories.Register(entry.category);
                entry.Selected = host.Config.Bind("Types", key,
                    BuiltInFilterPresets.DefaultSelected(entry),
                    new ConfigDescription(entry.prefab, null, new ConfigurationManagerAttributes { Browsable = false }));
                if (!entry.creature && entry.category != "Dropped items")
                {
                    bool keep = DefaultSettings.Value("Keep discovered markers", key,
                        DiscoveryVisibility.KeepByDefault(entry.dungeon, entry.category));
                    entry.KeepVisible = host.Config.Bind("Keep discovered markers", key, keep,
                        new ConfigDescription("Keep this type visible after leaving the client-loaded detection area. Turning this off hides remembered markers outside that area without deleting discoveries.",
                            null, new ConfigurationManagerAttributes { Browsable = false }));
                    if (keepDefaultsRevision.Value == 0) entry.KeepVisible.Value = keep;
                    if (plantKeepRevision.Value == 0 && entry.category == "Plants and pickables") entry.KeepVisible.Value = false;
                }
            }
            finally { host.Config.SaveOnConfigSet = autosave; }
        }
        // Explicit location semantics override component-based detection, including
        // nested dungeon components in ruins. Preserve native keys and saved settings.
        internal static void ClassifyLocation(CatalogEntry entry)
        {
            if (entry?.key == "location:CharredFortress" && entry.prefab == "CharredFortress")
            { entry.category = "Dungeons"; entry.dungeon = true; }
            else if (entry?.key == "location:AshlandRuins" && entry.prefab == "AshlandRuins")
            { entry.category = "Locations"; entry.dungeon = false; }
        }
        internal void ApplyMinimalPreset()
        {
            bool autosave = host.Config.SaveOnConfigSet; host.Config.SaveOnConfigSet = false;
            try
            {
                foreach (var entry in Entries)
                {
                    entry.Selected.Value = BuiltInFilterPresets.DefaultSelected(entry);
                    if (entry.KeepVisible != null) entry.KeepVisible.Value = DefaultSettings.Value("Keep discovered markers", entry.KeepVisible.Definition.Key,
                        DiscoveryVisibility.KeepByDefault(entry.dungeon, entry.category));
                }
                host.Hostile.Value = true; host.Friendly.Value = false; host.Animals.Value = false; host.Tamed.Value = false;
                host.PresetRevision.Value = 1;
            }
            finally { host.Config.SaveOnConfigSet = autosave; }
            host.Config.Save();
        }
        internal CatalogEntry GetLegacyDungeon(string name)
        {
            string key = "legacy:" + name;
            var entry = Entries.FirstOrDefault(e => e.key == key);
            if (entry != null) return entry;
            entry = new CatalogEntry { key = key, prefab = name, name = name, category = "Dungeons", dungeon = true,
                source = "AutoMapPins saved map pin", products = new string[0], components = new string[0] };
            Add(entry, new Dictionary<int, CatalogEntry>());
            return entry;
        }

        private static string ItemName(GameObject go)
        {
            return go?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name ?? go?.name ?? "";
        }
        private static string[] Products(DropTable table)
        {
            return table?.m_drops?.Where(d => d.m_item != null).Select(d => d.m_item.name).Distinct().ToArray() ?? new string[0];
        }
        private static CatalogEntry ReadPrefab(GameObject go)
        {
            var e = new CatalogEntry { key = "prefab:" + go.name, prefab = go.name, name = go.name,
                category = "Other", source = "ZNetScene/ObjectDB", products = new string[0],
                components = go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray() };
            Character ch = go.GetComponent<Character>();
            Piece piece = go.GetComponent<Piece>();
            Pickable plant = go.GetComponent<Pickable>();
            MineRock rock = go.GetComponent<MineRock>();
            MineRock5 rock5 = go.GetComponent<MineRock5>();
            Container box = go.GetComponent<Container>();
            ItemDrop item = go.GetComponent<ItemDrop>();
            if (ch != null) { e.category = "Creatures"; e.creature = true; e.name = ch.m_name; }
            else if (rock != null) { e.category = "Ore"; e.name = rock.m_name; e.products = Products(rock.m_dropItems); }
            else if (rock5 != null) { e.category = "Ore"; e.name = rock5.m_name; e.products = Products(rock5.m_dropItems); }
            else if (plant != null)
            {
                e.category = "Plants and pickables";
                e.name = string.IsNullOrEmpty(plant.m_overrideName) ? ItemName(plant.m_itemPrefab) : plant.m_overrideName;
                e.products = plant.m_itemPrefab == null ? new string[0] : new[] { plant.m_itemPrefab.name };
                e.respawnMinutes = plant.m_respawnTimeMinutes; e.renewable = e.respawnMinutes > 0;
            }
            else if (box != null) { e.category = "Containers"; e.name = box.m_name; e.products = Products(box.m_defaultItems); }
            else if (item != null) { e.category = "Dropped items"; e.name = ItemName(go); e.Icon = item.m_itemData.GetIcon(); }
            // Plant/build mods attach Piece to resources and even loose items.
            // Classify by the object's harvest/loot behavior before buildability.
            else if (piece != null) { e.category = "Player structures"; e.playerPiece = true; e.name = piece.m_name; e.Icon = piece.m_icon; }
            else if (go.GetComponent<TreeBase>() != null || go.GetComponent<TreeLog>() != null) e.category = "Trees";
            else if (go.GetComponent<Destructible>() != null) e.category = "Destructibles";
            if (go.name == "dvergrprops_crate_long") { e.category = "Dvergr loot"; e.name = "$item_dvergrneedle"; e.products = new[] { "DvergrNeedle" }; }
            if (go.name == "DvergrNeedle" && item != null) e.category = "Dvergr loot";
            if (go.name == "Beehive" && piece == null) { e.category = "Wild beehives"; e.name = "$item_queenbee"; }
            if (go.GetComponent<SapCollector>() is SapCollector sap) { e.category = "Sap collectors"; e.name = sap.m_name; }
            if (go.GetComponent<TeleportWorld>() != null) e.category = "Portals";
            if (go.name.IndexOf("YggaRoot", StringComparison.OrdinalIgnoreCase) >= 0) e.category = "Sap roots";
            if (e.creature)
            {
                e.Icon = CreatureIconResolver.Resolve(go, e.prefab);
            }
            if (e.category == "Ore" && (string.IsNullOrWhiteSpace(e.name) || e.name == go.name || e.name == "Rock" || e.name == "$piece_rock"))
            {
                string resource = e.products.FirstOrDefault(p => p != "Stone") ?? e.products.FirstOrDefault();
                if (resource != null)
                {
                    var resourcePrefab = ObjectDB.instance.GetItemPrefab(resource);
                    e.name = ItemName(resourcePrefab);
                    e.Icon = resourcePrefab?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
                }
            }
            if ((e.name == go.name || string.IsNullOrWhiteSpace(e.name)) && go.GetComponent<Hoverable>() is Hoverable hover)
            {
                try { string hoverName = hover.GetHoverName(); if (!string.IsNullOrWhiteSpace(hoverName)) e.name = hoverName; }
                catch { /* Some prefab hover methods require an instantiated object. */ }
            }
            e.technical = e.category == "Other" && (e.components.Any(c => c == "Projectile" || c == "Aoe" || c == "TimedDestruction" || c == "ParticleSystem" || c == "ZSFX") ||
                go.name.StartsWith("sfx_", StringComparison.OrdinalIgnoreCase) || go.name.StartsWith("vfx_", StringComparison.OrdinalIgnoreCase) || go.name.StartsWith("fx_", StringComparison.OrdinalIgnoreCase));
            if (e.Icon == null && e.products.Length > 0)
            {
                var output = ObjectDB.instance.GetItemPrefab(e.products[0])?.GetComponent<ItemDrop>();
                if (output != null) e.Icon = output.m_itemData.GetIcon();
            }
            return e;
        }

        private void ReadLocationSafe(ZoneSystem.ZoneLocation location, GameObject go)
        {
            try
            {
                Location loc = go.GetComponent<Location>();
                Teleport entrance = go.GetComponentsInChildren<Teleport>(true).FirstOrDefault();
                var entry = new CatalogEntry { key = "location:" + location.m_prefabName, prefab = location.m_prefabName,
                    name = loc != null && !string.IsNullOrEmpty(loc.m_discoverLabel) ? loc.m_discoverLabel : location.m_prefabName,
                    source = "ZoneSystem", category = "Locations", products = new string[0],
                    components = new[] { "Location" } };
                entry.dungeon = entrance != null || go.GetComponentInChildren<DungeonGenerator>(true) != null;
                if (entry.dungeon)
                {
                    entry.category = "Dungeons";
                    if (entrance != null && !string.IsNullOrEmpty(entrance.m_enterText)) entry.name = entrance.m_enterText;
                }
                Add(entry, Locations);
            }
            catch (Exception e) { warnings.Add(location.m_prefabName + ": " + e.Message); }
        }

        internal void Export()
        {
            if (!Ready) { host.Report("Catalog is still being collected; export deferred until complete."); return; }
            try
            {
                var doc = new CatalogDocument { modVersion = Plugin.Version, gameVersion = global::Version.GetVersionString(),
                    generatedUtc = DateTime.UtcNow.ToString("o"), entries = Entries, warnings = warnings };
                Directory.CreateDirectory(Path.GetDirectoryName(ExportPath));
                string temp = ExportPath + ".tmp";
                string json = DataJson.Write(doc, true);
                if (json.IndexOf("\"entries\"", StringComparison.Ordinal) < 0) throw new InvalidOperationException("Serializer omitted the catalog entries.");
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(ExportPath)) File.Replace(temp, ExportPath, ExportPath + ".bak");
                else File.Move(temp, ExportPath);
            }
            catch (Exception e) { host.Report("Catalog export failed", e); }
        }
    }
}
