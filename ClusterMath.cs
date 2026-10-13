using System;
using System.Collections.Generic;

namespace MapTracker
{
    internal sealed class ClusterPoint
    {
        internal float X, Y;
        internal string Kind;
        internal float WorldX, WorldZ;
        internal float HalfWidth, HalfHeight;
    }
    internal static class ClusterMath
    {
        private sealed class FootprintGroup
        {
            internal readonly List<int> Members = new List<int>();
            internal FootprintGroup Parent;
            internal float MinX, MaxX, MinY, MaxY, MinWorldX, MaxWorldX, MinWorldZ, MaxWorldZ, HalfWidth, HalfHeight;
            internal FootprintGroup Root()
            {
                if (Parent != null) Parent = Parent.Root();
                return Parent ?? this;
            }
            internal void Add(ClusterPoint point, int id)
            {
                if (Members.Count == 0)
                { MinX = MaxX = point.X; MinY = MaxY = point.Y; MinWorldX = MaxWorldX = point.WorldX; MinWorldZ = MaxWorldZ = point.WorldZ; }
                else
                {
                    MinX = Math.Min(MinX, point.X); MaxX = Math.Max(MaxX, point.X); MinY = Math.Min(MinY, point.Y); MaxY = Math.Max(MaxY, point.Y);
                    MinWorldX = Math.Min(MinWorldX, point.WorldX); MaxWorldX = Math.Max(MaxWorldX, point.WorldX);
                    MinWorldZ = Math.Min(MinWorldZ, point.WorldZ); MaxWorldZ = Math.Max(MaxWorldZ, point.WorldZ);
                }
                HalfWidth = Math.Max(HalfWidth, point.HalfWidth); HalfHeight = Math.Max(HalfHeight, point.HalfHeight);
                Members.Add(id);
            }
            private bool BoundsFit(float minX, float maxX, float minY, float maxY, float minWorldX, float maxWorldX,
                float minWorldZ, float maxWorldZ, float screenLimit, float worldLimit)
            {
                float x = Math.Max(MaxX, maxX) - Math.Min(MinX, minX), y = Math.Max(MaxY, maxY) - Math.Min(MinY, minY);
                float wx = Math.Max(MaxWorldX, maxWorldX) - Math.Min(MinWorldX, minWorldX);
                float wz = Math.Max(MaxWorldZ, maxWorldZ) - Math.Min(MinWorldZ, minWorldZ);
                return x * x + y * y <= screenLimit * screenLimit && wx * wx + wz * wz <= worldLimit * worldLimit;
            }
            internal bool Fits(ClusterPoint point, List<ClusterPoint> points, float gap, float worldLimit)
            {
                float screenLimit = 2 * (2 * Math.Max(Math.Max(HalfWidth, HalfHeight), Math.Max(point.HalfWidth, point.HalfHeight)) + gap);
                if (!BoundsFit(point.X, point.X, point.Y, point.Y, point.WorldX, point.WorldX, point.WorldZ, point.WorldZ, screenLimit, worldLimit)) return false;
                if (point.X + point.HalfWidth + gap < MinX - HalfWidth || point.X - point.HalfWidth - gap > MaxX + HalfWidth ||
                    point.Y + point.HalfHeight + gap < MinY - HalfHeight || point.Y - point.HalfHeight - gap > MaxY + HalfHeight) return false;
                foreach (int id in Members)
                {
                    var other = points[id];
                    if (Math.Abs(point.X - other.X) <= point.HalfWidth + other.HalfWidth + gap &&
                        Math.Abs(point.Y - other.Y) <= point.HalfHeight + other.HalfHeight + gap) return true;
                }
                return false;
            }
            internal bool CanMerge(FootprintGroup other, float gap, float worldLimit)
            {
                float screenLimit = 2 * (2 * Math.Max(Math.Max(HalfWidth, HalfHeight), Math.Max(other.HalfWidth, other.HalfHeight)) + gap);
                return BoundsFit(other.MinX, other.MaxX, other.MinY, other.MaxY, other.MinWorldX, other.MaxWorldX,
                    other.MinWorldZ, other.MaxWorldZ, screenLimit, worldLimit);
            }
        }

