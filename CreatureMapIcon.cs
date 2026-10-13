using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed class CreatureMapIcon
    {
        private RectTransform root;
        private CreatureContour contour;
        private Image head;
        private Sprite previousSprite;
        private float previousSize;
        private bool previousContrast;
        private int previousThickness;
        internal void Update(RectTransform parent, Sprite sprite, float size, bool visible, bool contrast, int thickness)
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
                previousSprite = null;
            }
            if (root.gameObject.activeSelf && previousSprite == sprite && previousSize == size &&
                previousContrast == contrast && previousThickness == thickness) return;
            previousSprite = sprite; previousSize = size; previousContrast = contrast;
            previousThickness = thickness;
            root.gameObject.SetActive(true);
            root.anchoredPosition = Vector2.zero; root.sizeDelta = new Vector2(size, size);
            contour.enabled = contrast;
            contour.Thickness = thickness;
            head.sprite = sprite; float headSize = size;
            head.rectTransform.sizeDelta = new Vector2(headSize, headSize);
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
