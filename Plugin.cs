using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MapTracker
{
    [BepInPlugin(Guid, "MapTracker", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "Slikfoul.MapTracker";
        public const string Version = "0.1.23";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled, SmallMap, LargeMap, OtherPlayerPins;
        internal ConfigEntry<bool> Hostile, Friendly, Animals, Tamed;
        internal ConfigEntry<bool> ShowPickedPlants, ShowMined, ShowCompleted;
        internal ConfigEntry<bool> HideMinimapNames, HideLargeMapNames, CreatureIcons, MinimapCreatureIcons, ContrastCreatureIcons;
        internal ConfigEntry<int> PresetRevision;
        internal ConfigEntry<bool> GroupMarkers, GroupCreatures, GroupPlants;
        internal ConfigEntry<bool> ShareMarkers;
        internal ConfigEntry<int> GroupThreshold, GroupDistance;
        internal ConfigEntry<int> MinimapCreatureIconSize, LargeMapCreatureIconSize;
        internal ConfigEntry<int> MinimapNameDistance;
        internal ConfigEntry<int> DetectionRadius;
        internal float DetectionDistance => RadiusSetting.Distance(DetectionRadius.Value);
        internal bool SightDetection => DetectionRadius.Value == RadiusSetting.LineOfSight;
        internal Catalog Catalog;
        internal Tracking Tracking;
        internal FilterWindow Window;
        private MarkerSharing sharing;
        private Harmony harmony;
        private float nextError;

        private void Awake()
        {
            Instance = this;
            Enabled = Bind("General", "Enabled", true, "Включить мод", "Enable map markers.");
            SmallMap = Bind("Maps", "Minimap", true, "Показывать на мини-карте", "Show on the minimap.");
            LargeMap = Bind("Maps", "Large map", true, "Показывать на большой карте", "Show MapTracker markers on the large map.");
            HideMinimapNames = Bind("Names", "Hide minimap names", false, "Скрывать подписи на мини-карте", "Hide MapTracker creature names on the minimap. When off, names show within Minimap name distance; a group uses its nearest member. Icons, stars and counts remain visible.");
            MinimapNameDistance = Config.Bind("Names", "Minimap name distance", 60, new ConfigDescription("Maximum distance from the character for creature names on the minimap, in whole meters. Separate from detection radius: distant creature markers retain icons or dots without names. Applies when Hide minimap names is off.", new AcceptableValueRange<int>(5, 200), new ConfigurationManagerAttributes { DispName = "Дальность подписей существ на мини-карте, м" }));
            HideLargeMapNames = Bind("Names", "Hide large map names", false, "Скрывать подписи на большой карте", "Hide persistent MapTracker names on the large map. Creature names remain available on hover and group details on click. Does not affect native/manual pins.");
            CreatureIcons = Bind("Icons", "Creature icons", true, "Показывать существ иконками на большой карте", "Use creature trophy icons on the large map at close zoom. Turn off to use relationship-colored dots; stars and group counts remain visible.");
            MinimapCreatureIcons = ConfigSections.CopyIfMissing(Config, CreatureIcons, "Minimap creature icons", true,
                new ConfigDescription("Use creature trophy icons on the minimap independently of the large map. Turn off to use relationship-colored dots; names, stars and group counts retain their own settings.", null,
                    new ConfigurationManagerAttributes { DispName = "Показывать существ иконками на мини-карте" }));
            ContrastCreatureIcons = Bind("Icons", "Contrast creature icons", true, "Контрастные значки существ", "Outline creature icons along their transparent silhouette on both maps, with a dark outer edge and a lighter inner edge. No circular background or clipping. Turn off for plain heads. Relationship-colored dots and stars remain. Applies when Creature icons is enabled.");
            MinimapCreatureIconSize = Config.Bind("Icons", "Minimap creature icon size", 26, new ConfigDescription("Creature icon size on the minimap, in UI pixels. Applies to icon mode; colored dot sizes remain unchanged.", new AcceptableValueRange<int>(12, 48), new ConfigurationManagerAttributes { DispName = "Размер иконок существ на мини-карте" }));
            LargeMapCreatureIconSize = Config.Bind("Icons", "Large map creature icon size", 30, new ConfigDescription("Creature icon size on the large map, in UI pixels. Applies at icon-visible zoom; distant colored dots remain unchanged.", new AcceptableValueRange<int>(12, 64), new ConfigurationManagerAttributes { DispName = "Размер иконок существ на большой карте" }));
            OtherPlayerPins = Bind("Sharing", "Other player pins", true, "Показывать метки других игроков", "Show pins from the cartography table and discoveries received from other MapTracker players. Does not control live player positions or your own pins.");
            ShareMarkers = Bind("Sharing", "Share discoveries", false, "Делиться метками с другими игроками", "Share static discoveries and live creature positions with other MapTracker players on this server. Received data is not forwarded. No server plugin is required.");
            DetectionRadius = RadiusSetting.Bind(Config, DrawRadius);
            GroupMarkers = Bind("Grouping", "Group markers", true, "Группировать близкие метки", "Combine overlapping markers of the same type; groups split when zooming in.");
            GroupPlants = Bind("Grouping", "Group plants", false, "Группировать растения", "Group plants, pickables and wild beehives when general marker grouping is enabled. Off keeps each marker at its actual location. Saved in custom presets; Gathering turns it off.");
            GroupCreatures = Bind("Grouping", "Group creatures", true, "Группировать существ", "Allow creature markers to group when general marker grouping is enabled. Turn off to keep creatures separate while portals and other objects still group.");
            GroupThreshold = ConfigSections.Bind(Config, "General", "Grouping", "Group threshold", 3, new ConfigDescription("Minimum number of nearby markers to form a group.", new AcceptableValueRange<int>(2, 10), new ConfigurationManagerAttributes { DispName = "Минимум меток в группе" }));
            GroupDistance = ConfigSections.Bind(Config, "General", "Grouping", "Group distance", 36, new ConfigDescription("Maximum screen distance between neighboring markers to group, in UI pixels. Zooming out covers more world space; chains of neighbors may span a wider area. Turn off Group plants for exact plant locations.", new AcceptableValueRange<int>(8, 64), new ConfigurationManagerAttributes { DispName = "Расстояние группировки (пиксели)" }));
            Hostile = Bind("Creatures", "Hostile", true, "Враждебные существа", "Creatures hostile to your character.");
            Friendly = Bind("Creatures", "Friendly", false, "Дружественные существа", "Friendly creatures, including calm Dvergr.");
            Animals = Bind("Creatures", "Animals", false, "Нейтральные животные", "Non-aggressive wildlife.");
            Tamed = Bind("Creatures", "Tamed", false, "Приручённые существа", "Tamed creatures.");
            ShowPickedPlants = Bind("Completed", "Plants", false, "Показывать собранное: растения и добыча", "Show collected plants and loot with a completed mark.");
            ShowMined = Bind("Completed", "Ore", false, "Показывать собранное: руда", "Show depleted deposits with a completed mark.");
            ShowCompleted = Bind("Completed", "Dungeons", true, "Показывать пройденные подземелья", "Show manually completed dungeon entrances.");
            Catalog = new Catalog(this);
            Window = new FilterWindow(this);
            Tracking = new Tracking(this);
            OtherPlayerPins.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            Enabled.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            HideMinimapNames.SettingChanged += (_, __) => { Tracking.RequestPinRefresh(); Tracking.ResetNameDiagnostics(); };
            MinimapNameDistance.SettingChanged += (_, __) => { Tracking.RequestPinRefresh(); Tracking.ResetNameDiagnostics(); };
            HideLargeMapNames.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            CreatureIcons.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            MinimapCreatureIcons.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            ContrastCreatureIcons.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            MinimapCreatureIconSize.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            LargeMapCreatureIconSize.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            GroupCreatures.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            GroupPlants.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            GroupDistance.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            DetectionRadius.SettingChanged += (_, __) => Tracking.DetectionChanged();
            sharing = new MarkerSharing(this);
            PresetRevision = Config.Bind("Internal", "Minimal preset revision", 0,
                new ConfigDescription("Migration revision for the initial minimal filter set.", null, new ConfigurationManagerAttributes { Browsable = false }));
            ConfigSections.Bind(Config, "General", "Tools", "Open filters", false, new ConfigDescription("Open the searchable category and type selector.", null,
                new ConfigurationManagerAttributes { DispName = "Фильтры", CustomDrawer = entry =>
                { if (GUILayout.Button(Text("Открыть фильтры", "Open filters"))) Window.Toggle(); } }));
            ConfigSections.Bind(Config, "General", "Tools", "Export catalog", false, new ConfigDescription("Export the game-derived catalog to BepInEx/config/MapTracker/catalog.json.", null,
                new ConfigurationManagerAttributes { DispName = "Выгрузка каталога", CustomDrawer = entry =>
                { if (GUILayout.Button(Text("Выгрузить каталог", "Export catalog"))) Catalog.Export(); } }));
            harmony = new Harmony(Guid);
            harmony.PatchAll();
            new Terminal.ConsoleCommand("maptracker", "Open MapTracker filters", args =>
            {
                try
                {
                    if (!Catalog.Ready || Player.m_localPlayer == null)
                    { global::Console.instance?.Print(Text("Сначала загрузи мир и дождись каталога.", "Load a world and wait for the catalog first.")); return; }
                    Window.Toggle();
                }
                catch (Exception e) { Report("MapTracker command failed", e); global::Console.instance?.Print(Text("Команда не выполнена; подробности в журнале.", "Command failed; see the log.")); }
            });
        }

        internal ConfigEntry<T> Bind<T>(string section, string key, T value, string ru, string description)
        {
            return ConfigSections.Bind(Config, ConfigSections.PreviousSection(section, key), section, key, value, new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { DispName = ru }));
        }
        private static void DrawRadius(ConfigEntryBase entry)
        {
            var radius = (ConfigEntry<int>)entry;
            GUILayout.BeginHorizontal();
            int selected = Mathf.RoundToInt(GUILayout.HorizontalSlider(radius.Value, RadiusSetting.Minimum, RadiusSetting.Unlimited));
            if (radius.Value != selected) radius.Value = selected;
            GUILayout.Label(selected == RadiusSetting.LineOfSight ? Text("В поле зрения", "Line of sight") :
                selected == RadiusSetting.Unlimited ? Text("∞ — максимум клиента", "∞ — client maximum") :
                selected + Text(" м", " m"), GUILayout.Width(180));
            GUILayout.EndHorizontal();
        }

        internal static string Text(string ru, string en)
        {
            return Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Russian" ? ru : en;
        }

        internal static string Localize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return Localization.instance != null ? Localization.instance.Localize(value) : value;
        }

        private void Update()
        {
            try
            {
                Catalog.Tick();
                Window.Tick();
                Tracking.Tick();
                sharing.Tick();
            }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup >= nextError)
                {
                    Logger.LogError("MapTracker update failed: " + e);
                    nextError = Time.realtimeSinceStartup + 15f;
                }
            }
        }

        internal void Report(string message, Exception e = null) { Logger.LogWarning(message + (e == null ? "" : ": " + e)); }
        internal void NameDiagnostic(string message) { Logger.LogInfo("Minimap name diagnostic: " + message); }
        private void LateUpdate()
        {
            try { Tracking?.KeepSharedPinsHidden(); }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup >= nextError)
                { Report("Shared pin visibility update failed", e); nextError = Time.realtimeSinceStartup + 15f; }
            }
        }
        private void OnDestroy()
        {
            Window?.Dispose();
            Tracking?.Dispose();
            CustomCreatureIcons.Dispose();
            harmony?.UnpatchSelf();
            Instance = null;
        }
    }

    internal sealed class ConfigurationManagerAttributes
    {
        public string DispName;
        public bool? Browsable;
        public Action<ConfigEntryBase> CustomDrawer;
    }
}
