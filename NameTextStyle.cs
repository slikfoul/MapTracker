using TMPro;

namespace MapTracker
{
    internal static class NameTextStyle
    {
        internal static bool Apply(TMP_Text text, int size, bool ellipsis = false)
        {
            if (text == null) return false;
            var overflow = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            bool changed = text.enableAutoSizing || text.fontSize != size || text.fontSizeMin != size || text.fontSizeMax != size ||
                text.textWrappingMode != TextWrappingModes.NoWrap || text.overflowMode != overflow;
            if (!changed) return false;
            text.enableAutoSizing = false;
            text.fontSizeMin = text.fontSizeMax = size;
            text.fontSize = size;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = overflow;
            text.ForceMeshUpdate(true);
            return true;
        }
    }
}
