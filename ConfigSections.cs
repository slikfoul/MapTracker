using BepInEx.Configuration;

namespace MapTracker
{
    public static class ConfigSections
    {
        public static ConfigEntry<T> CopyIfMissing<T>(ConfigFile config, ConfigEntry<T> source, string key,
            T defaultValue, ConfigDescription description)
        {
            string section = source.Definition.Section;
            if (config.TryGetEntry<T>(section, key, out var existing)) return existing;
            bool autosave = config.SaveOnConfigSet; config.SaveOnConfigSet = false;
            try
            {
                var saved = config.Bind(section, key, "");
                string serialized = saved.Value;
                config.Remove(saved.Definition);
                var result = config.Bind(section, key, defaultValue, description);
                result.SetSerializedValue(string.IsNullOrEmpty(serialized) ? source.GetSerializedValue() : serialized);
                return result;
            }
            finally { config.SaveOnConfigSet = autosave; }
        }

        public static ConfigEntry<T> Bind<T>(ConfigFile config, string oldSection, string section, string key,
            T defaultValue, ConfigDescription description)
        {
            if (oldSection == section) return config.Bind(section, key, defaultValue, description);
            if (config.TryGetEntry<T>(section, key, out var existing)) return existing;
            bool autosave = config.SaveOnConfigSet; config.SaveOnConfigSet = false;
            try
            {
                // Preserve serialized choices, preferring a previously saved new section.
                // Typed entries retain their original reset-to-default value.
                var old = config.Bind(oldSection, key, "");
                var current = config.Bind(section, key, "");
                string serialized = string.IsNullOrEmpty(current.Value) ? old.Value : current.Value;
                config.Remove(old.Definition); config.Remove(current.Definition);
                var result = config.Bind(section, key, defaultValue, description);
                if (!string.IsNullOrEmpty(serialized)) result.SetSerializedValue(serialized);
                return result;
            }
            finally { config.SaveOnConfigSet = autosave; }
        }
        internal static string PreviousSection(string section, string key)
        {
            switch (section)
            {
                case "Maps": case "Sharing": case "Tools": return "General";
                case "Names": case "Icons": return "Display";
                case "Grouping": return key == "Group creatures" ? "Creatures" : "General";
                default: return section;
            }
        }
    }
}
