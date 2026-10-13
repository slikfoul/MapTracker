using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    // A network ID is not a durable identity for a remembered dungeon entrance.
    internal static class DungeonDiscoveries
    {
        private static bool Eligible(MarkerRecord record)
        { return !record.shared && !record.dynamic && record.entry?.dungeon == true; }

        private static bool Legacy(MarkerRecord record)
        { return record.imported && record.type != null && record.type.StartsWith("legacy:", StringComparison.Ordinal); }

        private static bool SameEntrance(MarkerRecord record, string type, Vector3 position, bool legacy)
        {
            // Only unclassified legacy imports can be refined to another type.
            // Never combine two known dungeon types or entrances on different floors.
            if (record.type != type && !Legacy(record) && !legacy) return false;
            float x = record.position.x - position.x, y = record.position.y - position.y, z = record.position.z - position.z;
            return x * x + y * y + z * z < 4f;
        }

        internal static MarkerRecord Rebind(IDictionary<string, MarkerRecord> records, string id,
            CatalogEntry entry, Vector3 position, Action<MarkerRecord> removePin)
        {
            var record = records.Values.FirstOrDefault(r => Eligible(r) && SameEntrance(r, entry.key, position, false));
            if (record == null) return null;
            if (record.type != entry.key) removePin(record);
            records.Remove(record.id);
            record.id = id; record.type = entry.key; record.entry = entry; record.position = position;
            record.imported = false;
            // The caller adds it under its new ID, as for a newly observed record.
            return record;
        }

        internal static bool RemoveDuplicates(IDictionary<string, MarkerRecord> records, Action<MarkerRecord> removePin)
        {
            var entrances = records.Values.Where(Eligible)
                .OrderByDescending(r => r.live).ThenBy(Legacy).ThenBy(r => r.imported)
                .ThenBy(r => r.id, StringComparer.Ordinal).ToList();
            var retained = new List<MarkerRecord>();
            bool changed = false;
            foreach (var record in entrances)
            {
                var survivor = retained.FirstOrDefault(r => SameEntrance(r, record.type, record.position, Legacy(record)));
                if (survivor == null) { retained.Add(record); continue; }
                survivor.visited |= record.visited;
                survivor.completed |= record.completed;
                survivor.collected |= record.collected;
                survivor.imported &= record.imported;
                survivor.favorite |= record.favorite;
                survivor.nameCache = null;
                removePin(record); records.Remove(record.id); changed = true;
            }
            return changed;
        }
    }
}
