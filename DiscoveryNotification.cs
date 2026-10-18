namespace MapTracker
{
    internal static class DiscoveryNotification
    {
        internal static bool Allowed(MarkerRecord record, bool allDungeons, bool selectedType) =>
            record.entry != null && !record.entry.creature && !record.dynamic && !record.shared && !record.imported && !record.collected &&
            ((allDungeons && record.entry.dungeon) || selectedType);
    }
}
