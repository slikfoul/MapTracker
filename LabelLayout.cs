using System.Collections.Generic;

namespace MapTracker
{
    internal struct LabelBox
    {
        internal float X, Y, Width, Height;
        internal LabelBox(float x, float y, float width, float height)
        { X = x; Y = y; Width = width; Height = height; }
    }
    internal static class LabelLayout
    {
        internal static bool Inside(LabelBox box, LabelBox area) => box.X >= area.X && box.Y >= area.Y &&
            box.X + box.Width <= area.X + area.Width && box.Y + box.Height <= area.Y + area.Height;

        // At most two nearby anchors: never scatter labels across the map.
        internal sealed class TwoAnchorSelection
        {
            private LabelBox[] placed;
            private SpatialOverlap index;
            private int[] choices;
            internal int[] Choose(IReadOnlyList<LabelBox> preferred, IReadOnlyList<LabelBox> alternate,
                LabelBox viewport, LabelBox reserved, float gap)
            {
                if (choices == null || choices.Length != preferred.Count)
                {
                    choices = new int[preferred.Count]; placed = new LabelBox[preferred.Count + 1];
                    index = new SpatialOverlap(placed, gap);
                }
                index.Reset(gap);
                placed[0] = reserved; index.Add(0);
                for (int i = 0; i < preferred.Count; i++)
                {
                    choices[i] = -1;
                    LabelBox box = preferred[i];
                    if (Inside(box, viewport) && index.Overlap(box) == 0) choices[i] = 0;
                    else
                    {
                        box = alternate[i];
                        if (Inside(box, viewport) && index.Overlap(box) == 0) choices[i] = 1;
                    }
                    if (choices[i] < 0) continue;
                    placed[i + 1] = box; index.Add(i + 1);
                }
                return choices;
            }
        }

        internal sealed class Selection
        {
            private LabelBox[] previous;
            private bool[] selected;
            private SpatialOverlap index;
            private float previousGap;
            private bool cached;
            internal bool[] Choose(IReadOnlyList<LabelBox> boxes, float gap)
            {
                if (previous == null || previous.Length != boxes.Count)
                {
                    previous = new LabelBox[boxes.Count]; selected = new bool[boxes.Count];
                    index = new SpatialOverlap(previous, gap); cached = false;
                }
                bool translated = cached && previousGap == gap;
                float dx = boxes.Count > 0 ? boxes[0].X - previous[0].X : 0;
                float dy = boxes.Count > 0 ? boxes[0].Y - previous[0].Y : 0;
                for (int i = 0; translated && i < boxes.Count; i++)
                    translated = boxes[i].Width == previous[i].Width && boxes[i].Height == previous[i].Height &&
                        System.Math.Abs(boxes[i].X - previous[i].X - dx) < 0.001f &&
                        System.Math.Abs(boxes[i].Y - previous[i].Y - dy) < 0.001f;
                if (!translated)
                {
                    index.Reset(gap);
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        previous[i] = boxes[i]; selected[i] = index.Overlap(boxes[i]) == 0;
                        if (selected[i]) index.Add(i);
                    }
                }
                else for (int i = 0; i < boxes.Count; i++) previous[i] = boxes[i];
                cached = true; previousGap = gap;
                return selected;
            }
        }
        internal static LabelBox[] Place(IReadOnlyList<LabelBox> boxes, float gap)
            => new Placement().Place(boxes, gap);

