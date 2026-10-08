using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;

namespace MapTracker
{
    internal sealed class LargeMapLabelLayout
    {
        private readonly List<LabelBox> boxes = new List<LabelBox>();
        private readonly List<MarkerRecord> candidates = new List<MarkerRecord>();
        private readonly LabelLayout.Placement placement = new LabelLayout.Placement();
        private readonly ConditionalWeakTable<TMP_Text, Measure> measurements = new ConditionalWeakTable<TMP_Text, Measure>();
        private sealed class Measure { internal string Name; internal float FontSize; internal Vector2 Size; }
        internal void Apply(IEnumerable<MarkerRecord> records, Minimap map, Plugin host)
        {
            if (map.m_mode != Minimap.MapMode.Large || !host.LargeMap.Value || !host.Enabled.Value || host.HideLargeMapNames.Value ||
                !MapLabelZoom.Visible(map.LargeZoom, map.m_minZoom, map.m_showNamesZoom)) return;
            var canvas = map.m_mapImageLarge.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float scale = canvas != null ? canvas.scaleFactor : 1;
            Vector3 origin = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            candidates.Clear();
            foreach (var r in records)
                if (r.pin?.m_uiElement != null && r.pin.m_uiElement.gameObject.activeInHierarchy &&
                r.pin.m_NamePinData?.PinNameText != null && r.pin.m_NamePinData.PinNameGameObject != null &&
                RectTransformUtility.RectangleContainsScreenPoint(map.m_mapImageLarge.rectTransform,
                    RectTransformUtility.WorldToScreenPoint(camera, r.pin.m_uiElement.position), camera)) candidates.Add(r);
            candidates.Sort((a, b) =>
            {
                float ax = a.pin.m_pos.x - origin.x, az = a.pin.m_pos.z - origin.z;
                float bx = b.pin.m_pos.x - origin.x, bz = b.pin.m_pos.z - origin.z;
                int order = (ax * ax + az * az).CompareTo(bx * bx + bz * bz);
                return order != 0 ? order : StringComparer.Ordinal.Compare(a.id, b.id);
            });
            boxes.Clear();
            foreach (var record in candidates)
            {
                record.pin.m_NamePinData.PinNameRectTransform.anchoredPosition = record.pin.m_uiElement.anchoredPosition;
                TMP_Text text = record.pin.m_NamePinData.PinNameText;
                if (record.entry.creature) text.fontSize = 16;
                // Native labels use a much wider layout rect than their actual text.
                // Measure the glyph bounds so only real collisions hide names.
                var measured = measurements.GetValue(text, _ => new Measure());
                if (measured.Name != text.text || measured.FontSize != text.fontSize)
                { measured.Name = text.text; measured.FontSize = text.fontSize; measured.Size = text.GetPreferredValues(text.text); }
                Vector2 size = measured.Size;
                RectTransform rect = text.rectTransform;
                Vector3 center = rect.rect.center;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(center - new Vector3(size.x * 0.5f, size.y * 0.5f)));
                Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(center + new Vector3(size.x * 0.5f, size.y * 0.5f)));
                boxes.Add(new LabelBox(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)));
            }
            LabelBox[] placed = placement.Place(boxes, 4 * scale);
            for (int i = 0; i < candidates.Count; i++)
            {
                var record = candidates[i]; record.pin.m_NamePinData.PinNameGameObject.SetActive(true);
                Vector2 offset = new Vector2(placed[i].X - boxes[i].X, placed[i].Y - boxes[i].Y);
                LabelLeader.Move(record.pin.m_NamePinData.PinNameRectTransform, camera, offset);
            }
        }
    }
}
