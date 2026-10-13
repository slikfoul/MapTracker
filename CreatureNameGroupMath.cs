using System;
using System.Collections.Generic;

namespace MapTracker
{
    internal struct CreatureNamePoint
    {
        internal float WorldX, WorldZ, ScreenX, ScreenY;
        internal int Count;
        internal string Kind;
    }
    internal static class CreatureNameGroupMath
    {
        internal static List<List<int>> Build(List<CreatureNamePoint> names, int radius)
        {
            if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            var points = new List<ClusterPoint>(names.Count);
            foreach (var name in names) points.Add(new ClusterPoint { X = name.WorldX, Y = name.WorldZ,
                WorldX = name.WorldX, WorldZ = name.WorldZ, Kind = name.Kind });
            return ClusterMath.BuildBounded(points, radius, 2, radius * 2, radius * 2);
        }
        internal static void Centre(List<CreatureNamePoint> points, out float x, out float y, out int count)
        {
            double sumX = 0, sumY = 0; count = 0;
            foreach (var point in points)
            {
                int weight = Math.Max(1, point.Count);
                count += weight; sumX += point.ScreenX * weight; sumY += point.ScreenY * weight;
            }
            x = count > 0 ? (float)(sumX / count) : 0;
            y = count > 0 ? (float)(sumY / count) : 0;
        }
    }
}
