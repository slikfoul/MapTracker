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
        private RectTransform nativePanel;
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
            nativePanel = original.transform.parent as RectTransform;
            root = new GameObject("MapTrackerFilters", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            root.transform.SetParent(map.m_largeRoot.transform, false);
            root.layer = original.gameObject.layer;
            rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            Image background = root.GetComponent<Image>();
            // The selection image's parent is the death icon itself, not the panel background.
            // Copying it would put a skull underneath our gear.
            Image panelBackground = nativePanel != null ? nativePanel.GetComponent<Image>() : null;
            if (panelBackground == null && nativePanel != null)
                foreach (var image in nativePanel.GetComponentsInChildren<Image>(true))
                {
                    if (image.transform == original.transform || image.transform.IsChildOf(original.transform) ||
                        map.m_selectedIconBoss != null && image.transform.IsChildOf(map.m_selectedIconBoss.transform.parent)) continue;
                    Rect bounds = InParent(image.rectTransform, nativePanel);
                    if (bounds.width >= original.rectTransform.rect.width && bounds.height >= original.rectTransform.rect.height)
                    { panelBackground = image; break; }
                }
            background.sprite = panelBackground != null ? panelBackground.sprite : null;
            background.type = panelBackground != null ? panelBackground.type : Image.Type.Simple;
            background.material = panelBackground != null ? panelBackground.material : null;
            background.color = panelBackground != null ? panelBackground.color : new Color(0.15f, 0.15f, 0.15f, 0.85f);
            background.raycastTarget = true;
            Button button = root.GetComponent<Button>();
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
            iconRect.anchorMin = new Vector2(0.12f, 0.12f);
            iconRect.anchorMax = new Vector2(0.88f, 0.88f);
            iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
            GearGraphic gear = icon.GetComponent<GearGraphic>();
            gear.color = Color.white;
            gear.raycastTarget = false;
            // Native button colors tint the icon, not the shared dark panel.
            // Applying their alpha to our backing made it lighter than the controls below.
            button.targetGraphic = gear;
            background.canvasRenderer.SetColor(Color.white);
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
            float width = control.width, height = control.height;
            float centerX = control.center.x;
            float top = control.yMax;
            if (nativePanel != null)
            {
                Rect panelBounds = InParent(nativePanel, rect.parent);
                width = Mathf.Clamp(panelBounds.width, control.width, control.width * 1.6f);
                centerX = panelBounds.center.x;
                if (map.m_selectedIconBoss?.transform.parent?.parent == nativePanel)
                    height = Mathf.Clamp(panelBounds.height * 0.5f, control.height, control.height * 1.6f);
                top = Mathf.Max(top, panelBounds.yMax);
            }
            if (map.m_selectedIconBoss?.transform.parent is RectTransform boss)
                top = Mathf.Max(top, InParent(boss, rect.parent).yMax);
            Vector2 center = new Vector2(centerX, top + height * 0.5f + 8f);
            if (map.m_mapImageLarge != null)
            {
                Rect area = InParent(map.m_mapImageLarge.rectTransform, rect.parent);
                float margin = Mathf.Max(width, height) * 0.6f;
                if (area.width > margin * 2 && area.height > margin * 2)
                {
                    // The native control column already defines the horizontal
                    // alignment. Clamping it against map padding shifted only the gear.
                    center.y = Mathf.Clamp(center.y, area.yMin + margin, area.yMax - margin);
                }
            }
            rect.sizeDelta = new Vector2(width, height);
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
            root = null; rect = nativePanel = null; map = null;
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
