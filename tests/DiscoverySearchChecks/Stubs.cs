namespace MapTracker
{
    internal struct Position { internal float x, z; }
    internal sealed class CatalogEntry { internal bool creature; }
    internal sealed class MarkerRecord
    {
        internal bool dynamic, shared, favorite;
        internal string id, type, portalTag, name;
        internal Position position;
        internal CatalogEntry entry;
    }
}
