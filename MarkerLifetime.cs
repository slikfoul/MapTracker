using System;
using System.Collections.Generic;
using System.Linq;

namespace MapTracker
{
    internal static class MarkerLifetime
    {
        // Called only for confirmed network destruction, never scene unloading.
        // Return true when the saved discoveries must be rewritten.
        internal static bool Destroyed(IDictionary<string, MarkerRecord> records, string id, Action<MarkerRecord> removePin)
        {
            if (!records.TryGetValue(id, out var record)) return false;
            if (record.dynamic || record.entry?.category == "Portals")
            {
                removePin(record); records.Remove(id);
                return !record.dynamic;
            }
            record.collected = true; record.live = false;
            return true;
        }

        internal static bool RemoveDestroyedPortals(IDictionary<string, MarkerRecord> records, Action<MarkerRecord> removePin)
        {
            // Older builds kept confirmed destroyed portals as collected records.
            // A missing/unloaded scene object alone is never evidence of destruction.
            var removed = records.Where(kv => kv.Value.collected && kv.Value.entry?.category == "Portals")
                .Select(kv => kv.Key).ToList();
            foreach (string id in removed)
            {
                removePin(records[id]); records.Remove(id);
            }
            return removed.Count != 0;
        }
    }
}