        internal static List<List<int>> BuildOverlapping(List<ClusterPoint> points, float gap, int minimum, float worldLimit)
        {
            if (gap < 0 || worldLimit <= 0) throw new ArgumentOutOfRangeException(nameof(worldLimit));
            float cellSize = 1;
            foreach (var point in points) cellSize = Math.Max(cellSize, 2 * Math.Max(point.HalfWidth, point.HalfHeight) + gap);
            var cells = new Dictionary<Cell, List<FootprintGroup>>();
            var groups = new List<FootprintGroup>();
            var queried = new HashSet<FootprintGroup>();
            var compatible = new List<FootprintGroup>();
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                int x = (int)Math.Floor(point.X / cellSize), y = (int)Math.Floor(point.Y / cellSize);
                queried.Clear(); compatible.Clear();
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        if (!cells.TryGetValue(new Cell { X = x + dx, Y = y + dy, Kind = point.Kind }, out var bucket)) continue;
                        foreach (var indexed in bucket)
                        {
                            var group = indexed.Root();
                            if (queried.Add(group) && group.Fits(point, points, gap, worldLimit)) compatible.Add(group);
                        }
                    }
                FootprintGroup chosen;
                if (compatible.Count == 0)
                {
                    chosen = new FootprintGroup(); groups.Add(chosen);
                    var key = new Cell { X = x, Y = y, Kind = point.Kind };
                    if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new List<FootprintGroup>());
                    bucket.Add(chosen);
                }
                else chosen = compatible[0];
                chosen.Add(point, i);
                for (int j = 1; j < compatible.Count; j++)
                {
                    var other = compatible[j];
                    if (!chosen.CanMerge(other, gap, worldLimit)) continue;
                    foreach (int id in other.Members) chosen.Add(points[id], id);
                    other.Parent = chosen;
                }
            }
            var result = new List<List<int>>();
            foreach (var group in groups) if (group.Parent == null && group.Members.Count >= minimum) result.Add(group.Members);
            return result;
        }
        private sealed class Extent
        {
            internal readonly List<int> Members = new List<int>();
            internal float MinX, MaxX, MinY, MaxY, MinWorldX, MaxWorldX, MinWorldZ, MaxWorldZ;
            internal bool Fits(ClusterPoint p, List<ClusterPoint> points, float distance, float screenDiameter, float worldDiameter)
            {
                float x = Math.Max(MaxX, p.X) - Math.Min(MinX, p.X), y = Math.Max(MaxY, p.Y) - Math.Min(MinY, p.Y);
                float wx = Math.Max(MaxWorldX, p.WorldX) - Math.Min(MinWorldX, p.WorldX);
                float wz = Math.Max(MaxWorldZ, p.WorldZ) - Math.Min(MinWorldZ, p.WorldZ);
                if (x * x + y * y > screenDiameter * screenDiameter || wx * wx + wz * wz > worldDiameter * worldDiameter) return false;
                foreach (int i in Members)
                { float dx = p.X - points[i].X, dy = p.Y - points[i].Y; if (dx * dx + dy * dy <= distance * distance) return true; }
                return false;
            }
            internal void Add(ClusterPoint p, int i)
            {
                if (Members.Count == 0) { MinX = MaxX = p.X; MinY = MaxY = p.Y; MinWorldX = MaxWorldX = p.WorldX; MinWorldZ = MaxWorldZ = p.WorldZ; }
                else { MinX = Math.Min(MinX, p.X); MaxX = Math.Max(MaxX, p.X); MinY = Math.Min(MinY, p.Y); MaxY = Math.Max(MaxY, p.Y);
                    MinWorldX = Math.Min(MinWorldX, p.WorldX); MaxWorldX = Math.Max(MaxWorldX, p.WorldX);
                    MinWorldZ = Math.Min(MinWorldZ, p.WorldZ); MaxWorldZ = Math.Max(MaxWorldZ, p.WorldZ); }
                Members.Add(i);
            }
        }
        internal static List<List<int>> BuildBounded(List<ClusterPoint> points, float distance, int minimum, float screenDiameter, float worldDiameter)
        {
            if (screenDiameter <= 0 || worldDiameter <= 0) throw new ArgumentOutOfRangeException(nameof(screenDiameter));
            var result = new List<List<int>>();
            foreach (var connected in Build(points, distance, minimum))
            {
                var parts = new List<Extent>();
                foreach (int i in connected)
                {
                    Extent chosen = null;
                    foreach (var part in parts) if (part.Fits(points[i], points, distance, screenDiameter, worldDiameter)) { chosen = part; break; }
                    if (chosen == null) { chosen = new Extent(); parts.Add(chosen); }
                    chosen.Add(points[i], i);
                }
                foreach (var part in parts) if (part.Members.Count >= minimum) result.Add(part.Members);
            }
            return result;
        }
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
