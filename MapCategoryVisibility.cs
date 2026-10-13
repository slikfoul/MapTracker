using System.Collections.Generic;
using BepInEx.Configuration;

namespace MapTracker
{
    internal sealed class MapCategoryVisibility
    {
        private readonly ConfigFile config;
        private readonly Dictionary<string, ConfigEntry<bool>> entries = new Dictionary<string, ConfigEntry<bool>>();
        internal MapCategoryVisibility(ConfigFile file) { config = file; }
        internal void Register(string category)
        {
            foreach (string map in new[] { "Minimap", "Large map" })
            {
                string key = map + ": " + category;
                if (!entries.ContainsKey(key)) entries.Add(key, config.Bind("Map categories", key, true,
                    new ConfigDescription("Show this category on the " + map.ToLowerInvariant() + ". Discovery and saved locations are unaffected.", null,
                        new ConfigurationManagerAttributes { Browsable = false })));
            }
        }
        internal bool Visible(string category, bool large) =>
            !entries.TryGetValue((large ? "Large map: " : "Minimap: ") + category, out var entry) || entry.Value;
    }
}
