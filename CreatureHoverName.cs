using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapTracker
{
    internal sealed class CreatureHoverName : IDisposable
    {
        private TextMeshProUGUI text;
        internal void Apply(IEnumerable<MarkerRecord> records, Minimap map, Plugin host, Func<MarkerRecord, string> name)
        {
            if (text != null) text.gameObject.SetActive(false);
            if (map == null || map.m_mode != Minimap.MapMode.Large || !host.Enabled.Value || !host.LargeMap.Value || host.Window.Open) return;
            if (!host.HideLargeMapNames.Value && MapLabelZoom.Visible(map.LargeZoom, map.m_minZoom, map.m_showNamesZoom)) return;
            var canvas = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 pointer = ZInput.pointerPosition;
            if (!RectTransformUtility.RectangleContainsScreenPoint(map.m_mapImageLarge.rectTransform, pointer, camera)) return;
            MarkerRecord closest = null; float best = float.MaxValue;
            foreach (var record in records)
            {
                if (record.entry?.creature != true || record.pin?.m_uiElement == null || !record.pin.m_uiElement.gameObject.activeInHierarchy) continue;
                float distance = Vector2.Distance(pointer, RectTransformUtility.WorldToScreenPoint(camera, record.pin.m_uiElement.position));
                float reach = Mathf.Max(16, Mathf.Max(record.pin.m_uiElement.rect.width, record.pin.m_uiElement.rect.height) * 0.5f) *
                    (canvas != null ? canvas.scaleFactor : 1);
                if (distance <= reach && distance < best) { best = distance; closest = record; }
            }
            if (closest == null) return;
            if (text != null && text.transform.parent != map.m_largeRoot.transform) Object.Destroy(text.gameObject);
            if (text == null)
            {
                text = NativeMapText.Create("MapTrackerCreatureHoverName", map.m_largeRoot.transform, map);
                text.fontSize = 16; text.color = Color.white; text.richText = false; text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.Midline; text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                text.rectTransform.pivot = new Vector2(0.5f, 0);
                text.rectTransform.sizeDelta = new Vector2(280, 24);
            }
            text.text = name(closest) + (closest.group != null ? " ×" + closest.group.Count : "");
            var parent = map.m_largeRoot.transform as RectTransform;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, closest.pin.m_uiElement.position);
            screen.y += (closest.pin.m_uiElement.rect.height * 0.5f + 5) * (canvas != null ? canvas.scaleFactor : 1);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out var local))
                text.rectTransform.anchoredPosition = local;
            text.gameObject.SetActive(true);
        }
        public void Dispose() { if (text != null) Object.Destroy(text.gameObject); text = null; }
    }
}
