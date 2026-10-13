using UnityEngine;

namespace MapTracker
{
    internal static class MarkerPresentation
    {
        internal static int NameOrder(bool aTarget, float aDistance, string aId, bool bTarget, float bDistance, string bId)
        {
            int order = bTarget.CompareTo(aTarget);
            if (order == 0) order = aDistance.CompareTo(bDistance);
            return order != 0 ? order : System.StringComparer.Ordinal.Compare(aId, bId);
        }
        internal static LabelBox MapArea(RectTransform area, Camera camera)
        {
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.min));
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.max));
            return new LabelBox(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }
        internal static bool Nearby(bool previouslyShown, float distance, int maximum, int margin) =>
            distance <= maximum + (previouslyShown ? margin : 0);
        internal static bool OnMap(RectTransform area, Camera camera, RectTransform marker)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(area,
                RectTransformUtility.WorldToScreenPoint(camera, marker.position), camera);
        }
        internal static LabelBox PlayerArea(Minimap map, bool large, Camera camera, float scale)
        {
            var marker = large ? map.m_largeMarker : map.m_smallMarker;
            Vector2 point = marker != null ? RectTransformUtility.WorldToScreenPoint(camera, marker.position) : new Vector2(-10000, -10000);
            float size = 20 * scale;
            return new LabelBox(point.x - size, point.y - size, size * 2, size * 2);
        }
    }
}
