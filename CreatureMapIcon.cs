using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed class CreatureMapIcon
    {
        private RectTransform root;
        private CreatureContour contour;
        private Image head, plainRelation;
        private Sprite previousSprite, previousDot;
        private float previousSize;
        private Color previousColor;
        private bool previousContrast;
        private int previousThickness;
        internal void Update(RectTransform parent, Sprite sprite, Sprite dot, float size, Color color, bool visible, bool contrast, int thickness)
        {
            if (root != null && root.parent != parent) { Object.Destroy(root.gameObject); root = null; }
            if (!visible || sprite == null)
            {
                if (root != null) root.gameObject.SetActive(false);
                return;
            }
            if (root == null)
            {
                var go = new GameObject("MapTrackerCreatureBadge", typeof(RectTransform));
                go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
                root = (RectTransform)go.transform; Center(root); root.SetAsFirstSibling();
                head = CreateImage("Head", root); head.preserveAspect = true;
                contour = head.gameObject.AddComponent<CreatureContour>();
                plainRelation = CreateImage("PlainRelationshipDot", root);
                previousSprite = null;
            }
            if (root.gameObject.activeSelf && previousSprite == sprite && previousDot == dot && previousSize == size &&
                previousColor == color && previousContrast == contrast && previousThickness == thickness) return;
            previousSprite = sprite; previousDot = dot; previousSize = size; previousColor = color; previousContrast = contrast;
            previousThickness = thickness;
            root.gameObject.SetActive(true);
            root.anchoredPosition = Vector2.zero; root.sizeDelta = new Vector2(size, size);
            contour.enabled = contrast;
            contour.Thickness = thickness;
            head.sprite = sprite; float headSize = size;
            head.rectTransform.sizeDelta = new Vector2(headSize, headSize);
            plainRelation.gameObject.SetActive(true); plainRelation.sprite = dot; plainRelation.color = color;
            plainRelation.rectTransform.anchoredPosition = new Vector2(size * 0.4f, -size * 0.4f);
            plainRelation.rectTransform.sizeDelta = new Vector2(4, 4);
        }
        private static Image CreateImage(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>(); image.raycastTarget = false;
            Center(image.rectTransform);
            return image;
        }
        private static void Center(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }
    }
}
