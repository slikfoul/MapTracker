using MapTracker;

// Independent snapshot of the pre-optimization algorithm, retained to verify
// nearest-first ordering, offset ties, gaps and densely overlapping labels.
static class LayoutReference
{
    internal static LabelBox[] Place(IReadOnlyList<LabelBox> boxes, float gap)
    {
        var placed = new LabelBox[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
        {
            LabelBox original = boxes[i], best = original;
            float bestOverlap = Overlap(original, placed, i, gap);
            for (int ring = 1; bestOverlap > 0 && ring <= 12; ring++)
            {
                float x = (original.Width + gap) * ring, y = (original.Height + gap) * ring;
                var offsets = new[] { new LabelBox(0, y, 0, 0), new LabelBox(0, -y, 0, 0),
                    new LabelBox(x, 0, 0, 0), new LabelBox(-x, 0, 0, 0),
                    new LabelBox(x, y, 0, 0), new LabelBox(-x, y, 0, 0),
                    new LabelBox(x, -y, 0, 0), new LabelBox(-x, -y, 0, 0) };
                foreach (var offset in offsets)
                {
                    var candidate = new LabelBox(original.X + offset.X, original.Y + offset.Y, original.Width, original.Height);
                    float overlap = Overlap(candidate, placed, i, gap);
                    if (overlap < bestOverlap) { best = candidate; bestOverlap = overlap; }
                    if (bestOverlap == 0) break;
                }
            }
            placed[i] = best;
        }
        return placed;
    }
    static float Overlap(LabelBox candidate, LabelBox[] placed, int count, float gap)
    {
        float area = 0;
        for (int i = 0; i < count; i++)
        {
            var other = placed[i];
            float x = Math.Min(candidate.X + candidate.Width + gap, other.X + other.Width + gap) - Math.Max(candidate.X, other.X);
            float y = Math.Min(candidate.Y + candidate.Height + gap, other.Y + other.Height + gap) - Math.Max(candidate.Y, other.Y);
            if (x > 0 && y > 0) area += x * y;
        }
        return area;
    }
}
