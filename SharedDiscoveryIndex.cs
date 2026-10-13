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
        internal SharedDiscoveryIndex(IEnumerable<MarkerRecord> records) { foreach (var record in records) Add(record); }
        private static Cell Key(string type, Vector3 point) => new Cell { Type = type, X = (int)Math.Floor(point.x / 2), Y = (int)Math.Floor(point.y / 2), Z = (int)Math.Floor(point.z / 2) };
        internal void Add(MarkerRecord record)
        {
            if (record.dynamic || record.entry == null || record.entry.creature) return;
            AddType(record.type, record);
            if (record.entry.key != record.type) AddType(record.entry.key, record);
        }
        private void AddType(string type, MarkerRecord record)
        {
            if (string.IsNullOrEmpty(type)) return;
            var key = Key(type, record.position);
            if (!cells.TryGetValue(key, out var members)) cells.Add(key, members = new List<MarkerRecord>());
            if (!members.Contains(record)) members.Add(record);
        }
        internal MarkerRecord Find(string type, Vector3 position)
        {
            if (string.IsNullOrEmpty(type)) return null;
            var center = Key(type, position); MarkerRecord shared = null;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            {
                var key = new Cell { Type = type, X = center.X + x, Y = center.Y + y, Z = center.Z + z };
                if (!cells.TryGetValue(key, out var members)) continue;
                foreach (var record in members)
                    if (SharedDiscoveries.Near(record, position)) { if (!record.shared) return record; shared = shared ?? record; }
            }
            return shared;
        }
    }
}
