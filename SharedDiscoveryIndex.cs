using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTracker
{
    internal sealed class SharedDiscoveryIndex
    {
        private struct Cell : IEquatable<Cell>
        {
            internal string Type; internal int X, Y, Z;
            public bool Equals(Cell other) => Type == other.Type && X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object other) => other is Cell cell && Equals(cell);
            public override int GetHashCode() => unchecked((((Type.GetHashCode() * 397) ^ X) * 397 ^ Y) * 397 ^ Z);
        }
        private readonly Dictionary<Cell, List<MarkerRecord>> cells = new Dictionary<Cell, List<MarkerRecord>>();
        private readonly Dictionary<MarkerRecord, Cell[]> indexed = new Dictionary<MarkerRecord, Cell[]>();
        private readonly Dictionary<Cell, HashSet<MarkerRecord>> dungeonAreas = new Dictionary<Cell, HashSet<MarkerRecord>>();
        private readonly Dictionary<MarkerRecord, Cell> dungeonAreaKeys = new Dictionary<MarkerRecord, Cell>();
        private readonly Dictionary<string, MarkerRecord> dungeonIds = new Dictionary<string, MarkerRecord>(StringComparer.Ordinal);
        private readonly Dictionary<MarkerRecord, string[]> indexedAliases = new Dictionary<MarkerRecord, string[]>();
        internal Predicate<MarkerRecord> CandidateIsCurrent;
        internal SharedDiscoveryIndex(IEnumerable<MarkerRecord> records) { foreach (var record in records) Add(record); }
        private static Cell Key(string type, Vector3 point) => new Cell { Type = type, X = (int)Math.Floor(point.x / 2), Y = (int)Math.Floor(point.y / 2), Z = (int)Math.Floor(point.z / 2) };
        private static Cell AreaKey(string type, Vector3 point) => new Cell { Type = type, X = (int)Math.Floor(point.x / 64), Z = (int)Math.Floor(point.z / 64) };
        private void AddDungeonIdentity(MarkerRecord record, string type)
        {
            if (record.entry?.dungeon != true) return;
            var area = AreaKey(type, record.position);
            if (!dungeonAreaKeys.TryGetValue(record, out var previous) || !previous.Equals(area))
            {
                RemoveDungeonIdentity(record);
                dungeonAreaKeys[record] = area;
                if (!dungeonAreas.TryGetValue(area, out var members)) dungeonAreas.Add(area, members = new HashSet<MarkerRecord>());
                members.Add(record);
            }
            if (indexedAliases.TryGetValue(record, out var aliases) && ReferenceEquals(aliases, record.dungeonAliases)) return;
            if (aliases != null) foreach (string id in aliases)
                if (dungeonIds.TryGetValue(id, out var owner) && owner == record) dungeonIds.Remove(id);
            indexedAliases[record] = record.dungeonAliases;
            if (record.dungeonAliases != null) foreach (string id in record.dungeonAliases)
                if (!string.IsNullOrEmpty(id)) dungeonIds[id] = record;
        }
        private void RemoveDungeonIdentity(MarkerRecord record)
        {
            if (dungeonAreaKeys.TryGetValue(record, out var area))
            {
                if (dungeonAreas.TryGetValue(area, out var members)) { members.Remove(record); if (members.Count == 0) dungeonAreas.Remove(area); }
                dungeonAreaKeys.Remove(record);
            }
            if (indexedAliases.TryGetValue(record, out var aliases))
            {
                if (aliases != null) foreach (string id in aliases)
                    if (dungeonIds.TryGetValue(id, out var owner) && owner == record) dungeonIds.Remove(id);
                indexedAliases.Remove(record);
            }
        }
        internal MarkerRecord FindAlias(string id) => id != null && dungeonIds.TryGetValue(id, out var record) &&
            (CandidateIsCurrent == null || CandidateIsCurrent(record)) ? record : null;
        internal IEnumerable<MarkerRecord> DungeonsInArea(string type, Vector3 origin, float radius)
        {
            if (radius <= 0 || radius > 100 || float.IsNaN(radius)) yield break;
            int minX = (int)Math.Floor((origin.x - radius) / 64), maxX = (int)Math.Floor((origin.x + radius) / 64);
            int minZ = (int)Math.Floor((origin.z - radius) / 64), maxZ = (int)Math.Floor((origin.z + radius) / 64);
            for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
                if (dungeonAreas.TryGetValue(new Cell { Type = type, X = x, Z = z }, out var members))
                    foreach (var record in members)
                        if (CandidateIsCurrent == null || CandidateIsCurrent(record)) yield return record;
        }
        internal void Add(MarkerRecord record)
        {
            if (record.dynamic || !CatalogContent.CanTrack(record.entry) || record.entry.creature) { Remove(record); return; }
            string type = string.IsNullOrEmpty(record.type) ? record.entry.key : record.type;
            if (string.IsNullOrEmpty(type)) return;
            var first = Key(type, record.position);
            bool alias = !string.IsNullOrEmpty(record.entry.key) && record.entry.key != type;
            var second = alias ? Key(record.entry.key, record.position) : first;
            bool hasOrigin = record.entry.dungeon && record.dungeonOrigin.HasValue;
            var third = hasOrigin ? Key(type, record.dungeonOrigin.Value) : first;
            bool originCell = hasOrigin && !third.Equals(first);
            var fourth = alias && originCell ? Key(record.entry.key, record.dungeonOrigin.Value) : second;
            int count = (alias ? 2 : 1) * (originCell ? 2 : 1);
            if (indexed.TryGetValue(record, out var previous) && previous.Length == count &&
                previous[0].Equals(first) && (!alias || previous[1].Equals(second)) &&
                (!originCell || previous[alias ? 2 : 1].Equals(third)) && (!alias || !originCell || previous[3].Equals(fourth)))
            { AddDungeonIdentity(record, type); return; }
            Remove(record);
            var keys = alias ? originCell ? new[] { first, second, third, fourth } : new[] { first, second } :
                originCell ? new[] { first, third } : new[] { first };
            indexed[record] = keys;
            foreach (var key in keys) AddCell(key, record);
            AddDungeonIdentity(record, type);
        }
        internal void Remove(MarkerRecord record)
        {
            RemoveDungeonIdentity(record);
            if (!indexed.TryGetValue(record, out var previous)) return;
            foreach (var key in previous)
                if (cells.TryGetValue(key, out var members)) { members.Remove(record); if (members.Count == 0) cells.Remove(key); }
            indexed.Remove(record);
        }
        private void AddCell(Cell key, MarkerRecord record)
        {
            if (!cells.TryGetValue(key, out var members)) cells.Add(key, members = new List<MarkerRecord>());
            members.Add(record);
        }
        internal MarkerRecord Find(string type, Vector3 position)
        {
            if (string.IsNullOrEmpty(type)) return null;
            var center = Key(type, position); MarkerRecord shared = null;
            // Keep ordinary ore/resource lookups allocation-free.
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            {
                var key = new Cell { Type = type, X = center.X + x, Y = center.Y + y, Z = center.Z + z };
                if (!cells.TryGetValue(key, out var members)) continue;
                foreach (var record in members)
                    if ((CandidateIsCurrent == null || CandidateIsCurrent(record)) && SharedDiscoveries.Near(record, position))
                    { if (!record.shared) return record; shared = shared ?? record; }
            }
            return shared;
        }
        internal MarkerRecord FindSite(string type, Vector3 position, Vector3? origin)
        {
            if (!origin.HasValue) return Find(type, position);
            MarkerRecord shared = null;
            foreach (var record in Candidates(type, position, origin))
            { if (!record.shared) return record; shared = shared ?? record; }
            return shared;
        }
        internal IEnumerable<MarkerRecord> Candidates(string type, Vector3 position, Vector3? origin)
        {
            if (string.IsNullOrEmpty(type)) yield break;
            var seen = new HashSet<MarkerRecord>();
            var centers = origin.HasValue ? new[] { Key(type, position), Key(type, origin.Value) } : new[] { Key(type, position) };
            foreach (var center in centers)
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            {
                var key = new Cell { Type = type, X = center.X + x, Y = center.Y + y, Z = center.Z + z };
                if (!cells.TryGetValue(key, out var members)) continue;
                foreach (var record in members)
                    if ((CandidateIsCurrent == null || CandidateIsCurrent(record)) && seen.Add(record) &&
                        (record.entry.dungeon ? DungeonDiscoveries.SameEntrance(record, type, position, false, origin) : SharedDiscoveries.Near(record, position)))
                        yield return record;
            }
        }
    }
}
