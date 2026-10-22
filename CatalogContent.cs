using System;
using System.Linq;

namespace MapTracker
{
    internal static class CatalogContent
    {
        // Transient sound/visual/projectile definitions are catalog metadata,
        // never persistent discoveries or outbound discovery records.
        internal static bool CanTrack(CatalogEntry entry) => entry != null && !entry.technical;
        internal static bool IsServiceDefinition(CatalogEntry entry)
        {
            if (entry == null || entry.creature || entry.dungeon ||
                (entry.key ?? "").StartsWith("location:", StringComparison.Ordinal)) return false;
            if (ObjectIconResolver.IsServiceDefinition(entry)) return true;
            string name = entry.prefab ?? "";
            var components = entry.components ?? new string[0];
            if (name == "Abomination_attack1" || name == "Abomination_attack2" || name == "Abomination_attack3") return true;
            if (components.Contains("MusicLocation")) return true;
            if (name.StartsWith("DG_", StringComparison.Ordinal) && components.Contains("DungeonGenerator")) return true;
            // CreatureSpawner is an invisible spawn point. Visible nests/body piles
            // use SpawnArea and retain their destructible or interactable definition.
            return components.Contains("CreatureSpawner") && !components.Any(c =>
                c == "Destructible" || c == "Piece" || c == "Container" || c == "MineRock" || c == "MineRock5");
        }
    }
}
