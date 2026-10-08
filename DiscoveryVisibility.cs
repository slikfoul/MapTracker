namespace MapTracker
{
    internal static class DiscoveryVisibility
    {
        internal static bool KeepByDefault(bool dungeon, string category) => dungeon || category == "Portals";
        internal static bool ShowSight(bool keep, bool seen, bool remoteOnly) => !remoteOnly && (keep || seen);
        internal static bool Show(bool keep, bool loadedLocally, bool remoteOnly, float radius, float distance)
        {
            return keep || (loadedLocally && !remoteOnly && (radius <= 0 || distance <= radius));
        }
    }
}
