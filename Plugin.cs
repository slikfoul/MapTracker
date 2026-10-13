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
        public const string Version = "0.11.0";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled, SmallMap, LargeMap, OtherPlayerPins;
        internal ConfigEntry<bool> Hostile, Friendly, Animals, Tamed;
        internal ConfigEntry<bool> ShowPickedPlants, ShowMined, ShowCompleted;
        internal ConfigEntry<bool> HideMinimapNames, HideLargeMapNames, CreatureIcons, MinimapCreatureIcons, ContrastCreatureIcons;
        internal ConfigEntry<int> PresetRevision;
        internal ConfigEntry<bool> GroupMarkers, GroupCreatures, GroupPlants;
        internal ConfigEntry<bool> ShareMarkers;
        internal ConfigEntry<bool> ShareCreatures;
        internal ConfigEntry<int> GroupThreshold, GroupDistance;
        internal ConfigEntry<int> MinimapCreatureIconSize, LargeMapCreatureIconSize;
        internal ConfigEntry<int> MinimapNameDistance, CreatureOutlineThickness;
        internal ConfigEntry<int> CreatureNameSize, ObjectNameSize;
        internal ConfigEntry<bool> GroupCreatureNames;
        internal ConfigEntry<int> CreatureNameGroupRadius;
        internal ConfigEntry<int> DetectionRadius;
        internal ConfigEntry<int> ScanBudget, NameMargin;
        internal ConfigEntry<bool> NotifyDungeons;
        internal ConfigEntry<string> NotifyTypes;
        internal ConfigEntry<KeyboardShortcut> CancelNavigationKey;
        internal ConfigEntry<KeyboardShortcut> SelectNavigationKey;
        internal ConfigEntry<bool> ShowNavigationArrow;
        internal MapCategoryVisibility MapCategories;
        internal PresetCycler Presets;
        internal float DetectionDistance => RadiusSetting.Distance(DetectionRadius.Value);
        internal bool SightDetection => DetectionRadius.Value == RadiusSetting.LineOfSight;
        internal Catalog Catalog;
        internal Tracking Tracking;
        internal FilterWindow Window;
        internal MarkerSharing sharing;
        private Harmony harmony;
        private float nextError;

        private void Awake()
        {
            Instance = this;
            Enabled = Bind("General", "Enabled", true, "Включить мод", "Enable map markers.");
            SmallMap = Bind("Minimap", "Minimap", true, "Показывать на мини-карте", "Show MapTracker markers on the minimap.");
            LargeMap = Bind("Large map", "Large map", true, "Показывать на большой карте", "Show MapTracker markers on the large map.");
            HideMinimapNames = Bind("Minimap", "Hide minimap names", false, "Скрывать подписи на мини-карте", "Hide MapTracker creature names on the minimap. When off, names show within Minimap name distance; a group uses its nearest member. Icons, stars and counts remain visible.");
            MinimapNameDistance = ConfigSections.Bind(Config, "Names", "Minimap", "Minimap name distance", DefaultSettings.Value("Minimap", "Minimap name distance", 60), new ConfigDescription("Maximum distance from the character for creature names on the minimap, in whole meters. Separate from detection radius: distant creature markers retain icons or dots without names. Applies when Hide minimap names is off.", new AcceptableValueRange<int>(5, 200), new ConfigurationManagerAttributes { DispName = "Minimap name distance (m)" }));
            CreatureNameSize = Config.Bind("Names", "Creature name size", 14, new ConfigDescription("Creature name font size on both maps and in hover labels. Larger text automatically leaves less room for other names.", new AcceptableValueRange<int>(8, 28)));
            ObjectNameSize = Config.Bind("Names", "Object name size", 18, new ConfigDescription("Font size for portal, dungeon and other object names, including hover labels. Does not change native/manual pin names or navigation HUD text.", new AcceptableValueRange<int>(8, 28)));
            GroupCreatureNames = Config.Bind("Names", "Group creature names", true, "Combine nearby names of the same creature type into one centered name and count. Enabled by default. Marker positions and icon grouping are unchanged. The selected navigation target keeps its individual name.");
            CreatureNameGroupRadius = Config.Bind("Names", "Creature name group radius", 15, new ConfigDescription("Nearby creatures of the same type share a centered name within this world-distance radius, default 15 meters. Applies when Group creature names is enabled; separate from discovery radius and icon grouping.", new AcceptableValueRange<int>(1, 100), new ConfigurationManagerAttributes { DispName = "Creature name group radius (m)" }));
            HideLargeMapNames = Bind("Large map", "Hide large map names", false, "Скрывать подписи на большой карте", "Hide persistent MapTracker names on the large map. Creature names remain available on hover and group details on click. Does not affect native/manual pins.");
            CreatureIcons = Bind("Large map", "Creature icons", true, "Показывать существ иконками на большой карте", "Use creature trophy icons on the large map at close zoom. Turn off to use relationship-colored dots; stars and group counts remain visible.");
            MinimapCreatureIcons = ConfigSections.Bind(Config, "Icons", "Minimap", "Minimap creature icons", DefaultSettings.Value("Minimap", "Minimap creature icons", true),
                new ConfigDescription("Use creature trophy icons on the minimap independently of the large map. Turn off to use relationship-colored dots; names, stars and group counts retain their own settings.", null,
                    new ConfigurationManagerAttributes { DispName = "Creature icons on minimap" }));
            ContrastCreatureIcons = Bind("Icons", "Contrast creature icons", true, "Контрастные значки существ", "Add a solid black outline along each creature icon's transparent silhouette on both maps. No circular background, colored copies or clipping. Turn off for plain heads. Stars remain; relationship colors apply in dot mode. Applies when creature icons are enabled.");
            CreatureOutlineThickness = Config.Bind("Icons", "Creature outline thickness", 1, new ConfigDescription("Thickness of the solid black outline along the icon silhouette, in whole UI pixels, on both maps. Requires Contrast creature icons and icon mode. The head itself keeps its chosen size.", new AcceptableValueRange<int>(1, 4)));
            MinimapCreatureIconSize = ConfigSections.Bind(Config, "Icons", "Minimap", "Minimap creature icon size", DefaultSettings.Value("Minimap", "Minimap creature icon size", 26), new ConfigDescription("Creature icon size on the minimap, in UI pixels. Applies to icon mode; colored dot sizes remain unchanged.", new AcceptableValueRange<int>(12, 48), new ConfigurationManagerAttributes { DispName = "Minimap creature icon size" }));
            LargeMapCreatureIconSize = ConfigSections.Bind(Config, "Icons", "Large map", "Large map creature icon size", DefaultSettings.Value("Large map", "Large map creature icon size", 30), new ConfigDescription("Creature icon size on the large map, in UI pixels. Applies at icon-visible zoom; distant colored dots remain unchanged.", new AcceptableValueRange<int>(12, 64), new ConfigurationManagerAttributes { DispName = "Large map creature icon size" }));
            OtherPlayerPins = Bind("Sharing", "Other player pins", true, "Показывать метки других игроков", "Show pins from the cartography table and discoveries received from other MapTracker players. Does not control live player positions or your own pins.");
            ShareMarkers = Bind("Sharing", "Share discoveries", false, "Делиться метками с другими игроками", "Share static discoveries with other MapTracker players on this server. Share creature positions separately enables automatic live creature transmission. Discoveries merge without duplicate markers and follow each recipient's Show object and Keep discovered filters. Fresh ally observations can appear outside your detection radius. Alt + middle-click shares a separate blue navigation destination while preserving personal navigation. Received data is not forwarded. No server plugin is required.");
            ShareCreatures = Config.Bind("Sharing", "Share creature positions", false, "Automatically transmit detected creature positions, levels and relationships to other MapTracker users. Disabled by default. Requires Share discoveries. Turn off to share static discoveries without live creatures. Received positions still follow Other player pins and your creature display filters. Explicit shared navigation targets use their separate map gesture.");
            DetectionRadius = RadiusSetting.Bind(Config, DrawRadius);
            GroupMarkers = Bind("Grouping", "Group markers", true, "Группировать близкие метки", "Combine overlapping markers of the same type; groups split when zooming in.");
            GroupPlants = Bind("Grouping", "Group plants", false, "Группировать растения", "Group plants, pickables and wild beehives when general marker grouping is enabled. Off keeps each marker at its actual location. Filter presets do not change this setting.");
            GroupCreatures = Bind("Grouping", "Group creatures", true, "Группировать существ", "Allow creature markers to group when general marker grouping is enabled. Turn off to keep creatures separate while portals and other objects still group.");
            GroupThreshold = ConfigSections.Bind(Config, "General", "Grouping", "Group threshold", DefaultSettings.Value("Grouping", "Group threshold", 3), new ConfigDescription("Minimum number of nearby markers to form a group.", new AcceptableValueRange<int>(2, 10), new ConfigurationManagerAttributes { DispName = "Minimum markers per group" }));
            GroupDistance = ConfigSections.Bind(Config, "General", "Grouping", "Group distance", DefaultSettings.Value("Grouping", "Group distance", 400), new ConfigDescription("Maximum distance between the furthest members of a group in world meters. Default: 400; maximum: 800. Markers only group when their displayed icons overlap or nearly touch. Zooming in splits groups as soon as the icons have room. This limit never forces separated screen markers into a group. Click a group to reveal exact member locations.", new AcceptableValueRange<int>(8, 800), new ConfigurationManagerAttributes { DispName = "Maximum group span (m)" }));
            Hostile = Bind("Creatures", "Hostile", true, "Враждебные существа", "Creatures hostile to your character.");
            Friendly = Bind("Creatures", "Friendly", false, "Дружественные существа", "Friendly creatures, including calm Dvergr.");
            Animals = Bind("Creatures", "Animals", false, "Нейтральные животные", "Non-aggressive wildlife.");
            Tamed = Bind("Creatures", "Tamed", false, "Приручённые существа", "Tamed creatures.");
            ShowPickedPlants = Bind("Completed", "Plants", false, "Показывать собранное: растения и добыча", "Show collected plants and loot with a completed mark.");
            ShowMined = Bind("Completed", "Ore", false, "Показывать собранное: руда", "Show depleted deposits with a completed mark.");
            ShowCompleted = Bind("Completed", "Dungeons", true, "Показывать пройденные подземелья", "Show manually completed dungeon entrances.");
            ScanBudget = Config.Bind("Performance", "Scan budget ms", 2, new ConfigDescription("Maximum scan work per frame in milliseconds. A single object inspection may exceed this budget.", new AcceptableValueRange<int>(1, 8)));
            NameMargin = Config.Bind("Names", "Name distance hysteresis meters", 5, new ConfigDescription("Names appear inside their distance limit and disappear this many meters beyond it, reducing flicker.", new AcceptableValueRange<int>(0, 30)));
            NotifyDungeons = Config.Bind("Notifications", "New dungeons", false, "Notify once when a new local dungeon entrance is actually detected. Previously saved entrances do not notify.");
            NotifyTypes = Config.Bind("Notifications", "Object type keys", "", "Comma-separated catalog keys for discovery notifications. Use the Notify checkbox in the map filter window to add or remove a type.");
            SelectNavigationKey = Config.Bind("Navigation", "Select navigation key", new KeyboardShortcut(KeyCode.Mouse0, KeyCode.LeftAlt), "Select an object, public player or temporary destination under the pointer on the large map. Default: Alt + left-click (either Alt key). Press again on the selected target to cancel, or right-click it. Mouse bindings replace that button's normal map action; keyboard bindings work while pointing at the map. Alt + middle-click is reserved for sending a navigation target when Share discoveries is enabled. Disabled while typing or using menus.");
            CancelNavigationKey = Config.Bind("Navigation", "Cancel navigation key", new KeyboardShortcut(KeyCode.F10), "Cancel both personal and shared navigation destinations. Right-click a map target to cancel only that target. Automatic arrival stops navigation within 5 meters. Disabled while typing or using menus.");
            ShowNavigationArrow = Config.Bind("Navigation", "Show navigation arrow", true, "Show direction arrows, destination names and distances at the top of the gameplay screen. Personal navigation is gold; shared navigation is blue, side by side when both exist. The arrow turns relative to the camera: up is ahead and down is behind. Hides while the large map or menus are open; turning this off keeps the map destination and navigation active.");
            MapCategories = new MapCategoryVisibility(Config);
            Catalog = new Catalog(this);
            Window = new FilterWindow(this);
            Tracking = new Tracking(this);
            Presets = new PresetCycler(this);
            Config.SettingChanged += (_, __) => Tracking.SettingsChanged();
            OtherPlayerPins.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            Enabled.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            HideMinimapNames.SettingChanged += (_, __) => { Tracking.RequestPinRefresh(); Tracking.ResetNameDiagnostics(); };
            MinimapNameDistance.SettingChanged += (_, __) => { Tracking.RequestPinRefresh(); Tracking.ResetNameDiagnostics(); };
            CreatureNameSize.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            ObjectNameSize.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            HideLargeMapNames.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            CreatureIcons.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            MinimapCreatureIcons.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            ContrastCreatureIcons.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
            CreatureOutlineThickness.SettingChanged += (_, __) => Tracking.RequestPinRefresh();
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
                new ConfigurationManagerAttributes { DispName = "Filters", CustomDrawer = entry =>
                { if (GUILayout.Button(Text("Открыть фильтры", "Open filters"))) Window.Toggle(); } }));
            ConfigSections.Bind(Config, "General", "Tools", "Export catalog", false, new ConfigDescription("Export the game-derived catalog to BepInEx/config/MapTracker/catalog.json.", null,
                new ConfigurationManagerAttributes { DispName = "Export catalog", CustomDrawer = entry =>
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
            return DefaultSettings.Bind(Config, ConfigSections.PreviousSection(section, key), section, key, value, new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { DispName = key }));
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
            return en;
        }

        internal static string Localize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return EnglishLocalization.Translate(value);
        }

        private void Update()
        {
            try
            {
                Catalog.Tick();
                Window.Tick();
                Tracking.Tick();
                Tracking.HandleNavigationKey();
                Tracking.Discoveries.HandleCancelKey();
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
            CreatureNearbyName.ClearPool();
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
