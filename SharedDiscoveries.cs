using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    internal static class SharedDiscoveries
    {
        internal static MarkerRecord Find(IDictionary<string, MarkerRecord> records, string id, string type, Vector3 position)
        {
            if (!string.IsNullOrEmpty(id) && records.TryGetValue(id, out var exact) && exact.type == type) return exact;
            return records.Values.Where(r => !r.dynamic && r.entry != null && !r.entry.creature &&
                (r.type == type || r.entry.key == type) && Near(r, position)).OrderBy(r => r.shared).FirstOrDefault();
        }
        internal static bool Near(MarkerRecord record, Vector3 position)
        {
            float limit = record.entry.dungeon ? 2 : record.entry.category == "Portals" ? 0.25f : 0.1f;
            float x = record.position.x - position.x, y = record.position.y - position.y, z = record.position.z - position.z;
            return x * x + y * y + z * z < limit * limit;
        }
        internal static void Observe(MarkerRecord record, long peer, bool seen, float now, float lifetime)
        {
            if (record.remoteObservers == null) record.remoteObservers = new Dictionary<long, float>();
            if (seen) record.remoteObservers[peer] = now + lifetime; else record.remoteObservers.Remove(peer);
            record.remoteSeenUntil = record.remoteObservers.Count == 0 ? 0 : record.remoteObservers.Values.Max();
        }
        internal static void ObserveCreature(MarkerRecord record, MarkerRecord incoming, long peer, float now)
        {
            record.remoteCreaturePosition = incoming.position; record.remoteCreatureLevel = incoming.creatureLevel;
            record.remoteCreatureRelation = incoming.creatureRelation;
            Observe(record, peer, true, now, 4);
        }
        internal static bool HandOffCreature(MarkerRecord record, float now)
        {
            if (!record.dynamic || record.entry == null || !record.entry.creature || record.remoteSeenUntil <= now) return false;
            record.shared = true; record.character = null; record.expiresAt = record.remoteSeenUntil;
            record.position = record.remoteCreaturePosition; record.creatureLevel = record.remoteCreatureLevel;
            record.creatureRelation = record.remoteCreatureRelation;
            if (record.remoteObservers != null)
                record.sourcePeer = record.remoteObservers.OrderByDescending(p => p.Value).First().Key;
            return true;
        }
        internal static MarkerRecord Receive(IDictionary<string, MarkerRecord> records, MarkerRecord incoming, long peer, float now, SharedDiscoveryIndex index = null)
        {
            MarkerRecord existing = records.TryGetValue(incoming.id, out var exact) ? exact :
                index != null ? index.Find(incoming.type, incoming.position) : Find(records, incoming.id, incoming.type, incoming.position);
            if (existing != null && (existing.dynamic || existing.entry?.creature == true ||
                existing.type != incoming.type && existing.entry?.key != incoming.type)) return existing;
            if (existing == null)
            {
                incoming.shared = true; incoming.sourcePeer = peer; incoming.favorite = false; incoming.dynamic = false;
                records[incoming.id] = incoming; existing = incoming;
                index?.Add(incoming);
            }
            else if (existing.shared)
            {
                existing.position = incoming.position; existing.portalTag = incoming.portalTag;
                existing.completed = incoming.completed; existing.visited = incoming.visited; existing.collected = incoming.collected;
                existing.sourcePeer = peer; existing.nameCache = null;
                if (incoming.entry != null) existing.entry = incoming.entry;
            }
            Observe(existing, peer, incoming.observed, now, 40);
            return existing;
        }
        internal static bool RemoveDuplicates(IDictionary<string, MarkerRecord> records, Action<MarkerRecord> removePin)
        {
            var index = new SharedDiscoveryIndex(new MarkerRecord[0]); bool changed = false;
            foreach (var record in records.Values.Where(r => !r.dynamic && r.entry != null && !r.entry.creature)
                .OrderBy(r => r.shared).ThenByDescending(r => r.live).ThenBy(r => r.id, StringComparer.Ordinal).ToList())
            {
                var canonical = index.Find(record.type, record.position);
                if (canonical == null) { index.Add(record); continue; }
                if (canonical.shared == record.shared)
                { canonical.visited |= record.visited; canonical.completed |= record.completed; canonical.collected |= record.collected; canonical.favorite |= record.favorite; }
                canonical.nameCache = null; removePin(record); records.Remove(record.id); changed = true;
            }
            return changed;
        }
    }
}
