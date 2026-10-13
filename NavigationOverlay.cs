using System;
using TMPro;
using UnityEngine;

namespace MapTracker
{
    internal sealed class NavigationOverlay : IDisposable
    {
        private TextMeshProUGUI text;
        private float nextUpdate;
        private readonly NavigationTargetIcon targetIcon = new NavigationTargetIcon();
        internal void Apply(Minimap map, MarkerRecord target, Func<MarkerRecord, string> name, bool shared = false, bool paired = false)
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
                text.rectTransform.pivot = new Vector2(1, 1); text.rectTransform.sizeDelta = new Vector2(275, 45);
            }
            text.gameObject.SetActive(true);
            text.alignment = large ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;
            text.rectTransform.pivot = new Vector2(large ? 0 : 1, 1);
            text.color = shared ? new Color(0.35f, 0.85f, 1) : new Color(1, 0.85f, 0.35f);
            float offset = shared && paired ? 50 : 0;
            var minimap = map.m_smallRoot.transform as RectTransform;
            if (large) text.rectTransform.anchoredPosition = new Vector2(parent.rect.xMin + 24 + (NavigationTargetIcon.HasIcon(target) ? 36 : 0), parent.rect.yMin + 70 + offset) - parent.rect.center;
            else if (minimap != null) text.rectTransform.position = minimap.TransformPoint(new Vector3(minimap.rect.xMax, minimap.rect.yMin - 6 - offset, 0));
            if (Time.realtimeSinceStartup < nextUpdate) return;
            string title = name(target);
            float titleWidth = Mathf.Min(275, text.GetPreferredValues(title).x);
            targetIcon.Apply(text.rectTransform, map, target, new Vector2(large ? -22 : -titleWidth - 22, -9), 28);
            var origin = Player.m_localPlayer.transform.position;
            string caption = title + "\n" + Mathf.RoundToInt(DiscoveryQuery.Distance(target, origin)) + " m · " + DiscoveryQuery.Direction(origin, target.position);
            if (text.text != caption) text.text = caption;
            nextUpdate = Time.realtimeSinceStartup + 0.2f;
        }
        public void Dispose() { targetIcon.Dispose(); if (text != null) UnityEngine.Object.Destroy(text.gameObject); text = null; nextUpdate = 0; }
    }
}
