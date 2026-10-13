using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed class DiscoveryNotificationHud : IDisposable
    {
        private RectTransform root;
        private TextMeshProUGUI title, caption;
        private MarkerRecord previous;
        private readonly NavigationTargetIcon icon = new NavigationTargetIcon();
        internal static bool CanShow(Minimap map, bool enabled)
        {
            var player = Player.m_localPlayer;
            return enabled && player != null && Hud.instance != null && Hud.instance.IsVisible() && map != null &&
                map.m_mode != Minimap.MapMode.Large && !Menu.IsVisible() && !InventoryGui.IsVisible() &&
                !global::Console.IsVisible() && !TextInput.IsVisible() && !player.IsDead() && !player.IsTeleporting() && !player.InCutscene();
        }
        internal void Apply(Minimap map, MarkerRecord item, bool visible)
        {
            if (!visible || item == null)
            { if (root != null) root.gameObject.SetActive(false); return; }
            var parent = Hud.instance?.m_rootObject.transform as RectTransform;
            if (parent == null) return;
            if (root != null && root.parent != parent) Dispose();
            if (root == null) Create(parent, map);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            float width = Mathf.Min(420, Mathf.Max(220, parent.rect.width - 32));
            if (root.sizeDelta.x != width)
            {
                root.sizeDelta = new Vector2(width, 88);
                title.rectTransform.sizeDelta = new Vector2(width - 96, 24);
                caption.rectTransform.sizeDelta = new Vector2(width - 96, 42);
            }
            if (previous != item) { previous = item; caption.text = item.nameCache ?? ""; }
            icon.Apply(root, map, item, new Vector2(-width * 0.5f + 40, -44), 44);
        }
        private void Create(RectTransform parent, Minimap map)
        {
            var panel = new GameObject("MapTrackerDiscoveryNotification", typeof(RectTransform), typeof(CanvasRenderer), typeof(DiscoveryNotificationPanel));
            panel.layer = parent.gameObject.layer; root = (RectTransform)panel.transform; root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1); root.pivot = new Vector2(0.5f, 1);
            root.sizeDelta = new Vector2(420, 88); root.anchoredPosition = new Vector2(0, -195);
            panel.GetComponent<DiscoveryNotificationPanel>().raycastTarget = false;
            title = NativeMapText.Create("NewDiscovery", root, map);
            caption = NativeMapText.Create("ObjectName", root, map);
            Configure(title, 17, new Color(1, 0.83f, 0.33f), -12, 24);
            Configure(caption, 20, Color.white, -36, 42);
            title.text = "New discovery";
        }
        private static void Configure(TextMeshProUGUI text, int size, Color tint, float y, float height)
        {
            text.fontSize = size; text.enableAutoSizing = false; text.color = tint;
            text.richText = false; text.raycastTarget = false; text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Ellipsis;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0, 1);
            text.rectTransform.anchoredPosition = new Vector2(80, y); text.rectTransform.sizeDelta = new Vector2(324, height);
        }
        public void Dispose()
        {
            icon.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null; title = caption = null; previous = null;
        }
    }
    internal sealed class DiscoveryNotificationPanel : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var bounds = rectTransform.rect;
            var gold = new Color(0.8f, 0.57f, 0.2f, 1);
            Quad(mesh, bounds.xMin, bounds.yMin, bounds.xMax, bounds.yMax, gold);
            Quad(mesh, bounds.xMin + 2, bounds.yMin + 2, bounds.xMax - 2, bounds.yMax - 2, new Color(0.055f, 0.04f, 0.025f, 0.96f));
            Quad(mesh, bounds.xMin + 2, bounds.yMin + 2, bounds.xMin + 6, bounds.yMax - 2, gold);
        }
        private static void Quad(VertexHelper mesh, float left, float bottom, float right, float top, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(left, bottom), tint, Vector2.zero); mesh.AddVert(new Vector3(left, top), tint, Vector2.zero);
            mesh.AddVert(new Vector3(right, top), tint, Vector2.zero); mesh.AddVert(new Vector3(right, bottom), tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
