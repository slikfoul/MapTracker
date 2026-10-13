using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;

namespace MapTracker
{
    public static class PresetDisplay
    {
        private static bool Allowed(string section) => section == "Maps" || section == "Names" || section == "Icons" ||
            section == "Grouping" || section == "Map categories" || section == "Detection" || section == "Minimap" || section == "Large map";
        private static bool Removed(string section, string key) => (section == "Grouping" &&
            (key == "Maximum group diameter pixels" || key == "Maximum group diameter meters")) ||
            (section == "Names" && key == "Large map name distance");
        public static Dictionary<string, Dictionary<string, string>> Capture(ConfigFile config)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            foreach (var pair in config)
            {
                if (!Allowed(pair.Key.Section) || Removed(pair.Key.Section, pair.Key.Key) || (pair.Value.SettingType != typeof(bool) && pair.Value.SettingType != typeof(int))) continue;
                if (!result.TryGetValue(pair.Key.Section, out var values)) result.Add(pair.Key.Section, values = new Dictionary<string, string>());
                values.Add(pair.Key.Key, pair.Value.GetSerializedValue());
            }
            return result;
        }
        public static void Apply(ConfigFile config, Dictionary<string, Dictionary<string, string>> display)
        {
            if (display == null) return; // Legacy presets retain current presentation.
            var changes = new List<KeyValuePair<ConfigEntryBase, string>>();
            foreach (var section in display)
            {
                if (!Allowed(section.Key) || section.Value == null) continue;
                foreach (var value in section.Value)
                {
                    if (Removed(section.Key, value.Key)) continue;
                    string currentSection = ConfigSections.CurrentSection(section.Key, value.Key);
                    if (currentSection != section.Key && display.TryGetValue(currentSection, out var currentValues) &&
                        currentValues != null && currentValues.ContainsKey(value.Key)) continue;
                    ConfigEntryBase entry = null;
                    foreach (var pair in config) if (pair.Key.Section == currentSection && pair.Key.Key == value.Key) { entry = pair.Value; break; }
                    if (entry == null && currentSection != section.Key)
                        foreach (var pair in config) if (pair.Key.Section == section.Key && pair.Key.Key == value.Key) { entry = pair.Value; break; }
                    if (entry == null) continue;
                    object parsed;
                    if (entry.SettingType == typeof(bool)) { if (!bool.TryParse(value.Value, out var b)) throw new ArgumentException("Invalid preset Boolean."); parsed = b; }
                    else if (entry.SettingType == typeof(int)) { if (!int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) throw new ArgumentException("Invalid preset integer."); parsed = n; }
                    else continue;
                    if (entry.Description.AcceptableValues != null && !entry.Description.AcceptableValues.IsValid(parsed))
                        throw new ArgumentException("Preset value outside the supported range: " + value.Key);
                    changes.Add(new KeyValuePair<ConfigEntryBase, string>(entry, value.Value));
                }
            }
            foreach (var change in changes) change.Key.SetSerializedValue(change.Value);
        }
    }
}
