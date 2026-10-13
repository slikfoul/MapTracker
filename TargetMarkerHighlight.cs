using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal static class TargetHighlight
    {
        internal static readonly Color Personal = new Color(1f, 0.8f, 0.25f), Shared = new Color(0.35f, 0.85f, 1);
        internal static Color Tint(bool personal, bool shared) => personal && shared ? Color.Lerp(Personal, Shared, 0.5f) :
            shared ? Shared : Personal;
    }
    internal sealed class TargetMarkerHighlight
    {
        private TargetSelectionGraphic graphic;
        internal void Apply(RectTransform parent, bool personal, bool shared)
        {
            if (graphic != null && graphic.transform.parent != parent) { Object.Destroy(graphic.gameObject); graphic = null; }
            if (!personal && !shared) { if (graphic != null) graphic.gameObject.SetActive(false); return; }
            if (graphic == null)
            {
                var go = new GameObject("MapTrackerSelectedTarget", typeof(RectTransform), typeof(CanvasRenderer), typeof(TargetSelectionGraphic));
                go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
                var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(-4, -4); rect.offsetMax = new Vector2(4, 4);
                graphic = go.GetComponent<TargetSelectionGraphic>(); graphic.raycastTarget = false;
            }
            graphic.gameObject.SetActive(true); graphic.color = TargetHighlight.Tint(personal, shared);
        }
    }
    internal sealed class TargetSelectionGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var r = GetPixelAdjustedRect();
            float arm = Mathf.Clamp(Mathf.Min(r.width, r.height) * 0.28f, 4, 10);
            Corner(mesh, r.xMin, r.yMin, 1, 1, arm); Corner(mesh, r.xMax, r.yMin, -1, 1, arm);
            Corner(mesh, r.xMin, r.yMax, 1, -1, arm); Corner(mesh, r.xMax, r.yMax, -1, -1, arm);
        }
        private void Corner(VertexHelper mesh, float x, float y, float dx, float dy, float arm)
        {
            Bar(mesh, x, y, x + dx * arm, y + dy * 2); Bar(mesh, x, y, x + dx * 2, y + dy * arm);
        }
        private void Bar(VertexHelper mesh, float x1, float y1, float x2, float y2)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x1, y1), color, Vector2.zero); mesh.AddVert(new Vector3(x2, y1), color, Vector2.zero);
            mesh.AddVert(new Vector3(x2, y2), color, Vector2.zero); mesh.AddVert(new Vector3(x1, y2), color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
