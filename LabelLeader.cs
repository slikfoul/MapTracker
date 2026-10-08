using UnityEngine;

namespace MapTracker
{
    internal static class LabelLeader
    {
        internal static void Move(RectTransform label, Camera camera, Vector2 offset)
        {
            if (offset.sqrMagnitude < 0.01f) return;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, label.position);
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(label, screen + offset, camera, out var position)) label.position = position;
        }
    }
}