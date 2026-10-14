using System;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Collections.Generic;

namespace MapTracker
{
    internal sealed class CreatureNearbyName : IDisposable
    {
        private TextMeshProUGUI text;
        private readonly NativeNameVisibility visibility = new NativeNameVisibility();
        private string previousName;
        private int previousFontSize;
        private bool reportedEmpty;
        private static readonly Stack<TextMeshProUGUI> pool = new Stack<TextMeshProUGUI>();
        internal static void ClearPool() { while (pool.Count > 0) { var t = pool.Pop(); if (t != null) Object.Destroy(t.gameObject); } }
        internal void Hide() { visibility.Apply(text != null ? text.gameObject : null, false); }
        internal void SetVisible(bool show) { visibility.Apply(text != null ? text.gameObject : null, show); }
        internal void Show(RectTransform parent, Minimap map, string name, int fontSize, Vector2? center = null)
        {
            RectTransform labelParent = map.m_pinRootSmall;
            if (text != null && text.transform.parent != labelParent) Dispose();
            if (text == null)
            {
                while (pool.Count > 0 && text == null) text = pool.Pop();
                if (text == null) text = NativeMapText.Create("MapTrackerNearbyCreatureName", labelParent, map);
                else if (text.transform.parent != labelParent) text.transform.SetParent(labelParent, false);
                previousName = null;
                reportedEmpty = false;
                text.fontSize = 13; text.color = Color.white; text.richText = false; text.raycastTarget = false;
                text.enableAutoSizing = false; text.isOrthographic = true;
                text.margin = Vector4.zero;
                if (text.font != null) text.fontSharedMaterial = text.font.material;
                text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                text.rectTransform.pivot = new Vector2(0, 0.5f);
                text.rectTransform.sizeDelta = new Vector2(120, 18);
            }
            var canvas = labelParent.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            bool styleChanged = NameTextStyle.Apply(text, fontSize, true);
            bool nameChanged = previousName != name || previousFontSize != fontSize || styleChanged;
            if (nameChanged)
            {
                previousName = name; previousFontSize = fontSize; text.fontSize = fontSize; text.text = name;
                Vector2 measured = text.GetPreferredValues(name);
                text.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(measured.x + 4, 4, 120), Mathf.Max(18, measured.y + 4));
            }
            Vector2 screen = center.HasValue ? center.Value - new Vector2(text.rectTransform.rect.width * 0.5f * (canvas != null ? canvas.scaleFactor : 1), 0) :
                RectTransformUtility.WorldToScreenPoint(camera, parent.TransformPoint(new Vector3(parent.rect.xMax + 3, parent.rect.center.y, 0)));
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(labelParent, screen, camera, out var point))
                text.rectTransform.localPosition = new Vector3(point.x, point.y, 0);
            text.gameObject.SetActive(true);
            if (nameChanged)
            {
                text.ForceMeshUpdate();
                if (!reportedEmpty && !string.IsNullOrEmpty(name) && text.textInfo.characterCount == 0)
                {
                    reportedEmpty = true;
                    Plugin.Instance?.Report("Minimap creature name produced no glyphs: " + name + "; font=" + text.font?.name);
                }
            }
        }
        internal LabelBox Bounds(Camera camera)
        {
            var rect = text.rectTransform;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.min));
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.max));
            return new LabelBox(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }
        internal void Move(Camera camera, Vector2 offset) => LabelLeader.Move(text.rectTransform, camera, offset);
        internal string RenderState(Camera camera)
        {
            if (text == null) return "label=not-created";
            LabelBox bounds = Bounds(camera);
            return "labelActive=" + text.gameObject.activeInHierarchy + ", chars=" + text.textInfo.characterCount +
                ", meshVertices=" + (text.textInfo.meshInfo.Length > 0 ? text.textInfo.meshInfo[0].vertexCount : 0) +
                ", culled=" + text.canvasRenderer.cull + ", alpha=" + text.canvasRenderer.GetInheritedAlpha().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                ", font=" + text.font?.name + ", screen=" + bounds.X.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                "," + bounds.Y.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "," + bounds.Width.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                "," + bounds.Height.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }
        public void Dispose()
        {
            visibility.Dispose();
            if (text != null) { text.gameObject.SetActive(false); if (pool.Count < 128) pool.Push(text); else Object.Destroy(text.gameObject); }
            text = null; previousName = null;
        }
    }
}
