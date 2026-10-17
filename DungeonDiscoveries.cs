using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    // A network ID is not a durable identity for a remembered dungeon entrance.
    internal static class DungeonDiscoveries
    {
        internal const int MaxAliases = 8;
        internal static void RememberAlias(MarkerRecord record, string id)
        {
            if (!record.dungeonOrigin.HasValue || string.IsNullOrEmpty(id) || id.Length > 64 || id == record.id ||
                record.dungeonAliases != null && (record.dungeonAliases.Length >= MaxAliases || record.dungeonAliases.Contains(id))) return;
            record.dungeonAliases = (record.dungeonAliases ?? new string[0]).Concat(new[] { id }).ToArray();
        }
        internal static void AdoptAliases(MarkerRecord record, MarkerRecord incoming)
        {
            if (incoming.dungeonAliases != null)
                foreach (string id in incoming.dungeonAliases) RememberAlias(record, id);
        }
        private static bool Eligible(MarkerRecord record)
        { return !record.shared && !record.dynamic && record.entry?.dungeon == true; }

        private static bool Legacy(MarkerRecord record)
        { return record.imported && record.type != null && record.type.StartsWith("legacy:", StringComparison.Ordinal); }

        internal static bool Close(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
            return x * x + y * y + z * z < 4f;
        }

        internal static bool SameEntrance(MarkerRecord record, string type, Vector3 position, bool legacy,
            Vector3? origin = null)
        {
            // Only unclassified legacy imports can be refined to another type.
            // Never combine two known dungeon types or entrances on different floors.
            if (record.type != type && record.entry?.key != type && !Legacy(record) && !legacy) return false;
            if (record.dungeonOrigin.HasValue && origin.HasValue)
                return Close(record.dungeonOrigin.Value, origin.Value);
            return Close(record.position, position) ||
                record.dungeonOrigin.HasValue && Close(record.dungeonOrigin.Value, position) ||
                origin.HasValue && Close(record.position, origin.Value);
        }

        internal static void Merge(MarkerRecord survivor, MarkerRecord duplicate)
        {
            DungeonProgress.Merge(survivor, duplicate);
            survivor.collected |= duplicate.collected;
            survivor.imported &= duplicate.imported;
            survivor.favorite |= duplicate.favorite;
            AdoptOrigin(survivor, duplicate);
            AdoptAliases(survivor, duplicate); RememberAlias(survivor, duplicate.id);
            survivor.nameCache = null;
        }

        internal static void AdoptOrigin(MarkerRecord record, MarkerRecord incoming)
        {
            if (!incoming.dungeonOrigin.HasValue || record.dungeonOrigin.HasValue &&
                !Close(record.dungeonOrigin.Value, incoming.dungeonOrigin.Value)) return;
            if (!record.live && Close(record.position, incoming.dungeonOrigin.Value)) record.position = incoming.position;
            if (!record.dungeonOrigin.HasValue) record.dungeonOrigin = incoming.dungeonOrigin;
        }

        internal static bool Reconcile(IDictionary<string, MarkerRecord> records, MarkerRecord survivor,
            SharedDiscoveryIndex index, Action<MarkerRecord> removePin, Predicate<MarkerRecord> allow = null)
        {
            if (survivor.entry?.dungeon != true || !survivor.dungeonOrigin.HasValue) return false;
            bool changed = false;
            // Query only the two observed endpoints; never widen a dungeon's radius.
            foreach (var duplicate in index.Candidates(survivor.type, survivor.position, survivor.dungeonOrigin).ToList())
            {
                if (ReferenceEquals(survivor, duplicate) || duplicate.entry?.dungeon != true ||
                    allow != null && !allow(duplicate)) continue;
                Merge(survivor, duplicate);
                index.Remove(duplicate);
                MarkerLifetime.Forget(records, duplicate, removePin);
                changed = true;
            }
            if (changed) index.Add(survivor);
            foreach (string id in survivor.dungeonAliases ?? new string[0])
                if (!string.IsNullOrEmpty(id) && records.TryGetValue(id, out var alias) && alias != survivor && alias.entry?.dungeon == true &&
                    alias.type == survivor.type && (allow == null || allow(alias)) &&
                    (!alias.dungeonOrigin.HasValue || Close(alias.dungeonOrigin.Value, survivor.dungeonOrigin.Value)))
                {
                    Merge(survivor, alias); index.Remove(alias); MarkerLifetime.Forget(records, alias, removePin); changed = true;
                }
            if (changed) index.Add(survivor);
            return changed;
        }

        internal static bool ReconcileOwnedLocation(IDictionary<string, MarkerRecord> records, MarkerRecord survivor,
            SharedDiscoveryIndex index, float radius, Predicate<MarkerRecord> owns, Action<MarkerRecord> removePin)
        {
            if (!survivor.dungeonOrigin.HasValue || survivor.entry?.dungeon != true) return false;
            bool changed = false;
            foreach (var candidate in index.DungeonsInArea(survivor.type, survivor.dungeonOrigin.Value, radius).ToList())
            {
                if (candidate == survivor || candidate.dynamic || candidate.entry?.dungeon != true || candidate.dungeonOrigin.HasValue || !owns(candidate)) continue;
                Merge(survivor, candidate); index.Remove(candidate); MarkerLifetime.Forget(records, candidate, removePin); changed = true;
            }
            if (changed) index.Add(survivor);
            return changed;
        }

        internal static MarkerRecord Rebind(IDictionary<string, MarkerRecord> records, string id,
            CatalogEntry entry, Vector3 position, Action<MarkerRecord> removePin)
        {
            var record = records.Values.FirstOrDefault(r => Eligible(r) && SameEntrance(r, entry.key, position, false));
            if (record == null) return null;
            if (record.type != entry.key) removePin(record);
            string previousId = record.id;
            records.Remove(previousId);
            record.id = id; record.type = entry.key; record.entry = entry; record.position = position;
            RememberAlias(record, previousId);
            record.imported = false;
            // The caller adds it under its new ID, as for a newly observed record.
            return record;
        }

        internal static bool RemoveDuplicates(IDictionary<string, MarkerRecord> records, Action<MarkerRecord> removePin)
        {
            bool changed = false;
            foreach (var step in RemoveDuplicatesIncrementally(records, removePin, () => changed = true)) { }
            return changed;
        }
        internal static IEnumerable<byte> RemoveDuplicatesIncrementally(IDictionary<string, MarkerRecord> records,
            Action<MarkerRecord> removePin, Action changed)
        {
            var entrances = records.Values.Where(Eligible)
                .OrderByDescending(r => r.live).ThenByDescending(r => r.dungeonOrigin.HasValue).ThenBy(Legacy).ThenBy(r => r.imported)
                .ThenBy(r => r.id, StringComparer.Ordinal).ToList();
            var retained = new List<MarkerRecord>();
            foreach (var record in entrances)
            {
                if (!MarkerLifetime.Current(records, record)) { yield return 0; continue; }
                var survivor = retained.FirstOrDefault(r => MarkerLifetime.Current(records, r) && SameEntrance(r, record.type, record.position, Legacy(record), record.dungeonOrigin));
                if (survivor == null) { retained.Add(record); yield return 0; continue; }
                Merge(survivor, record);
                MarkerLifetime.Forget(records, record, removePin); changed();
                yield return 0;
            }
        }
    }
}
