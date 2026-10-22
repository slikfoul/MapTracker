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
            if (record.entry.dungeon) return DungeonDiscoveries.SameEntrance(record, record.type, position, false);
            float limit = record.entry.category == "Portals" ? 0.25f : 0.1f;
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
            => RefreshCreatureObservation(record, incoming, peer, now, 15);
        internal static void RefreshCreatureObservation(MarkerRecord record, MarkerRecord incoming, long peer, float now, int lifetime)
        {
            record.remoteCreaturePosition = incoming.position; record.remoteCreatureLevel = incoming.creatureLevel;
            record.remoteCreatureRelation = incoming.creatureRelation;
            Observe(record, peer, true, now, lifetime);
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
            => ReceiveTracked(records, incoming, peer, now, index, null);
        internal static MarkerRecord Resolve(IDictionary<string, MarkerRecord> records, MarkerRecord incoming,
            SharedDiscoveryIndex index, Predicate<MarkerRecord> allow = null)
        {
            records.TryGetValue(incoming.id, out var exact);
            var aliasOwner = index?.FindAlias(incoming.id);
            if (aliasOwner != null && aliasOwner.type == incoming.type && (allow == null || allow(aliasOwner)) &&
                (!aliasOwner.dungeonOrigin.HasValue || Vector3.Distance(aliasOwner.dungeonOrigin.Value, incoming.position) <= 100f) &&
                (!aliasOwner.dungeonOrigin.HasValue || !incoming.dungeonOrigin.HasValue ||
                    DungeonDiscoveries.Close(aliasOwner.dungeonOrigin.Value, incoming.dungeonOrigin.Value))) return aliasOwner;
            if (incoming.dungeonAliases != null)
                foreach (string id in incoming.dungeonAliases)
                    if (!string.IsNullOrEmpty(id) && records.TryGetValue(id, out var alias) && alias.type == incoming.type && alias.entry?.dungeon == true &&
                        incoming.dungeonOrigin.HasValue && Vector3.Distance(alias.position, incoming.dungeonOrigin.Value) <= 100f &&
                        (allow == null || allow(alias)) && (!alias.dungeonOrigin.HasValue ||
                        incoming.dungeonOrigin.HasValue && DungeonDiscoveries.Close(alias.dungeonOrigin.Value, incoming.dungeonOrigin.Value)))
                        if (exact == null || exact.shared && !alias.shared) { exact = alias; }
            // Keep an exact local observation, then prefer any local entrance over a remote alias.
            if (exact != null && (!exact.shared || exact.dynamic)) return exact;
            if (index != null && allow == null)
            {
                var indexedSite = index.FindSite(incoming.type, incoming.position, incoming.dungeonOrigin);
                return indexedSite != null && (!indexedSite.shared || exact == null) ? indexedSite : exact;
            }
            var candidates = index != null ? index.Candidates(incoming.type, incoming.position, incoming.dungeonOrigin) :
                records.Values.Where(r => !r.dynamic && r.entry != null && !r.entry.creature &&
                    (r.type == incoming.type || r.entry.key == incoming.type) &&
                    (r.entry.dungeon ? DungeonDiscoveries.SameEntrance(r, incoming.type, incoming.position, false, incoming.dungeonOrigin) : Near(r, incoming.position)));
            var site = candidates.Where(r => allow == null || allow(r)).OrderBy(r => r.shared).FirstOrDefault();
            return site != null && (!site.shared || exact == null) ? site : exact;
        }
        internal static MarkerRecord ReceiveTracked(IDictionary<string, MarkerRecord> records, MarkerRecord incoming,
            long peer, float now, SharedDiscoveryIndex index, Action<MarkerRecord> removePin, Predicate<MarkerRecord> allow = null)
        {
            MarkerRecord existing = Resolve(records, incoming, index, allow);
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
                // Old-format peers may still send the proxy root. Keep a learned doorway.
                bool rootOnly = existing.dungeonOrigin.HasValue && !incoming.dungeonOrigin.HasValue &&
                    (existing.dungeonAliases != null && existing.dungeonAliases.Contains(incoming.id) ||
                     DungeonDiscoveries.Close(incoming.position, existing.dungeonOrigin.Value) &&
                     !DungeonDiscoveries.Close(existing.position, existing.dungeonOrigin.Value));
                if (!rootOnly) existing.position = incoming.position;
                if (incoming.dungeonOrigin.HasValue) existing.dungeonOrigin = incoming.dungeonOrigin;
                existing.portalTag = incoming.portalTag;
                if (existing.entry?.dungeon != true && incoming.entry?.dungeon != true)
                { existing.completed = incoming.completed; existing.visited = incoming.visited; }
                existing.collected = incoming.collected;
                existing.pickedTime = incoming.pickedTime;
                existing.sourcePeer = peer; existing.nameCache = null;
                if (incoming.entry != null) existing.entry = incoming.entry;
            }
            if (existing.entry?.dungeon == true || incoming.entry?.dungeon == true) DungeonProgress.Merge(existing, incoming);
            if (existing.entry?.dungeon == true)
            {
                if (!existing.live && incoming.dungeonAliases != null && incoming.dungeonAliases.Contains(existing.id))
                    existing.position = incoming.position;
                DungeonDiscoveries.AdoptOrigin(existing, incoming);
                DungeonDiscoveries.AdoptAliases(existing, incoming);
                if (existing != incoming) DungeonDiscoveries.RememberAlias(existing, incoming.id);
                index?.Add(existing);
                if (index != null) DungeonDiscoveries.Reconcile(records, existing, index, removePin ?? (_ => { }), allow);
            }
            Observe(existing, peer, incoming.observed, now, 40);
            return existing;
        }
        internal static bool RemoveDuplicates(IDictionary<string, MarkerRecord> records, Action<MarkerRecord> removePin)
        {
            bool changed = false;
            foreach (var step in RemoveDuplicatesIncrementally(records, removePin, () => changed = true)) { }
            return changed;
        }
        internal static IEnumerable<byte> RemoveDuplicatesIncrementally(IDictionary<string, MarkerRecord> records,
            Action<MarkerRecord> removePin, Action changed, Action<SharedDiscoveryIndex> completed = null,
            SharedDiscoveryIndex workingIndex = null)
        {
            var index = workingIndex ?? new SharedDiscoveryIndex(new MarkerRecord[0]);
            index.CandidateIsCurrent = r => MarkerLifetime.Current(records, r);
            var source = records.Values.ToArray();
            var ordered = new PreparationEntry[source.Length]; int count = 0;
            foreach (var record in source)
            {
                if (!record.dynamic && record.entry != null && !record.entry.creature)
                    ordered[count++] = new PreparationEntry(record);
                yield return 0;
            }
            foreach (var step in FrameWork.StableSort(ordered, count, PreparationEntry.Compare)) yield return step;
            for (int i = 0; i < count; i++)
            {
                var record = ordered[i].Record;
                if (!MarkerLifetime.Current(records, record)) { yield return 0; continue; }
                var canonical = index.FindSite(record.type, record.position, record.dungeonOrigin);
                if (canonical == null) { index.Add(record); yield return 0; continue; }
                if (ReferenceEquals(canonical, record)) { yield return 0; continue; }
                // Live scanning can populate the shared index while archive work
                // yields. Preserve the same local/live/ID preference in that case.
                if (PreparationEntry.Compare(new PreparationEntry(record), new PreparationEntry(canonical)) < 0)
                { var previous = canonical; canonical = record; record = previous; index.Add(canonical); }
                if (canonical.entry?.dungeon == true) { DungeonDiscoveries.Merge(canonical, record); index.Add(canonical); }
                if (canonical.shared == record.shared)
                {
                    if (canonical.entry?.dungeon != true) { canonical.visited |= record.visited; canonical.completed |= record.completed; }
                    canonical.collected |= record.collected; canonical.favorite |= record.favorite;
                }
                canonical.nameCache = null; MarkerLifetime.Forget(records, record, removePin); changed();
                yield return 0;
            }
            completed?.Invoke(index);
        }
        private struct PreparationEntry
        {
            internal readonly MarkerRecord Record;
            private readonly bool shared, live;
            private readonly string id;
            internal PreparationEntry(MarkerRecord record)
            { Record = record; shared = record.shared; live = record.live; id = record.id; }
            internal static int Compare(PreparationEntry a, PreparationEntry b)
            {
                int order = a.shared.CompareTo(b.shared);
                if (order == 0) order = b.live.CompareTo(a.live);
                return order == 0 ? StringComparer.Ordinal.Compare(a.id, b.id) : order;
            }
        }
    }
}
