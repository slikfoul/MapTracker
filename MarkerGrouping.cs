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
        private int previousCount = -1;
        internal void Hide() { if (text != null && text.gameObject.activeSelf) text.gameObject.SetActive(false); }
        internal void Show(RectTransform parent, Minimap map, int count)
        {
            if (text != null && text.transform.parent != parent) { UnityEngine.Object.Destroy(text.gameObject); text = null; previousCount = -1; }
            if (text == null)
            {
                text = NativeMapText.Create("MapTrackerGroupCount", parent, map);
                text.fontSize = 16; text.color = new Color(1, 0.83f, 0.25f); text.alignment = TextAlignmentOptions.Midline;
                text.raycastTarget = false; text.richText = false;
                text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                text.rectTransform.anchoredPosition = Vector2.zero;
                text.rectTransform.sizeDelta = new Vector2(60, 24);
            }
            if (text.transform.GetSiblingIndex() != text.transform.parent.childCount - 1) text.transform.SetAsLastSibling();
            if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
            if (previousCount != count) { previousCount = count; text.fontSize = count >= 1000 ? 13 : 16; text.text = count.ToString(); }
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
        private readonly HashSet<MarkerRecord> badgeOwners = new HashSet<MarkerRecord>();
        private List<List<MarkerRecord>> cachedGroups = new List<List<MarkerRecord>>();
        private float nextBuild, previousZoom;
        private float previousScale;
        private int previousCount = -1, previousMinimum, previousDistance;
        private Minimap.MapMode previousMode;
        private string selectedGroup;
        private List<MarkerRecord> tooltipMembers;
        private readonly GroupTooltipLayout.Placement tooltipPlacement = new GroupTooltipLayout.Placement();
        private int? tooltipGeometry;
        private readonly List<LabelBox> tooltipObstacles = new List<LabelBox>();
        private readonly Vector3[] tooltipCorners = new Vector3[4];
        internal void Invalidate() { nextBuild = 0; previousCount = -1; }
        internal void Toggle(MarkerRecord representative)
        {
            selectedGroup = selectedGroup == representative.id ? null : representative.id;
            tooltipPlacement.Reset();
            tooltipGeometry = null;
        }
        internal void Close() { selectedGroup = null; tooltipMembers = null; tooltipGeometry = null; tooltipPlacement.Reset(); if (tooltip != null) tooltip.SetActive(false); }
        internal bool IsExpanded(MarkerRecord record) => selectedGroup == record.id;
        internal bool ShowsDetails(MarkerRecord record) => tooltip != null && tooltip.activeInHierarchy && tooltipMembers != null && tooltipMembers.Contains(record);
        internal void Apply(IEnumerable<MarkerRecord> records, Minimap map, Plugin host, Func<MarkerRecord, string> name)
        {
            eligible.Clear(); visible.Clear(); badgeOwners.Clear();
            tooltipMembers = null;
            if (map.m_mode != Minimap.MapMode.Large || host.Window.Open) Close();
            foreach (var record in records)
            {
                record.group = null; record.groupHidden = false;
                var pin = record.pin;
                if (pin?.m_uiElement == null || !record.onMap) continue;
                if (record.favorite || host.Tracking.Discoveries.IsNavigationTarget(record) || !host.GroupMarkers.Value || (record.entry.creature && !host.GroupCreatures.Value)) continue;
                if (BuiltInFilterPresets.IsPlantMarker(record.entry) && !host.GroupPlants.Value) continue;
                eligible.Add(record);
                visible.Add(record);
            }
            if (!host.GroupMarkers.Value || eligible.Count < host.GroupThreshold.Value)
            { previousCount = -1; Close(); ApplyVisibility(records); return; }
            float scale = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas.scaleFactor ?? 1;
            var canvas = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float zoom = map.m_mode == Minimap.MapMode.Large ? map.LargeZoom : map.SmallZoom;
            if (Time.realtimeSinceStartup >= nextBuild || previousCount != eligible.Count || previousMode != map.m_mode ||
                previousZoom != zoom || previousScale != scale || previousMinimum != host.GroupThreshold.Value || previousDistance != host.GroupDistance.Value)
            {
                points.Clear();
                foreach (var record in eligible)
                {
                    Vector2 position = RectTransformUtility.WorldToScreenPoint(camera, record.pin.m_uiElement.position);
                    string kind = record.entry.category == "Portals" ? "Portals" : record.type;
                    kind += ":" + record.collected + ":" + record.visited + ":" + record.completed;
                    if (record.entry.creature) kind += ":" + (record.shared ? record.creatureLevel : record.character != null ? record.character.GetLevel() : 1) + ":" + record.pin.m_iconElement.color;
                    var rect = record.pin.m_uiElement;
                    Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.min));
                    Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.max));
                    bool icons = map.m_mode == Minimap.MapMode.Large ? host.CreatureIcons.Value : host.MinimapCreatureIcons.Value;
                    float outline = record.entry.creature && icons && host.ContrastCreatureIcons.Value &&
                        (map.m_mode != Minimap.MapMode.Large || MapLabelZoom.Visible(map.LargeZoom, map.m_minZoom, map.m_showNamesZoom))
                        ? host.CreatureOutlineThickness.Value * scale : 0;
                    points.Add(new ClusterPoint { X = position.x, Y = position.y, Kind = kind, WorldX = record.pin.m_pos.x, WorldZ = record.pin.m_pos.z,
                        HalfWidth = Mathf.Abs(max.x - min.x) * 0.5f + outline, HalfHeight = Mathf.Abs(max.y - min.y) * 0.5f + outline });
                }
                cachedGroups = ClusterMath.BuildOverlapping(points, 3 * Mathf.Max(scale, 0.25f), host.GroupThreshold.Value, host.GroupDistance.Value)
                    .Select(indices => indices.Select(i => eligible[i]).ToList()).ToList();
                nextBuild = Time.realtimeSinceStartup + 0.2f;
                previousCount = eligible.Count; previousMode = map.m_mode; previousZoom = zoom; previousMinimum = host.GroupThreshold.Value;
                previousScale = scale;
                previousDistance = host.GroupDistance.Value;
            }
            bool selectionVisible = false;
            foreach (var members in cachedGroups)
            {
                if (members.Any(member => !visible.Contains(member))) { nextBuild = 0; continue; }
                MarkerRecord representative = members[0];
                // Use a real member's location. Never move a plant group into an empty midpoint.
                Vector2 center = representative.pin.m_uiElement.anchoredPosition;
                representative.group = members;
                representative.pin.m_uiElement.anchoredPosition = center;
                if (representative.pin.m_NamePinData != null)
                {
                    if (representative.pin.m_NamePinData.PinNameRectTransform != null) representative.pin.m_NamePinData.PinNameRectTransform.anchoredPosition = center;
                }
                if (representative.badge == null) representative.badge = new ClusterBadge();
                representative.badge.Show(representative.pin.m_uiElement, map, members.Count);
                badgeOwners.Add(representative);
                foreach (var member in members.Skip(1))
                {
                    if (selectedGroup == representative.id && map.m_mode == Minimap.MapMode.Large) continue;
                    member.groupHidden = true;
                }
                if (selectedGroup == representative.id && !host.Window.Open && map.m_mode == Minimap.MapMode.Large)
                {
                    selectionVisible = true;
                    tooltipMembers = members;
                }
            }
            if (!selectionVisible) Close();
            ApplyVisibility(records);
        }
        private void ApplyVisibility(IEnumerable<MarkerRecord> records)
        {
            foreach (var record in records)
            {
                if (!badgeOwners.Contains(record)) record.badge?.Hide();
                var icon = record.pin?.m_uiElement;
                bool show = record.onMap && !record.groupHidden;
                if (icon != null && icon.gameObject.activeSelf != show) icon.gameObject.SetActive(show);
            }
        }
        internal void DrawTooltip(Minimap map, Plugin host, Func<MarkerRecord, string> name)
        {
            if (tooltipMembers == null || tooltipMembers.Count == 0 || map.m_mode != Minimap.MapMode.Large)
            { if (tooltip != null) tooltip.SetActive(false); return; }
            var canvas = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var representative = tooltipMembers[0].pin?.m_uiElement;
            if (representative == null) { Close(); return; }
            ShowTooltip(map, host, tooltipMembers, RectTransformUtility.WorldToScreenPoint(camera, representative.position), name);
        }
        internal void UpdateNames(IEnumerable<MarkerRecord> records, Func<MarkerRecord, string> name)
        {
            foreach (var record in records)
            {
                var text = record.pin?.m_NamePinData?.PinNameText;
                if (text == null || record.pin.m_uiElement == null || !record.pin.m_uiElement.gameObject.activeInHierarchy) continue;
                string label = record.nameGroupMembers != null ? record.nameGroupCaption : name(record) + (record.group != null ? " ×" + record.group.Count : "");
                if (text.text != label) text.text = label;
            }
        }
        private void ShowTooltip(Minimap map, Plugin host, List<MarkerRecord> members, Vector2 screen, Func<MarkerRecord, string> name)
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
                tooltipText.enableAutoSizing = false; tooltipText.textWrappingMode = TextWrappingModes.NoWrap;
                tooltipText.overflowMode = TextOverflowModes.Ellipsis;
                tooltipText.color = new Color(1, 0.86f, 0.63f);
                tooltipText.rectTransform.anchorMin = Vector2.zero; tooltipText.rectTransform.anchorMax = Vector2.one;
            tooltipText.rectTransform.offsetMin = new Vector2(18, 16); tooltipText.rectTransform.offsetMax = new Vector2(-18, -16);
            }
            tooltip.transform.SetAsLastSibling();
            var lines = members.Select(name).Distinct().Take(12).ToList();
            string text = Plugin.Text("Объектов: ", "Objects: ") + members.Count + "\n" + string.Join("\n", lines.ToArray());
            if (members.Count > 12) text += "\n…";
            if (tooltipText.text != text) tooltipText.text = text;
            RectTransform rect = (RectTransform)tooltip.transform;
            float height = 24 * (lines.Count + 1) + 36;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            RectTransform parentRect = parent as RectTransform;
            var canvas = parent.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, camera, out var local))
            {
                // Only stable map/object state invalidates a placement. Native label
                // visibility and hover widgets must never drive this signature.
                int geometry = map.LargeZoom.GetHashCode() ^ parentRect.rect.GetHashCode() ^ host.CreatureNameSize.Value ^ (host.ObjectNameSize.Value << 8);
                foreach (var member in members)
                    geometry = unchecked(geometry + (member.id.GetHashCode() * 397 ^ member.position.GetHashCode() ^ name(member).GetHashCode()));
                if (tooltipGeometry != geometry) { tooltipPlacement.Reset(); tooltipGeometry = geometry; }
                tooltipObstacles.Clear();
                foreach (var member in members)
                {
                    ProtectTooltip(member.pin?.m_uiElement, parentRect);
                    if (member.pin?.m_NamePinData?.PinNameText != null)
                    {
                        var label = member.pin.m_NamePinData.PinNameText;
                        string caption = name(member) + (member.group != null ? " ×" + member.group.Count : "");
                        ProtectTooltip(label.rectTransform, parentRect, label.GetPreferredValues(caption), true);
                    }
                }
                var area = new LabelBox(parentRect.rect.xMin + 12, parentRect.rect.yMin + 12, parentRect.rect.width - 24, parentRect.rect.height - 24);
                if (!tooltipPlacement.TryPlace(tooltipObstacles, area, 320, height, local.x, local.y, 20, out var placed, lockPosition: true))
                { tooltip.SetActive(false); return; }
                rect.sizeDelta = new Vector2(placed.Width, placed.Height);
                rect.anchoredPosition = new Vector2(placed.X + placed.Width * 0.5f, placed.Y + placed.Height * 0.5f) - parentRect.rect.center;
                tooltip.SetActive(true);
            }
            else tooltip.SetActive(false);
        }
        private void ProtectTooltip(RectTransform element, RectTransform parent, Vector2? textSize = null, bool reserveHidden = false)
        {
            if (element == null || (!reserveHidden && (!element.gameObject.activeInHierarchy || (element.GetComponent<CanvasGroup>()?.alpha ?? 1) < 0.01f))) return;
            if (textSize.HasValue)
            {
                var center = element.rect.center; var half = textSize.Value * 0.5f;
                tooltipCorners[0] = element.TransformPoint(new Vector3(center.x - half.x, center.y - half.y));
                tooltipCorners[1] = element.TransformPoint(new Vector3(center.x - half.x, center.y + half.y));
                tooltipCorners[2] = element.TransformPoint(new Vector3(center.x + half.x, center.y + half.y));
                tooltipCorners[3] = element.TransformPoint(new Vector3(center.x + half.x, center.y - half.y));
            }
            else element.GetWorldCorners(tooltipCorners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var corner in tooltipCorners)
            {
                var point = parent.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, point.x); minY = Mathf.Min(minY, point.y); maxX = Mathf.Max(maxX, point.x); maxY = Mathf.Max(maxY, point.y);
            }
            tooltipObstacles.Add(new LabelBox(minX, minY, maxX - minX, maxY - minY));
        }
        public void Dispose()
        {
            if (tooltip != null) UnityEngine.Object.Destroy(tooltip);
            if (tooltipFrame != null) UnityEngine.Object.Destroy(tooltipFrame);
            tooltipFrame = null;
            tooltip = null; tooltipText = null; cachedGroups.Clear(); previousCount = -1; nextBuild = 0;
            badgeOwners.Clear(); eligible.Clear(); visible.Clear();
            Close();
        }
    }
}
