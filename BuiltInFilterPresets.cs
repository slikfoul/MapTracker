using System;
using System.Collections.Generic;
using System.Linq;

namespace MapTracker
{
    public enum BuiltInFilterPreset { Default, Gathering, Exploration, Household }
    public static class BuiltInFilterPresets
    {
        private static readonly string[] PlantTokens =
        {
            "berry", "berries", "bush", "mushroom", "thistle", "dandelion", "carrot", "turnip", "onion",
            "flax", "barley", "fiddlehead", "oat", "kale", "vine", "seed", "jotunpuff", "magecap",
            "smokepuff", "herb", "flower"
        };
        public static FilterPreset Build(IEnumerable<CatalogEntry> entries, BuiltInFilterPreset kind)
        {
            return new FilterPreset
            {
                hostile = kind == BuiltInFilterPreset.Default,
                tamed = kind == BuiltInFilterPreset.Household,
                groupPlants = kind == BuiltInFilterPreset.Gathering ? (bool?)false : null,
                types = entries.ToDictionary(e => e.key, e => new FilterPresetType
                {
                    selected = Includes(e, kind),
                    keep = Includes(e, kind) && !e.creature && e.category != "Dropped items" && e.category != "Plants and pickables" &&
                        (kind != BuiltInFilterPreset.Default || DiscoveryVisibility.KeepByDefault(e.dungeon, e.category))
                })
            };
        }
        internal static bool DefaultSelected(CatalogEntry entry) => Includes(entry, BuiltInFilterPreset.Default);
        internal static bool IsPlantMarker(CatalogEntry entry) =>
            entry.category == "Plants and pickables" || entry.category == "Wild beehives";
        private static bool Includes(CatalogEntry entry, BuiltInFilterPreset kind)
        {
            if (entry.technical) return false;
            switch (kind)
            {
                case BuiltInFilterPreset.Default:
                    return entry.dungeon || entry.category == "Portals" || entry.creature ||
                        (entry.category == "Ore" && (entry.products ?? new string[0]).Any(p =>
                            !string.Equals(p, "Stone", StringComparison.OrdinalIgnoreCase)));
                case BuiltInFilterPreset.Gathering: return IsGrowingCollectible(entry);
                case BuiltInFilterPreset.Exploration:
                    return entry.dungeon || entry.category == "Portals" || entry.category == "Locations" ||
                        HasComponent(entry, "Trader") || HasComponent(entry, "OfferingBowl");
                case BuiltInFilterPreset.Household:
                    return entry.creature || entry.category == "Portals" || entry.category == "Sap collectors" ||
                        (entry.category == "Player structures" && HasComponent(entry, "Beehive"));
                default: return false;
            }
        }
        private static bool HasComponent(CatalogEntry entry, string component) =>
            entry.components != null && entry.components.Contains(component);
        private static bool IsGrowingCollectible(CatalogEntry entry)
        {
            if (entry.category == "Wild beehives") return true;
            if (entry.category != "Plants and pickables") return false;
            // The pickable registry also contains coins, ore, keys and dungeon loot.
            // Match botanical identity, never localization or loose inventory items.
            return new[] { entry.prefab ?? "" }.Concat(entry.products ?? new string[0]).Any(identity =>
                PlantTokens.Any(token => identity.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
        }
    }
}
