using System;
using System.Collections.Generic;
using System.Linq;

namespace MapTracker
{
    internal static class MarkerLifetime
    {
        internal static bool Current(IDictionary<string, MarkerRecord> records, MarkerRecord record) =>
            record != null && !string.IsNullOrEmpty(record.id) && records.TryGetValue(record.id, out var current) && ReferenceEquals(current, record);
        // A delayed cleanup may still hold an old instance after its ID has been reused.
        // Release that instance's working-set/UI references without deleting its replacement.
        internal static bool Forget(IDictionary<string, MarkerRecord> records, MarkerRecord record, Action<MarkerRecord> release)
        {
            bool current = Current(records, record);
            if (current) records.Remove(record.id);
            release(record);
            return current;
        }
        // Called only for confirmed network destruction, never scene unloading.
        // Return true when the saved discoveries must be rewritten.
        internal static bool Destroyed(IDictionary<string, MarkerRecord> records, string id, Action<MarkerRecord> removePin)
        {
            if (!records.TryGetValue(id, out var record)) return false;
            if (record.dynamic || record.entry?.category == "Portals")
            {
                Forget(records, record, removePin);
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
                if (!records.TryGetValue(id, out var record)) continue;
                Forget(records, record, removePin);
            }
            return removed.Count != 0;
        }
    }
}