        internal sealed class Placement
        {
            private LabelBox[] placed;
            private LabelBox[] previousBoxes;
            private SpatialOverlap index;
            private float previousGap;
            private bool hasLayout;
            internal LabelBox[] Place(IReadOnlyList<LabelBox> boxes, float gap)
            {
                if (placed == null || placed.Length != boxes.Count)
                {
                    placed = new LabelBox[boxes.Count];
                    previousBoxes = new LabelBox[boxes.Count];
                    index = new SpatialOverlap(placed, gap);
                    hasLayout = false;
                }
                // Panning translates static labels together; their relative collisions
                // and offsets remain unchanged. Keep the previous placement in that case.
                bool translated = hasLayout && previousGap == gap;
                float panX = boxes.Count > 0 ? boxes[0].X - previousBoxes[0].X : 0;
                float panY = boxes.Count > 0 ? boxes[0].Y - previousBoxes[0].Y : 0;
                for (int i = 0; translated && i < boxes.Count; i++)
                    translated = boxes[i].Width == previousBoxes[i].Width && boxes[i].Height == previousBoxes[i].Height &&
                        System.Math.Abs((boxes[i].X - previousBoxes[i].X) - panX) < 0.001f &&
                        System.Math.Abs((boxes[i].Y - previousBoxes[i].Y) - panY) < 0.001f;
                if (translated)
                {
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        placed[i].X = boxes[i].X + (placed[i].X - previousBoxes[i].X);
                        placed[i].Y = boxes[i].Y + (placed[i].Y - previousBoxes[i].Y);
                        previousBoxes[i] = boxes[i];
                    }
                    return placed;
                }
                index.Reset(gap);
                for (int i = 0; i < boxes.Count; i++)
                {
                    LabelBox original = boxes[i], best = original;
                    float bestOverlap = index.Overlap(original);
                    for (int ring = 1; bestOverlap > 0 && ring <= 12; ring++)
                    {
                        float x = (original.Width + gap) * ring, y = (original.Height + gap) * ring;
                        for (int direction = 0; direction < 8; direction++)
                        {
                            float dx = direction < 2 ? 0 : direction == 2 || direction == 4 || direction == 6 ? x : -x;
                            float dy = direction == 2 || direction == 3 ? 0 : direction == 0 || direction == 4 || direction == 5 ? y : -y;
                            var candidate = new LabelBox(original.X + dx, original.Y + dy, original.Width, original.Height);
                            float overlap = index.Overlap(candidate);
                            if (overlap < bestOverlap) { best = candidate; bestOverlap = overlap; }
                            if (bestOverlap == 0) break;
                        }
                    }
                    placed[i] = best;
                    index.Add(i);
                    previousBoxes[i] = original;
                }
                hasLayout = true; previousGap = gap;
                return placed;
            }
        }
        // Only labels sharing screen cells can intersect. Reuse the query buffers;
        // the previous implementation scanned every earlier label for every offset.
        private sealed class SpatialOverlap
        {
            private const float CellSize = 128;
            private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            private readonly List<int> neighbors = new List<int>();
            private readonly Stack<List<int>> bucketPool = new Stack<List<int>>();
            private readonly LabelBox[] placed;
            private readonly int[] seen;
            private float gap;
            private int query;
            internal SpatialOverlap(LabelBox[] placed, float gap)
            { this.placed = placed; this.gap = gap; seen = new int[placed.Length]; }
            internal void Reset(float gap)
            {
                this.gap = gap;
                foreach (var bucket in cells.Values) { bucket.Clear(); bucketPool.Push(bucket); }
                cells.Clear();
                System.Array.Clear(seen, 0, seen.Length); query = 0;
            }
            private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
            private static int Cell(float position) => (int)System.Math.Floor(position / CellSize);
            internal void Add(int id)
            {
                var box = placed[id];
                for (int y = Cell(box.Y); y <= Cell(box.Y + box.Height + gap); y++)
                    for (int x = Cell(box.X); x <= Cell(box.X + box.Width + gap); x++)
                    {
                        long key = Key(x, y);
                        if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = bucketPool.Count > 0 ? bucketPool.Pop() : new List<int>());
                        bucket.Add(id);
                    }
            }
            internal float Overlap(LabelBox candidate)
            {
                neighbors.Clear();
                if (query == int.MaxValue) { System.Array.Clear(seen, 0, seen.Length); query = 0; }
                query++;
                for (int y = Cell(candidate.Y); y <= Cell(candidate.Y + candidate.Height + gap); y++)
                    for (int x = Cell(candidate.X); x <= Cell(candidate.X + candidate.Width + gap); x++)
                        if (cells.TryGetValue(Key(x, y), out var bucket))
                            foreach (int id in bucket)
                                if (seen[id] != query) { seen[id] = query; neighbors.Add(id); }
                // Preserve the old summation order and therefore placement ties.
                neighbors.Sort();
                float area = 0;
                foreach (int id in neighbors)
                {
                    var other = placed[id];
                    float x = System.Math.Min(candidate.X + candidate.Width + gap, other.X + other.Width + gap) - System.Math.Max(candidate.X, other.X);
                    float y = System.Math.Min(candidate.Y + candidate.Height + gap, other.Y + other.Height + gap) - System.Math.Max(candidate.Y, other.Y);
                    if (x > 0 && y > 0) area += x * y;
                }
                return area;
            }
        }
        // Candidates arrive in nearest-first order. Keep every label that fits,
        // without moving names away from their actual creature markers.
        internal static bool[] Choose(IReadOnlyList<LabelBox> boxes, float gap)
            => new Selection().Choose(boxes, gap);
    }
}
