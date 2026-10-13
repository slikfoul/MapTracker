using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed class NavigationHud : IDisposable
    {
        private RectTransform root, arrow;
        private TextMeshProUGUI text;
        private float nextCaption;
        private MarkerRecord previousTarget;
        private readonly NavigationTargetIcon targetIcon = new NavigationTargetIcon();

        internal void Apply(Minimap map, MarkerRecord target, Func<MarkerRecord, string> name, bool shared = false, bool paired = false)
        {
            var player = Player.m_localPlayer;
            var hud = Hud.instance;
            bool visible = target != null && player != null && map != null && hud != null && hud.IsVisible() &&
                map.m_mode != Minimap.MapMode.Large && !Menu.IsVisible() && !InventoryGui.IsVisible() &&
                !global::Console.IsVisible() && !player.IsTeleporting() && !player.InCutscene() && !player.IsDead();
            if (!visible)
            { if (root != null) root.gameObject.SetActive(false); return; }
            var parent = hud.m_rootObject.transform as RectTransform;
            if (parent == null) return;
            if (root != null && root.parent != parent) Dispose();
            if (root == null) Create(parent, map);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            root.anchoredPosition = new Vector2(paired ? (shared ? 175 : -175) : 0, -75);
            Color tint = shared ? new Color(0.35f, 0.85f, 1) : new Color(1, 0.85f, 0.35f);
            arrow.GetComponent<NavigationArrowGraphic>().color = tint;
            text.color = tint;

            var origin = player.transform.position;
            var camera = Utils.GetMainCamera();
            var cameraForward = camera != null ? camera.transform.forward : player.transform.forward;
            var playerForward = player.transform.forward;
            float heading = NavigationHeading.Relative(target.position.x - origin.x, target.position.z - origin.z,
                cameraForward.x, cameraForward.z, playerForward.x, playerForward.z);
            arrow.localRotation = Quaternion.Euler(0, 0, -heading);
            if (previousTarget == target && Time.realtimeSinceStartup < nextCaption) return;
            previousTarget = target;
            string title = name(target);
            text.rectTransform.anchoredPosition = new Vector2(NavigationTargetIcon.HasIcon(target) ? 14 : 0, -45);
            float titleWidth = Mathf.Min(290, text.GetPreferredValues(title).x);
            targetIcon.Apply(root, map, target, new Vector2(14 - titleWidth * 0.5f - 22, -55), 30);
            string caption = title + "\n" + Mathf.RoundToInt(DiscoveryQuery.Distance(target, origin)) + " m";
            if (text.text != caption) text.text = caption;
            nextCaption = Time.realtimeSinceStartup + 0.2f;
        }

        private void Create(RectTransform parent, Minimap map)
        {
            var container = new GameObject("MapTrackerNavigationHud", typeof(RectTransform));
            container.layer = parent.gameObject.layer;
            root = (RectTransform)container.transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1);
            root.pivot = new Vector2(0.5f, 1);
            root.sizeDelta = new Vector2(340, 100);
            root.anchoredPosition = new Vector2(0, -75);

            var pointer = new GameObject("Direction", typeof(RectTransform), typeof(CanvasRenderer));
            pointer.layer = container.layer;
            arrow = (RectTransform)pointer.transform;
            arrow.SetParent(root, false);
            arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 1);
            arrow.sizeDelta = new Vector2(32, 36);
            arrow.anchoredPosition = new Vector2(0, -20);
            var graphic = pointer.AddComponent<NavigationArrowGraphic>();
            graphic.color = new Color(1, 0.85f, 0.35f);
            graphic.raycastTarget = false;

            text = NativeMapText.Create("Destination", root, map);
            text.fontSize = 16; text.color = graphic.color;
            text.richText = false; text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Top;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 1);
            text.rectTransform.pivot = new Vector2(0.5f, 1);
            text.rectTransform.sizeDelta = new Vector2(290, 50);
            text.rectTransform.anchoredPosition = new Vector2(14, -45);
        }

        public void Dispose()
        {
            targetIcon.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null; arrow = null; text = null; previousTarget = null; nextCaption = 0;
        }
    }

    // Native UI mesh: no texture assets, font glyphs or per-frame mesh rebuilds.
    internal sealed class NavigationArrowGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Shape(mesh, 0, 16, -11, -13, -6, new Color(0.08f, 0.06f, 0.03f, 1));
            Shape(mesh, 4, 12, -7, -8, -3, color);
        }
        private static void Shape(VertexHelper mesh, int index, float tip, float sideX, float sideY, float notchY, Color tint)
        {
            mesh.AddVert(new Vector3(0, tip, 0), tint, Vector2.zero);
            mesh.AddVert(new Vector3(sideX, sideY, 0), tint, Vector2.zero);
            mesh.AddVert(new Vector3(0, notchY, 0), tint, Vector2.zero);
            mesh.AddVert(new Vector3(-sideX, sideY, 0), tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
