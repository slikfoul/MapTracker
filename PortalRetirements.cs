using System.Collections.Generic;
using UnityEngine;

namespace MapTracker
{
    // Native ZDO identities, not coordinates: rebuilding in the same spot creates
    // a different object. Keep these across sessions so an offline peer cannot
    // resurrect a retired object when its old discovery archive returns.
    public sealed class PortalDeletion
    {
        public string id, type;
        public Vector3 position;
    }

    internal sealed class PortalRetirements
    {
        private readonly Dictionary<string, MarkerRecord> records = new Dictionary<string, MarkerRecord>();
        internal IEnumerable<MarkerRecord> Records => records.Values;
        internal bool Contains(string id) => id != null && records.ContainsKey(id);
        internal bool Current(MarkerRecord record) => record != null &&
            records.TryGetValue(record.id, out var current) && ReferenceEquals(record, current);
        internal static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            var parts = id.Split(':');
            return parts.Length == 2 && long.TryParse(parts[0], out var owner) && owner != 0 &&
                uint.TryParse(parts[1], out var serial) && serial != 0;
        }
        internal MarkerRecord Add(MarkerRecord source)
        {
            if (source == null || !ValidId(source.id) || string.IsNullOrEmpty(source.type)) return null;
            if (records.TryGetValue(source.id, out var existing)) return existing;
            // Collected portals already mean confirmed destruction in the saved
            // format. Reuse that wire state; older peers can still parse packets.
            var record = new MarkerRecord { id = source.id, type = source.type,
                position = source.position, collected = true, entry = source.entry };
            records.Add(record.id, record);
            return record;
        }
        internal void Remove(string id) => records.Remove(id);
        internal void Clear() => records.Clear();
    }
}
