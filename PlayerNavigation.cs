using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MapTracker
{
    internal static class PlayerNavigation
    {
        private static readonly System.Reflection.FieldInfo Pins = AccessTools.Field(typeof(Minimap), "m_playerPins");
        private static readonly System.Reflection.FieldInfo Players = AccessTools.Field(typeof(Minimap), "m_tempPlayerInfo");
        internal static bool TryPick(Minimap map, Vector2 pointer, out ZNet.PlayerInfo selected, out string name, out float distance)
        {
            selected = default; name = null; distance = float.MaxValue;
            if (map == null || map.m_mode != Minimap.MapMode.Large) return false;
            var canvas = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(map.m_mapImageLarge.rectTransform, pointer, camera)) return false;
            // These native lists are paired by the game itself, including duplicate player names.
            var pins = Pins.GetValue(map) as List<Minimap.PinData>;
            var players = Players.GetValue(map) as List<ZNet.PlayerInfo>;
            if (pins == null || players == null || pins.Count != players.Count) return false;
            bool found = false;
            for (int i = 0; i < pins.Count; i++)
            {
                var pin = pins[i]; var info = players[i];
                if (!info.m_publicPosition || info.m_characterID.IsNone() || pin.m_uiElement == null || !pin.m_uiElement.gameObject.activeInHierarchy) continue;
                float d = Vector2.Distance(pointer, RectTransformUtility.WorldToScreenPoint(camera, pin.m_uiElement.position));
                float reach = Mathf.Max(18, pin.m_uiElement.rect.width * 0.5f) * (canvas != null ? canvas.scaleFactor : 1);
                if (d > reach || d >= distance) continue;
                found = true; selected = info; name = pin.m_name; distance = d;
            }
            return found;
        }
        internal static bool Finite(Vector3 point) => !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z) &&
            !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);
        internal static bool TryPosition(IEnumerable<ZNet.PlayerInfo> players, ZDOID id, out Vector3 position)
        {
            position = default;
            if (id.IsNone()) return false;
            foreach (var player in players)
                if (player.m_publicPosition && player.m_characterID == id && Finite(player.m_position))
                { position = player.m_position; return true; }
            return false;
        }
    }
}
