namespace MapTracker
{
    internal enum MapTargetAction { None, Select, Place, Clear, Expand, Consume }
    internal static class MapTargetGesture
    {
        internal static MapTargetAction Resolve(bool alt, bool leftClick, bool hit, bool sameTarget, bool collapsedGroup)
        {
            if (!alt) return MapTargetAction.None;
            // Swallow the native double/right-click actions while Alt is held.
            if (!leftClick) return MapTargetAction.Consume;
            if (sameTarget) return MapTargetAction.Clear;
            if (!hit) return MapTargetAction.Place;
            if (collapsedGroup) return MapTargetAction.Expand;
            return MapTargetAction.Select;
        }
    }
}
