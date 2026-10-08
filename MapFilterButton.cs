using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    /// <summary>A native canvas button positioned relative to the game's pin-filter controls.</summary>
    internal sealed class MapFilterButton
    {
        private readonly FilterWindow window;
        private Minimap map;
        private GameObject root;
        private RectTransform rect;
        private readonly Vector3[] corners = new Vector3[4];
        internal MapFilterButton(FilterWindow filterWindow) { window = filterWindow; }

        internal void Tick()
        {
            Minimap current = Minimap.instance;
            if (current != map)
            {
                Dispose(); map = current;
            }
            if (map == null || map.m_largeRoot == null || map.m_selectedIconDeath == null) return;
            if (root == null) Create();
            if (root == null) return;
            root.SetActive(map.m_mode == Minimap.MapMode.Large);
            if (root.activeSelf) Position();
        }

        private void Create()
        {
            Image original = map.m_selectedIconDeath.transform.parent.GetComponent<Image>();
            if (original == null) return;
            root = new GameObject("MapTrackerFilters", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            root.transform.SetParent(map.m_largeRoot.transform, false);
            root.layer = original.gameObject.layer;
            rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            Image background = root.GetComponent<Image>();
            // The selection image's parent is the death icon itself, not the panel background.
            // Copying it would put a skull underneath our gear.
            Image panelBackground = original.transform.parent?.GetComponent<Image>();
            background.sprite = panelBackground != null ? panelBackground.sprite : null;
            background.type = panelBackground != null ? panelBackground.type : Image.Type.Simple;
            background.material = panelBackground != null ? panelBackground.material : null;
            background.color = panelBackground != null ? panelBackground.color : new Color(0.15f, 0.15f, 0.15f, 0.85f);
            background.raycastTarget = true;
            Button button = root.GetComponent<Button>();
            button.targetGraphic = background;
            Button originalButton = original.GetComponent<Button>();
            if (originalButton != null) button.colors = originalButton.colors;
            else
            {
                ColorBlock colors = button.colors;
                colors.highlightedColor = Color.white;
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
                button.colors = colors;
            }
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.onClick.AddListener(window.Toggle);
            var icon = new GameObject("Gear", typeof(RectTransform), typeof(CanvasRenderer), typeof(GearGraphic));
            icon.layer = root.layer;
            icon.transform.SetParent(root.transform, false);
            RectTransform iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = new Vector2(0.17f, 0.17f);
            iconRect.anchorMax = new Vector2(0.83f, 0.83f);
            iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
            GearGraphic gear = icon.GetComponent<GearGraphic>();
            gear.color = Color.white;
            gear.raycastTarget = false;
        }

        private Rect InParent(RectTransform source, Transform parent)
        {
            source.GetWorldCorners(corners);
            Vector3 first = parent.InverseTransformPoint(corners[0]);
            float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 point = parent.InverseTransformPoint(corners[i]);
                minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private void Position()
        {
            RectTransform original = map.m_selectedIconDeath.transform.parent as RectTransform;
            if (original == null) return;
            Rect control = InParent(original, rect.parent);
            float size = control.width;
            float top = control.yMax;
            if (original.parent is RectTransform filterPanel)
                top = Mathf.Max(top, InParent(filterPanel, rect.parent).yMax);
            if (map.m_selectedIconBoss?.transform.parent is RectTransform boss)
                top = Mathf.Max(top, InParent(boss, rect.parent).yMax);
            Vector2 center = new Vector2(control.center.x, top + size * 0.5f + 8f);
            if (map.m_mapImageLarge != null)
            {
                Rect area = InParent(map.m_mapImageLarge.rectTransform, rect.parent);
                float margin = size * 0.6f;
                if (area.width > margin * 2 && area.height > margin * 2)
                {
                    center.x = Mathf.Clamp(center.x, area.xMin + margin, area.xMax - margin);
                    center.y = Mathf.Clamp(center.y, area.yMin + margin, area.yMax - margin);
                }
            }
            rect.sizeDelta = new Vector2(size, size);
            rect.localPosition = new Vector3(center.x, center.y, 0);
        }

        internal bool ContainsPointer()
        {
            if (root == null || !root.activeInHierarchy) return false;
            Canvas canvas = root.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, ZInput.pointerPosition, camera);
        }
        internal void Dispose()
        {
            if (root != null) Object.Destroy(root);
            root = null; rect = null; map = null;
        }
    }

    /// <summary>White eight-tooth gear, built as vector geometry to stay crisp at the game's UI scale.</summary>
    public sealed class GearGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect area = GetPixelAdjustedRect();
            Vector2 center = area.center;
            float radius = Mathf.Min(area.width, area.height) * 0.5f;
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i + 0.5f) * Mathf.PI * 2f / segments;
                float rim = i % 4 == 1 || i % 4 == 2 ? radius : radius * 0.78f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(center + direction * rim, color, Vector2.zero);
                mesh.AddVert(center + direction * radius * 0.35f, color, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                mesh.AddTriangle(i * 2, next * 2, next * 2 + 1);
                mesh.AddTriangle(i * 2, next * 2 + 1, i * 2 + 1);
            }
        }
    }
}
