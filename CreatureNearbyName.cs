using System;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapTracker
{
    internal sealed class CreatureNearbyName : IDisposable
    {
        private TextMeshProUGUI text;
        private string previousName;
        private bool reportedEmpty;
        internal void Hide() { if (text != null) text.gameObject.SetActive(false); }
        internal void Show(RectTransform parent, Minimap map, string name)
        {
            RectTransform labelParent = map.m_pinRootSmall;
            if (text != null && text.transform.parent != labelParent) { Object.Destroy(text.gameObject); text = null; }
            if (text == null)
            {
                text = NativeMapText.Create("MapTrackerNearbyCreatureName", labelParent, map);
                previousName = null;
                reportedEmpty = false;
                text.fontSize = 13; text.color = Color.white; text.richText = false; text.raycastTarget = false;
                text.enableAutoSizing = false; text.isOrthographic = true;
                text.margin = Vector4.zero;
                if (text.font != null) text.fontSharedMaterial = text.font.material;
                text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                text.rectTransform.pivot = new Vector2(0, 0.5f);
                text.rectTransform.sizeDelta = new Vector2(120, 18);
            }
            var canvas = labelParent.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera,
                parent.TransformPoint(new Vector3(parent.rect.xMax + 3, parent.rect.center.y, 0)));
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(labelParent, screen, camera, out var point))
                text.rectTransform.localPosition = new Vector3(point.x, point.y, 0);
            bool nameChanged = previousName != name;
            if (nameChanged)
            {
                previousName = name; text.text = name;
                Vector2 measured = text.GetPreferredValues(name);
                text.rectTransform.sizeDelta = new Vector2(Mathf.Max(4, measured.x + 4), Mathf.Max(18, measured.y + 4));
            }
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
        public void Dispose() { if (text != null) Object.Destroy(text.gameObject); text = null; }
    }
}
