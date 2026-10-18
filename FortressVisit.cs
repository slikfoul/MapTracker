namespace MapTracker
{
    internal static class FortressVisit
    {
        internal const string Type = "location:CharredFortress";
        internal const string Chest = "TreasureChest_charredfortress";
        internal static bool Mark(MarkerRecord fortress, bool opened)
        {
            if (!opened || fortress?.type != Type || fortress.entry?.dungeon != true || fortress.visited) return false;
            fortress.visited = true;
            fortress.shared = false; fortress.sourcePeer = 0;
            fortress.nameCache = null;
            return true;
        }
    }
}
