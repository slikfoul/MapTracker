using System;
using System.Collections.Generic;

namespace MapTracker
{
    internal sealed class ClusterPoint
    {
        internal float X, Y;
        internal string Kind;
    }
    internal static class ClusterMath
    {
        private struct Cell : IEquatable<Cell>
        {
            internal int X, Y;
            internal string Kind;
            public bool Equals(Cell other) { return X == other.X && Y == other.Y && Kind == other.Kind; }
            public override bool Equals(object other) { return other is Cell && Equals((Cell)other); }
            public override int GetHashCode() { unchecked { return ((X * 397) ^ Y) * 397 ^ (Kind?.GetHashCode() ?? 0); } }
        }
        private static int Root(int[] parents, int index)
        {
            while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; }
            return index;
        }
        internal static List<List<int>> Build(List<ClusterPoint> points, float distance, int minimum)
        {
            if (distance <= 0) throw new ArgumentOutOfRangeException(nameof(distance));
            var cells = new Dictionary<Cell, List<int>>();
            var parents = new int[points.Count];
            float squared = distance * distance;
            // A cell's diagonal fits inside the merge distance, so its members
            // already share one component and one matching neighbor is sufficient.
            double cellSize = distance / Math.Sqrt(2);
            for (int i = 0; i < points.Count; i++)
            {
                parents[i] = i;
                var point = points[i];
                int x = (int)Math.Floor(point.X / cellSize), y = (int)Math.Floor(point.Y / cellSize);
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        var key = new Cell { X = x + dx, Y = y + dy, Kind = point.Kind };
                        if (!cells.TryGetValue(key, out var neighbors)) continue;
                        foreach (int neighbor in neighbors)
                        {
                            float offsetX = point.X - points[neighbor].X, offsetY = point.Y - points[neighbor].Y;
                            if (offsetX * offsetX + offsetY * offsetY <= squared)
                            { parents[Root(parents, i)] = Root(parents, neighbor); break; }
                        }
                    }
                var cell = new Cell { X = x, Y = y, Kind = point.Kind };
                if (!cells.TryGetValue(cell, out var indices)) cells[cell] = indices = new List<int>();
                indices.Add(i);
            }
            var grouped = new Dictionary<int, List<int>>();
            for (int i = 0; i < points.Count; i++)
            {
                int root = Root(parents, i);
                if (!grouped.TryGetValue(root, out var group)) grouped[root] = group = new List<int>();
                group.Add(i);
            }
            var result = new List<List<int>>();
            foreach (var group in grouped.Values) if (group.Count >= minimum) result.Add(group);
            return result;
        }
    }
}
