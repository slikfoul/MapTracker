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
        private readonly List<LabelBox> alternatives = new List<LabelBox>();
        private readonly List<MarkerRecord> candidates = new List<MarkerRecord>();
        private readonly LabelLayout.TwoAnchorSelection selection = new LabelLayout.TwoAnchorSelection();
        private readonly ConditionalWeakTable<TMP_Text, Measure> measurements = new ConditionalWeakTable<TMP_Text, Measure>();
        private sealed class Measure { internal string Name; internal float FontSize; internal Vector2 Size; }
        internal void Apply(IEnumerable<MarkerRecord> records, Minimap map, Plugin host)
        {
            foreach (var record in records) record.largeNameShown = false;
            if (map.m_mode != Minimap.MapMode.Large || !host.LargeMap.Value || !host.Enabled.Value || host.HideLargeMapNames.Value ||
                !MapLabelZoom.Visible(map.LargeZoom, map.m_minZoom, map.m_showNamesZoom)) return;
            var canvas = map.m_mapImageLarge.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float scale = canvas != null ? canvas.scaleFactor : 1;
            Vector3 origin = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            candidates.Clear();
            foreach (var r in records)
            {
                if (!r.nameGroupFollower && r.pin?.m_uiElement != null && r.pin.m_uiElement.gameObject.activeInHierarchy &&
                r.pin.m_NamePinData?.PinNameText != null && r.pin.m_NamePinData.PinNameGameObject != null &&
                RectTransformUtility.RectangleContainsScreenPoint(map.m_mapImageLarge.rectTransform,
                    RectTransformUtility.WorldToScreenPoint(camera, r.pin.m_uiElement.position), camera)) candidates.Add(r);
            }
            // Portals get a persistent caption at close zoom, before ordinary crowd labels.
            // Navigation targets retain their existing priority.
            candidates.Sort((a, b) => NameOrder(host.Tracking.Discoveries.IsNavigationTarget(a), a.entry.category == "Portals",
                Tracking.NearbyNameDistance(a, origin), a.id, host.Tracking.Discoveries.IsNavigationTarget(b), b.entry.category == "Portals",
                Tracking.NearbyNameDistance(b, origin), b.id));
            boxes.Clear(); alternatives.Clear();
            var viewport = MarkerPresentation.MapArea(map.m_mapImageLarge.rectTransform, camera);
            for (int i = 0; i < candidates.Count; i++)
            {
                var record = candidates[i];
                record.pin.m_NamePinData.PinNameRectTransform.anchoredPosition = record.pin.m_uiElement.anchoredPosition;
                TMP_Text text = record.pin.m_NamePinData.PinNameText;
                bool styleChanged = NameTextStyle.Apply(text, record.entry.creature ? host.CreatureNameSize.Value : host.ObjectNameSize.Value);
                // Native labels use a much wider layout rect than their actual text.
                // Measure the glyph bounds so only real collisions hide names.
                var measured = measurements.GetValue(text, _ => new Measure());
                if (measured.Name != text.text || measured.FontSize != text.fontSize || styleChanged)
                { measured.Name = text.text; measured.FontSize = text.fontSize; measured.Size = text.GetPreferredValues(text.text); }
                Vector2 size = measured.Size;
                RectTransform rect = text.rectTransform;
                Vector3 center = rect.rect.center;
                if (record.nameGroupMembers != null)
                {
                    Vector2 current = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(center));
                    LabelLeader.Move(record.pin.m_NamePinData.PinNameRectTransform, camera, record.nameGroupScreen - current);
                }
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(center - new Vector3(size.x * 0.5f, size.y * 0.5f)));
                Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(center + new Vector3(size.x * 0.5f, size.y * 0.5f)));
                var bounds = new LabelBox(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
                var icon = MarkerPresentation.MapArea(record.pin.m_uiElement, camera);
                var above = new LabelBox(bounds.X, icon.Y + icon.Height + 5 * scale, bounds.Width, bounds.Height);
                // Preserve grouped-name centres; individual names may use the upper anchor.
                if (record.nameGroupMembers != null) above = bounds;
                bool portal = record.entry.category == "Portals";
                boxes.Add(portal ? above : bounds);
                alternatives.Add(portal ? bounds : above);
            }
            int[] shown = selection.Choose(boxes, alternatives, viewport, MarkerPresentation.PlayerArea(map, true, camera, scale), 4 * scale);
            for (int i = 0; i < candidates.Count; i++)
            {
                var record = candidates[i]; record.largeNameShown = shown[i] >= 0;
                if (!record.largeNameShown) continue;
                var chosen = shown[i] == 0 ? boxes[i] : alternatives[i];
                var text = record.pin.m_NamePinData.PinNameText;
                Vector2 current = RectTransformUtility.WorldToScreenPoint(camera, text.rectTransform.TransformPoint(text.rectTransform.rect.center));
                LabelLeader.Move(record.pin.m_NamePinData.PinNameRectTransform, camera,
                    new Vector2(chosen.X + chosen.Width * 0.5f, chosen.Y + chosen.Height * 0.5f) - current);
            }
        }
        internal static int NameOrder(bool aTarget, bool aPortal, float aDistance, string aId,
            bool bTarget, bool bPortal, float bDistance, string bId)
        {
            int order = bTarget.CompareTo(aTarget);
            if (order == 0) order = bPortal.CompareTo(aPortal);
            return order != 0 ? order : MarkerPresentation.NameOrder(aTarget, aDistance, aId, bTarget, bDistance, bId);
        }
    }
}
