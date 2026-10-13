using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    internal sealed class CreatureNameGroups
    {
        private readonly List<MarkerRecord> eligible = new List<MarkerRecord>();
        private readonly HashSet<MarkerRecord> visible = new HashSet<MarkerRecord>();
        private readonly List<CreatureNamePoint> points = new List<CreatureNamePoint>();
        private readonly List<CreatureNamePoint> centrePoints = new List<CreatureNamePoint>();
        private List<List<MarkerRecord>> groups = new List<List<MarkerRecord>>();
        private float nextBuild, previousZoom;
        private int previousCount = -1, previousRadius;
        private Minimap.MapMode previousMode;
        internal void Invalidate() { nextBuild = 0; previousCount = -1; }
        internal void Reset() { Invalidate(); groups.Clear(); eligible.Clear(); visible.Clear(); points.Clear(); centrePoints.Clear(); }
        internal void Apply(IEnumerable<MarkerRecord> records, Minimap map, Plugin host)
        {
            eligible.Clear(); visible.Clear();
            foreach (var record in records)
            {
                record.nameGroupMembers = null; record.nameGroupFollower = false;
                if (!host.GroupCreatureNames.Value || record.entry?.creature != true || host.Tracking.Discoveries.IsTarget(record) ||
                    record.pin?.m_uiElement == null || !record.pin.m_uiElement.gameObject.activeInHierarchy) continue;
                eligible.Add(record); visible.Add(record);
            }
            if (!host.GroupCreatureNames.Value || eligible.Count < 2) { Invalidate(); return; }
            var canvas = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float zoom = map.m_mode == Minimap.MapMode.Large ? map.LargeZoom : map.SmallZoom;
            if (Time.realtimeSinceStartup >= nextBuild || previousCount != eligible.Count || previousRadius != host.CreatureNameGroupRadius.Value ||
                previousZoom != zoom || previousMode != map.m_mode)
            {
                points.Clear();
                foreach (var record in eligible)
                    points.Add(new CreatureNamePoint { WorldX = record.pin.m_pos.x, WorldZ = record.pin.m_pos.z,
                        Kind = record.type + ":" + record.pin.m_iconElement.color });
                groups = CreatureNameGroupMath.Build(points, host.CreatureNameGroupRadius.Value)
                    .Select(indices => indices.Select(i => eligible[i]).ToList()).ToList();
                previousCount = eligible.Count; previousRadius = host.CreatureNameGroupRadius.Value;
                previousMode = map.m_mode; previousZoom = zoom; nextBuild = Time.realtimeSinceStartup + 0.2f;
            }
            foreach (var members in groups)
            {
                if (members.Any(member => !visible.Contains(member))) { nextBuild = 0; continue; }
                centrePoints.Clear();
                foreach (var member in members)
                {
                    Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, member.pin.m_uiElement.position);
                    centrePoints.Add(new CreatureNamePoint { ScreenX = screen.x, ScreenY = screen.y, Count = member.group?.Count ?? 1 });
                }
                CreatureNameGroupMath.Centre(centrePoints, out float x, out float y, out int count);
                var anchor = members[0]; anchor.nameGroupMembers = members; anchor.nameGroupScreen = new Vector2(x, y);
                string label = host.Tracking.Name(anchor);
                if (anchor.nameGroupCaption == null || anchor.nameGroupBase != label || anchor.nameGroupCount != count)
                { anchor.nameGroupBase = label; anchor.nameGroupCount = count; anchor.nameGroupCaption = label + " ×" + count; }
                var text = anchor.pin.m_NamePinData?.PinNameText;
                if (text != null && text.text != anchor.nameGroupCaption) text.text = anchor.nameGroupCaption;
                for (int i = 1; i < members.Count; i++) members[i].nameGroupFollower = true;
            }
        }
    }
}
