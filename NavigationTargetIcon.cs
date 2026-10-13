using System;
using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed class NavigationTargetIcon : IDisposable
    {
        private RectTransform root;
        private Image image;
        private DungeonGraphic dungeon;
        private int dungeonKind = -1;
        internal static bool HasIcon(MarkerRecord target) => target?.entry != null && target.entry.category != "Navigation";
        internal void Apply(RectTransform parent, Minimap map, MarkerRecord target, Vector2 position, float size)
        {
            if (!HasIcon(target) || map == null || parent == null)
            { if (root != null) root.gameObject.SetActive(false); return; }
            if (root != null && root.parent != parent) Dispose();
            if (root == null)
            {
                var go = new GameObject("NavigationTargetIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.layer = parent.gameObject.layer; root = (RectTransform)go.transform; root.SetParent(parent, false);
                root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
                image = go.GetComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;
                var entrance = new GameObject("Dungeon", typeof(RectTransform), typeof(CanvasRenderer), typeof(DungeonGraphic));
                entrance.layer = go.layer; entrance.transform.SetParent(root, false);
                var rect = (RectTransform)entrance.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                dungeon = entrance.GetComponent<DungeonGraphic>(); dungeon.raycastTarget = false;
                dungeon.color = Color.white;
            }
            root.gameObject.SetActive(true);
            Vector2 anchored = position - parent.rect.center;
            if (root.anchoredPosition != anchored) root.anchoredPosition = anchored;
            var dimensions = new Vector2(size, size);
            if (root.sizeDelta != dimensions) root.sizeDelta = dimensions;
            bool isDungeon = target.entry?.dungeon == true;
            image.enabled = !isDungeon; dungeon.gameObject.SetActive(isDungeon);
            if (isDungeon)
            {
                int kind = DungeonIcon.Kind(target.entry.prefab);
                if (kind != dungeonKind) { dungeonKind = kind; dungeon.Kind = kind; dungeon.SetVerticesDirty(); }
            }
            else
            {
                var sprite = target.entry?.Icon;
                if (sprite == null && target.entry?.creature == true)
                    sprite = map.m_icons.Find(icon => icon.m_name == Minimap.PinType.Death).m_icon;
                if (sprite == null) sprite = target.pin?.m_icon;
                if (sprite == null)
                {
                    var fallback = target.entry?.category == "Players" ? Minimap.PinType.Player : Minimap.PinType.Icon3;
                    sprite = map.m_icons.Find(icon => icon.m_name == fallback).m_icon;
                }
                if (image.sprite != sprite) image.sprite = sprite;
                image.enabled = sprite != null;
            }
        }
        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null; image = null; dungeon = null; dungeonKind = -1;
        }
    }
}
