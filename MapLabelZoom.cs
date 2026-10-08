namespace MapTracker
{
    internal static class MapLabelZoom
    {
        internal static bool Visible(float zoom, float minimum, float nativeThreshold) =>
            zoom <= System.Math.Min(nativeThreshold, minimum * 2.5f);
    }
}
