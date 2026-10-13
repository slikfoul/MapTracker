using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;

namespace MapTracker
{
    // Initial settings captured from the configured profile. No world, player,
    // discovered positions, or migration flags are included in this resource.
    public static class DefaultSettings
    {
        private static readonly Dictionary<string, Dictionary<string, string>> settings = Load();

        private static Dictionary<string, Dictionary<string, string>> Load()
        {
            using (var stream = typeof(DefaultSettings).Assembly.GetManifestResourceStream("MapTracker.DefaultSettings.json"))
            using (var reader = new StreamReader(stream))
                return DataJson.Read<Dictionary<string, Dictionary<string, string>>>(reader.ReadToEnd());
        }

        public static T Value<T>(string section, string key, T fallback)
        {
            string value;
            if (!settings.TryGetValue(section, out var values) || !values.TryGetValue(key, out value))
            {
                string previous = ConfigSections.PreviousSection(section, key);
                if (previous == section || !settings.TryGetValue(previous, out values) || !values.TryGetValue(key, out value)) return fallback;
            }
            if (typeof(T) == typeof(bool)) return (T)(object)bool.Parse(value);
            if (typeof(T) == typeof(int)) return (T)(object)int.Parse(value, CultureInfo.InvariantCulture);
            throw new NotSupportedException("Unsupported initial setting type: " + typeof(T).Name);
        }

        public static ConfigEntry<T> Bind<T>(ConfigFile config, string oldSection, string section, string key,
            T fallback, ConfigDescription description) =>
            ConfigSections.Bind(config, oldSection, section, key, Value(section, key, fallback), description);
    }
}
