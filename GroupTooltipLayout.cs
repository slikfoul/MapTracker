using System;
using System.Collections.Generic;

namespace MapTracker
{
    internal static class GroupTooltipLayout
    {
        private static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
        private static bool Free(LabelBox panel, IReadOnlyList<LabelBox> members, float gap)
        {
            foreach (var member in members)
                if (panel.X < member.X + member.Width + gap && panel.X + panel.Width + gap > member.X &&
                    panel.Y < member.Y + member.Height + gap && panel.Y + panel.Height + gap > member.Y) return false;
            return true;
        }
        internal static bool TryPlace(IReadOnlyList<LabelBox> members, LabelBox area, float width, float height,
            float clickedX, float clickedY, float gap, out LabelBox result)
        {
            width = Math.Min(width, area.Width); height = Math.Min(height, area.Height);
            result = default;
            if (width <= 0 || height <= 0) return false;
            float minX = clickedX, minY = clickedY, maxY = clickedY;
            foreach (var member in members)
            { minX = Math.Min(minX, member.X); minY = Math.Min(minY, member.Y); maxY = Math.Max(maxY, member.Y + member.Height); }
            float left = minX - gap - width, y = Clamp(clickedY - height * 0.5f, area.Y, area.Y + area.Height - height);
            var preferred = new LabelBox(left, y, width, height);
            if (LabelLayout.Inside(preferred, area) && Free(preferred, members, gap)) { result = preferred; return true; }
            left = Clamp(left, area.X, area.X + area.Width - width);
            foreach (float row in new[] { maxY + gap, minY - gap - height })
            {
                var candidate = new LabelBox(left, row, width, height);
                if (LabelLayout.Inside(candidate, area) && Free(candidate, members, gap)) { result = candidate; return true; }
            }
            float best = float.MaxValue;
            for (int x = 0; x <= 4; x++) for (int row = 0; row <= 8; row++)
            {
                var candidate = new LabelBox(area.X + (area.Width - width) * x / 4,
                    area.Y + (area.Height - height) * row / 8, width, height);
                if (!Free(candidate, members, gap)) continue;
                float dx = candidate.X - preferred.X, dy = candidate.Y - preferred.Y;
                float score = dx * dx + dy * dy;
                if (score < best) { result = candidate; best = score; }
            }
            return best != float.MaxValue;
        }
    }
}
