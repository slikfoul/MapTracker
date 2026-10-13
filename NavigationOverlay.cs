using System;
using TMPro;
using UnityEngine;

namespace MapTracker
{
    internal sealed class NavigationOverlay : IDisposable
    {
        private TextMeshProUGUI text;
        private float nextUpdate;
        internal void Apply(Minimap map, MarkerRecord target, Func<MarkerRecord, string> name)
        {
            if (map == null || target == null || Player.m_localPlayer == null)
            { if (text != null) text.gameObject.SetActive(false); return; }
            bool large = map.m_mode == Minimap.MapMode.Large;
            var parent = large ? map.m_largeRoot.transform as RectTransform : map.m_smallRoot.transform.parent as RectTransform;
            if (parent == null) return;
            if (text != null && text.transform.parent != parent) text.transform.SetParent(parent, false);
            if (text == null)
            {
                text = NativeMapText.Create("MapTrackerNavigation", parent, map);
                text.fontSize = 15; text.color = new Color(1, 0.85f, 0.35f); text.richText = false; text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.TopRight; text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                text.rectTransform.pivot = new Vector2(1, 1); text.rectTransform.sizeDelta = new Vector2(310, 45);
            }
            text.gameObject.SetActive(true);
            var minimap = map.m_smallRoot.transform as RectTransform;
            if (large) text.rectTransform.anchoredPosition = new Vector2(parent.rect.xMax - 24, parent.rect.yMin + 70);
            else if (minimap != null) text.rectTransform.position = minimap.TransformPoint(new Vector3(minimap.rect.xMax, minimap.rect.yMin - 6, 0));
            if (Time.realtimeSinceStartup < nextUpdate) return;
            var origin = Player.m_localPlayer.transform.position;
            string caption = name(target) + "\n" + Mathf.RoundToInt(DiscoveryQuery.Distance(target, origin)) + " m · " + DiscoveryQuery.Direction(origin, target.position);
            if (text.text != caption) text.text = caption;
            nextUpdate = Time.realtimeSinceStartup + 0.2f;
        }
        public void Dispose() { if (text != null) UnityEngine.Object.Destroy(text.gameObject); text = null; nextUpdate = 0; }
    }
}
