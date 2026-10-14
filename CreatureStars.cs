using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed class CreatureStars
    {
        private RectTransform root;
        private readonly RectTransform[] stars = new RectTransform[2];
        private int previousCount = -1;
        private float previousSize;
        internal void Update(RectTransform parent, int count, float pointSize)
        {
            count = Mathf.Clamp(count, 0, 2);
            if (root != null && root.parent != parent) { Object.Destroy(root.gameObject); root = null; }
            if (count == 0)
            {
                if (root != null) root.gameObject.SetActive(false);
                return;
            }
            if (root == null)
            {
                var container = new GameObject("MapTrackerStars", typeof(RectTransform));
                container.layer = parent.gameObject.layer;
                container.transform.SetParent(parent, false);
                root = (RectTransform)container.transform;
                previousCount = -1;
                root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
                for (int i = 0; i < stars.Length; i++)
                {
                    var icon = new GameObject("Star" + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(StarGraphic));
                    icon.layer = parent.gameObject.layer;
                    icon.transform.SetParent(root, false);
                    stars[i] = (RectTransform)icon.transform;
                    stars[i].anchorMin = stars[i].anchorMax = stars[i].pivot = new Vector2(0.5f, 0.5f);
                    var graphic = icon.GetComponent<StarGraphic>();
                    graphic.color = new Color(1f, 0.83f, 0.25f);
                    graphic.raycastTarget = false;
                }
            }
            if (root.gameObject.activeSelf && previousCount == count && previousSize == pointSize) return;
            previousCount = count; previousSize = pointSize;
            root.gameObject.SetActive(true);
            float size = Mathf.Clamp(pointSize * 0.65f, 4f, 8f);
            root.anchoredPosition = new Vector2(0, pointSize * 0.5f + size * 0.5f + 1.5f);
            root.sizeDelta = new Vector2(count * (size + 1.5f), size);
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].gameObject.SetActive(i < count);
                stars[i].sizeDelta = new Vector2(size, size);
                stars[i].anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * (size + 1.5f), 0);
            }
        }
    }

    public sealed class StarGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect area = GetPixelAdjustedRect();
            Vector2 center = area.center;
            float radius = Mathf.Min(area.width, area.height) * 0.5f;
            mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i < 10; i++)
            {
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5;
                float length = i % 2 == 0 ? radius : radius * 0.45f;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length, color, Vector2.zero);
            }
            for (int i = 0; i < 10; i++) mesh.AddTriangle(0, i + 1, (i + 1) % 10 + 1);
        }
    }
}
