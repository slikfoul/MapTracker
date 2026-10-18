using System;

namespace MapTracker
{
    internal struct NavigationHudPosition
    {
        internal float X, Y, Width;
    }
    internal static class NavigationHudLayout
    {
        private static int Columns(int count, float width) => Math.Max(1, Math.Min(Math.Max(1, count), (int)(Math.Max(1, width - 32) / 250)));
        internal static NavigationHudPosition Place(int slot, int count, float width)
        {
            int columns = Columns(count, width), row = slot / columns;
            int inRow = Math.Min(columns, count - row * columns);
            float cell = Math.Min(350, Math.Max(1, width - 32) / columns);
            return new NavigationHudPosition { X = (slot % columns - (inRow - 1) * 0.5f) * cell,
                Y = -75 - row * 110, Width = Math.Max(1, cell - 10) };
        }
        internal static float NotificationTop(int count, float width) =>
            Math.Max(195, 85 + ((Math.Max(1, count) + Columns(count, width) - 1) / Columns(count, width)) * 110);
    }
}
