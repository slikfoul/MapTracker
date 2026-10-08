using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;

namespace MapTracker
{
    internal sealed class ClusterBadge
    {
        private TextMeshProUGUI text;
        internal void Hide() { if (text != null) text.gameObject.SetActive(false); }
        internal void Show(RectTransform parent, Minimap map, int count)
        {
            if (text != null && text.transform.parent != parent) UnityEngine.Object.Destroy(text.gameObject);
            if (text == null)
            {
                text = NativeMapText.Create("MapTrackerGroupCount", parent, map);
                text.fontSize = 16; text.color = new Color(1, 0.83f, 0.25f); text.alignment = TextAlignmentOptions.Midline;
                text.raycastTarget = false; text.richText = false;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(1, 1);
                text.rectTransform.sizeDelta = new Vector2(35, 20);
            }
            text.gameObject.SetActive(true); text.text = count.ToString();
        }
    }
    internal sealed class MarkerGrouping : IDisposable
    {
        private GameObject tooltip;
        private TextMeshProUGUI tooltipText;
        private Sprite tooltipFrame;
        private readonly List<MarkerRecord> eligible = new List<MarkerRecord>();
        private readonly List<ClusterPoint> points = new List<ClusterPoint>();
        private readonly HashSet<MarkerRecord> visible = new HashSet<MarkerRecord>();
        private List<List<MarkerRecord>> cachedGroups = new List<List<MarkerRecord>>();
        private float nextBuild, previousZoom;
        private int previousCount = -1, previousMinimum, previousDistance;
        private Minimap.MapMode previousMode;
        private string selectedGroup;
        internal void Toggle(MarkerRecord representative)
        {
            selectedGroup = selectedGroup == representative.id ? null : representative.id;
        }
        internal void Close() { selectedGroup = null; if (tooltip != null) tooltip.SetActive(false); }
        internal void Apply(IEnumerable<MarkerRecord> records, Minimap map, Plugin host, Func<MarkerRecord, string> name)
        {
            eligible.Clear(); visible.Clear();
            if (tooltip != null) tooltip.SetActive(false);
            if (map.m_mode != Minimap.MapMode.Large || host.Window.Open) selectedGroup = null;
            foreach (var record in records)
            {
                record.group = null; record.badge?.Hide();
                var pin = record.pin;
                if (pin?.m_uiElement == null || !pin.m_uiElement.gameObject.activeInHierarchy) continue;
                if (pin.m_NamePinData?.PinNameText != null)
                {
                    string label = name(record);
                    if (pin.m_NamePinData.PinNameText.text != label) pin.m_NamePinData.PinNameText.text = label;
                }
                if (!host.GroupMarkers.Value || (record.entry.creature && !host.GroupCreatures.Value)) continue;
                if (BuiltInFilterPresets.IsPlantMarker(record.entry) && !host.GroupPlants.Value) continue;
                eligible.Add(record);
                visible.Add(record);
            }
            if (!host.GroupMarkers.Value || eligible.Count < host.GroupThreshold.Value) { previousCount = -1; return; }
            float scale = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas.scaleFactor ?? 1;
            float zoom = map.m_mode == Minimap.MapMode.Large ? map.LargeZoom : map.SmallZoom;
            if (Time.realtimeSinceStartup >= nextBuild || previousCount != eligible.Count || previousMode != map.m_mode ||
                previousZoom != zoom || previousMinimum != host.GroupThreshold.Value || previousDistance != host.GroupDistance.Value)
            {
                points.Clear();
                foreach (var record in eligible)
                {
                    Vector2 position = RectTransformUtility.WorldToScreenPoint(null, record.pin.m_uiElement.position);
                    string kind = record.entry.category == "Portals" ? "Portals" : record.type;
                    kind += ":" + record.collected + ":" + record.visited + ":" + record.completed;
                    if (record.entry.creature) kind += ":" + (record.shared ? record.creatureLevel : record.character != null ? record.character.GetLevel() : 1) + ":" + record.pin.m_iconElement.color;
                    points.Add(new ClusterPoint { X = position.x, Y = position.y, Kind = kind });
                }
                cachedGroups = ClusterMath.Build(points, host.GroupDistance.Value * Mathf.Max(scale, 0.25f), host.GroupThreshold.Value)
                    .Select(indices => indices.Select(i => eligible[i]).ToList()).ToList();
                nextBuild = Time.realtimeSinceStartup + 0.2f;
                previousCount = eligible.Count; previousMode = map.m_mode; previousZoom = zoom; previousMinimum = host.GroupThreshold.Value;
                previousDistance = host.GroupDistance.Value;
            }
            bool selectionVisible = false;
            foreach (var members in cachedGroups)
            {
                if (members.Any(member => !visible.Contains(member))) { nextBuild = 0; continue; }
                MarkerRecord representative = members[0];
                Vector2 center = Vector2.zero;
                foreach (var member in members) center += member.pin.m_uiElement.anchoredPosition;
                center /= members.Count;
                representative.group = members;
                representative.pin.m_uiElement.anchoredPosition = center;
                if (representative.pin.m_NamePinData != null)
                {
                    if (representative.pin.m_NamePinData.PinNameRectTransform != null) representative.pin.m_NamePinData.PinNameRectTransform.anchoredPosition = center;
                    if (representative.pin.m_NamePinData.PinNameText != null)
                        representative.pin.m_NamePinData.PinNameText.text = representative.entry.Label + " ×" + members.Count;
                }
                if (representative.badge == null) representative.badge = new ClusterBadge();
                representative.badge.Show(representative.pin.m_uiElement, map, members.Count);
                foreach (var member in members.Skip(1))
                {
                    member.pin.m_uiElement.gameObject.SetActive(false);
                    if (member.pin.m_NamePinData?.PinNameGameObject != null) member.pin.m_NamePinData.PinNameGameObject.SetActive(false);
                }
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, representative.pin.m_uiElement.position);
                if (selectedGroup == representative.id && !host.Window.Open && map.m_mode == Minimap.MapMode.Large)
                {
                    selectionVisible = true;
                    ShowTooltip(map, members, screen, name);
                }
            }
            if (!selectionVisible) selectedGroup = null;
        }
        private void ShowTooltip(Minimap map, List<MarkerRecord> members, Vector2 screen, Func<MarkerRecord, string> name)
        {
            Transform parent = map.m_largeRoot.transform;
            if (tooltip == null)
            {
                tooltip = new GameObject("MapTrackerGroupTooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                tooltip.layer = parent.gameObject.layer; tooltip.transform.SetParent(parent, false);
                Image background = tooltip.GetComponent<Image>(); background.color = new Color(0.10f, 0.07f, 0.04f, 1); background.raycastTarget = false;
                var frame = new GameObject("WoodFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                frame.layer = tooltip.layer; frame.transform.SetParent(tooltip.transform, false);
                var frameRect = (RectTransform)frame.transform;
                frameRect.anchorMin = Vector2.zero; frameRect.anchorMax = Vector2.one; frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;
                foreach (var atlas in Resources.FindObjectsOfTypeAll<SpriteAtlas>().OrderByDescending(a => a.name == "UIAtlas"))
                {
                    tooltipFrame = atlas.GetSprite("woodpanel_trophys");
                    if (tooltipFrame != null) break;
                }
                Image wood = frame.GetComponent<Image>(); wood.sprite = tooltipFrame; wood.type = Image.Type.Sliced; wood.raycastTarget = false;
                wood.material = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "litpanel");
                tooltipText = NativeMapText.Create("Text", tooltip.transform, map);
                tooltipText.fontSize = 16; tooltipText.richText = false; tooltipText.raycastTarget = false;
                tooltipText.color = new Color(1, 0.86f, 0.63f);
                tooltipText.rectTransform.anchorMin = Vector2.zero; tooltipText.rectTransform.anchorMax = Vector2.one;
            tooltipText.rectTransform.offsetMin = new Vector2(18, 16); tooltipText.rectTransform.offsetMax = new Vector2(-18, -16);
            }
            tooltip.SetActive(true); tooltip.transform.SetAsLastSibling();
            var lines = members.Select(name).Distinct().Take(12).ToList();
            string text = Plugin.Text("Объектов: ", "Objects: ") + members.Count + "\n" + string.Join("\n", lines.ToArray());
            if (members.Count > 12) text += "\n…";
            tooltipText.text = text;
            RectTransform rect = (RectTransform)tooltip.transform;
            rect.sizeDelta = new Vector2(320, 24 * (lines.Count + 1) + 36);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            RectTransform parentRect = parent as RectTransform;
            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, null, out var local))
            {
                float halfWidth = rect.sizeDelta.x * 0.5f, halfHeight = rect.sizeDelta.y * 0.5f;
                rect.anchoredPosition = new Vector2(Mathf.Clamp(local.x + halfWidth + 20, parentRect.rect.xMin + halfWidth, parentRect.rect.xMax - halfWidth),
                    Mathf.Clamp(local.y, parentRect.rect.yMin + halfHeight, parentRect.rect.yMax - halfHeight));
            }
        }
        public void Dispose()
        {
            if (tooltip != null) UnityEngine.Object.Destroy(tooltip);
            if (tooltipFrame != null) UnityEngine.Object.Destroy(tooltipFrame);
            tooltipFrame = null;
            tooltip = null; tooltipText = null; cachedGroups.Clear(); previousCount = -1; nextBuild = 0;
            selectedGroup = null;
        }
    }
}
