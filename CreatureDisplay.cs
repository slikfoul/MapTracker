namespace MapTracker
{
    internal static class CreatureDisplay
    {
        internal static bool ShowHead(bool large, float distance, float zoom, float minimum, float nativeThreshold, bool iconsEnabled = true)
            => iconsEnabled && (!large || MapLabelZoom.Visible(zoom, minimum, nativeThreshold));

        internal static bool ShowNearbyName(float distance, float maximum = 30f) => distance <= maximum;
    }
}
