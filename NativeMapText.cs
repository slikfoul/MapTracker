using System.Linq;
using TMPro;
using UnityEngine;

namespace MapTracker
{
    internal static class NativeMapText
    {
        internal static TextMeshProUGUI Create(string name, Transform parent, Minimap map)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            root.SetActive(false);
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            var text = root.AddComponent<TextMeshProUGUI>();
            var original = map.m_biomeNameSmall?.font != null ? map.m_biomeNameSmall : map.m_biomeNameLarge;
            text.font = original?.font ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name.Contains("Averia"));
            if (original?.font != null) text.fontSharedMaterial = original.fontSharedMaterial;
            // Awake runs after the native font is assigned, avoiding an attempted
            // load of TextMeshPro's unbundled LiberationSans default asset.
            root.SetActive(true);
            return text;
        }
    }
}
