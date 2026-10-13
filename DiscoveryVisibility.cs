namespace MapTracker
{
    internal static class DiscoveryVisibility
    {
        internal static bool KeepByDefault(bool dungeon, string category) => dungeon || category == "Portals";
        internal static bool ShowSight(bool keep, bool seen, bool remoteOnly, bool remoteVisible = false) => keep || (!remoteOnly && seen) || remoteVisible;
        internal static bool Show(bool keep, bool loadedLocally, bool remoteOnly, float radius, float distance, bool remoteVisible = false)
        {
            return keep || remoteVisible || (loadedLocally && !remoteOnly && (radius <= 0 || distance <= radius));
        }
    }
}
