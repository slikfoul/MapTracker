using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    internal static class DiscoveryQuery
    {
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        internal static float Distance(MarkerRecord record, Vector3 origin)
        { float x = record.position.x - origin.x, z = record.position.z - origin.z; return (float)Math.Sqrt(x * x + z * z); }
        internal static List<MarkerRecord> Search(IEnumerable<MarkerRecord> records, string search, bool favorites,
            Vector3 origin, Func<MarkerRecord, string> name)
        {
            string term = (search ?? "").Trim();
            return records.Where(r => !r.dynamic && !r.shared && r.entry != null && !r.entry.creature && (!favorites || r.favorite) &&
                (term.Length == 0 || name(r).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (r.type ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 (r.portalTag ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(r => r.favorite).ThenBy(r => Distance(r, origin)).ThenBy(r => r.id, StringComparer.Ordinal).ToList();
        }
        internal static string Direction(Vector3 origin, Vector3 destination)
        {
            double angle = Math.Atan2(destination.x - origin.x, destination.z - origin.z) * 180 / Math.PI;
            int index = ((int)Math.Floor((angle + 360 + 22.5) / 45)) % 8;
            return Directions[index];
        }
    }
    internal sealed class DiscoveryTools
    {
        private readonly Tracking tracking;
        private readonly Plugin host;
        private readonly NavigationOverlay navigation = new NavigationOverlay();
        private readonly NavigationHud navigationHud = new NavigationHud();
        private readonly NavigationOverlay sharedNavigation = new NavigationOverlay();
        private readonly NavigationHud sharedNavigationHud = new NavigationHud();
        private readonly NavigationTargets targets = new NavigationTargets();
        private MarkerRecord target { get => targets.Personal; set => targets.Personal = value; }
        private MarkerRecord sharedTarget => targets.Shared;
        private Minimap sharedWaypointMap;
        private Minimap.PinData sharedWaypointPin;
        private MarkerRecord sharedObject;
        private string sharedObjectId, sharedObjectType;
        private bool pointTarget;
        private bool playerTarget;
        private ZDOID playerTargetId;
        private readonly List<ZNet.PlayerInfo> publicPlayers = new List<ZNet.PlayerInfo>();
        private Minimap waypointMap;
        private Minimap.PinData waypointPin;
        private static readonly System.Reflection.MethodInfo ScreenToWorld = HarmonyLib.AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint", new[] { typeof(Vector3) });
        private string previousTypes;
        private readonly HashSet<string> notifyTypes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> notifications = new Queue<string>();
        private float nextNotification;
        internal DiscoveryTools(Tracking owner, Plugin plugin) { tracking = owner; host = plugin; }
        internal List<MarkerRecord> Search(string term, bool favorites) => DiscoveryQuery.Search(tracking.Records, term, favorites,
            Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero, tracking.Name);
        internal void Favorite(MarkerRecord record) { record.favorite = !record.favorite; tracking.DiscoveryChanged(record); }
        internal void Select(MarkerRecord record) { RemoveWaypoint(); pointTarget = playerTarget = false; target = record; tracking.SettingsChanged(); }
        internal void ClearTarget() { RemoveWaypoint(); target = null; pointTarget = playerTarget = false; playerTargetId = default; tracking.SettingsChanged(); }
        internal bool IsPlayerTarget(ZNet.PlayerInfo player) => playerTarget && playerTargetId == player.m_characterID;
        internal void SelectPlayer(ZNet.PlayerInfo player, string name)
        {
            if (!player.m_publicPosition || player.m_characterID.IsNone() || !PlayerNavigation.Finite(player.m_position)) return;
            ClearTarget(); playerTarget = true; playerTargetId = player.m_characterID;
            target = new MarkerRecord { id = "navigation:player", type = "navigation:player", position = player.m_position,
                nameCache = name, entry = new CatalogEntry { key = "navigation:player", name = name, prefab = "Player", category = "Players" } };
            tracking.RequestPinRefresh();
        }
        internal void SelectPoint(Minimap map, Vector2 pointer)
        {
            if (TryMapPosition(map, pointer, out var position)) SelectPosition(map, position, "Map destination");
        }
        internal static bool TryMapPosition(Minimap map, Vector2 pointer, out Vector3 position)
        {
            position = default;
            if (map == null) return false;
            var canvas = map.m_largeRoot.GetComponentInParent<Canvas>()?.rootCanvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(map.m_mapImageLarge.rectTransform, pointer, camera)) return false;
            position = (Vector3)ScreenToWorld.Invoke(map, new object[] { new Vector3(pointer.x, pointer.y, 0) });
            return PlayerNavigation.Finite(position);
        }
        internal void SelectPosition(Minimap map, Vector3 position, string name, CatalogEntry entry = null)
        {
            ClearTarget();
            target = new MarkerRecord { id = "navigation:point", type = "navigation:point", position = position, nameCache = name,
                entry = entry ?? new CatalogEntry { key = "navigation:point", name = name, prefab = "Map destination", category = "Navigation" } };
            pointTarget = true; waypointMap = map;
            waypointPin = map.AddPin(position, Minimap.PinType.Icon3, "", false, false);
            tracking.RequestPinRefresh();
        }
        internal bool IsWaypointTarget => pointTarget && target != null;
        internal MarkerRecord Target => target;
        internal MarkerRecord SharedTarget => sharedTarget;
        internal RectTransform SharedWaypointUI => sharedWaypointPin?.m_uiElement;
        private bool SharedUsesPersonalPoint => pointTarget && NavigationTargets.SamePoint(target, sharedTarget);
        internal void SelectSharedTarget(Minimap map, Vector3 position, string name, string type = null, string objectId = null)
        {
            RemoveSharedWaypoint(); targets.SetShared(position, name);
            sharedObjectId = objectId; sharedObjectType = type;
            sharedObject = tracking.FindNavigationObject(objectId, type, position);
            if (sharedObject != null) sharedTarget.entry = sharedObject.entry;
            var definition = host.Catalog.GetSavedDefinition(type);
            if (definition != null) sharedTarget.entry = definition;
            else if (type == "navigation:player")
                sharedTarget.entry = new CatalogEntry { key = type, name = name, prefab = "Player", category = "Players" };
            EnsureSharedWaypoint(map); tracking.SettingsChanged();
        }
        private void EnsureSharedWaypoint(Minimap map)
        {
            if (map == null || sharedTarget == null) return;
            if (SharedUsesPersonalPoint && waypointPin != null) { RemoveSharedWaypoint(); return; }
            if (sharedObject?.pin?.m_uiElement != null && sharedObject.pin.m_uiElement.gameObject.activeInHierarchy)
            { RemoveSharedWaypoint(); return; }
            if (sharedWaypointMap != map) RemoveSharedWaypoint();
            if (sharedWaypointPin == null)
            { sharedWaypointMap = map; sharedWaypointPin = map.AddPin(sharedTarget.position, Minimap.PinType.Icon3, "", false, false); }
            else if (sharedWaypointPin.m_pos != sharedTarget.position)
            { sharedWaypointPin.m_pos = sharedTarget.position; tracking.RequestPinRefresh(); }
        }
        private void RemoveSharedWaypoint()
        {
            if (sharedWaypointMap != null && sharedWaypointPin != null) sharedWaypointMap.RemovePin(sharedWaypointPin);
            sharedWaypointMap = null; sharedWaypointPin = null;
        }
        internal void ClearSharedTarget() { RemoveSharedWaypoint(); targets.ClearShared(); sharedObject = null; sharedObjectId = sharedObjectType = null; tracking.SettingsChanged(); }
        internal RectTransform WaypointUI => waypointPin?.m_uiElement;
        private void RemoveWaypoint()
        { if (waypointMap != null && waypointPin != null) waypointMap.RemovePin(waypointPin); waypointPin = null; waypointMap = null; }
        internal void HandleCancelKey()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            bool typing = selected != null && (selected.GetComponent<UnityEngine.UI.InputField>()?.isFocused == true ||
                selected.GetComponent<TMPro.TMP_InputField>()?.isFocused == true);
            if ((target == null && sharedTarget == null) || Player.m_localPlayer == null || typing || Chat.instance?.HasFocus() == true ||
                global::Console.IsVisible() || (!host.Window.Open && TextInput.IsVisible()) || InventoryGui.IsVisible() || Menu.IsVisible()) return;
            if (host.CancelNavigationKey.Value.IsDown()) { ClearTarget(); ClearSharedTarget(); }
        }
        internal bool IsTarget(MarkerRecord record) => record == target;
        internal bool IsSharedObject(MarkerRecord record) => sharedTarget != null && sharedObject == record;
        internal bool IsNavigationTarget(MarkerRecord record) => IsTarget(record) || IsSharedObject(record);
        private void ReadNotificationTypes()
        {
            if (previousTypes == host.NotifyTypes.Value) return;
            previousTypes = host.NotifyTypes.Value; notifyTypes.Clear();
            foreach (string type in previousTypes.Split(',')) if (!string.IsNullOrWhiteSpace(type)) notifyTypes.Add(type.Trim());
        }
        internal bool Notifies(string type) { ReadNotificationTypes(); return notifyTypes.Contains(type); }
        internal void ToggleNotify(string type)
        { ReadNotificationTypes(); if (!notifyTypes.Remove(type)) notifyTypes.Add(type); host.NotifyTypes.Value = string.Join(",", notifyTypes.OrderBy(t => t)); }
        internal void SetNotify(IEnumerable<CatalogEntry> entries, bool enabled)
        {
            ReadNotificationTypes();
            foreach (var entry in entries)
                if (!entry.creature && entry.category != "Dropped items")
                { if (enabled) notifyTypes.Add(entry.key); else notifyTypes.Remove(entry.key); }
            host.NotifyTypes.Value = string.Join(",", notifyTypes.OrderBy(t => t));
        }
        internal void Detected(MarkerRecord record)
        {
            ReadNotificationTypes();
            if (DiscoveryNotification.Allowed(record, host.NotifyDungeons.Value, notifyTypes.Contains(record.type)))
                if (notifications.Count < 64) notifications.Enqueue("Discovered: " + tracking.Name(record));
        }
        internal void Tick()
        {
            var player = Player.m_localPlayer;
            if (sharedTarget != null)
            {
                var resolved = sharedObject != null && tracking.Contains(sharedObject) ? sharedObject :
                    tracking.FindNavigationObject(sharedObjectId, sharedObjectType, sharedTarget.position);
                if (sharedObject != null && resolved == null) ClearSharedTarget();
                else
                {
                    if (sharedObject != resolved) { sharedObject = resolved; tracking.SettingsChanged(); }
                    if (sharedObject != null) { sharedTarget.position = sharedObject.position; sharedTarget.entry = sharedObject.entry; }
                }
            }
            if (playerTarget && target != null)
            {
                publicPlayers.Clear(); ZNet.instance?.GetOtherPublicPlayers(publicPlayers);
                if (PlayerNavigation.TryPosition(publicPlayers, playerTargetId, out var position)) target.position = position;
                else ClearTarget();
            }
            if (target != null && ((!pointTarget && !playerTarget && !tracking.Contains(target)) || (player != null && NavigationArrival.Reached(
                player.transform.position, target.position, player.InInterior(), player.IsTeleporting(), pointTarget)))) ClearTarget();
            if (sharedTarget != null && (!host.ShareMarkers.Value || (player != null && NavigationArrival.Reached(
                player.transform.position, sharedTarget.position, player.InInterior(), player.IsTeleporting(), sharedTarget.entry.category == "Navigation")))) ClearSharedTarget();
            if (Player.m_localPlayer != null && notifications.Count > 0 && Time.realtimeSinceStartup >= nextNotification)
            { Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, notifications.Dequeue()); nextNotification = Time.realtimeSinceStartup + 4; }
        }
        internal void DrawNavigation(Minimap map)
        {
            EnsureSharedWaypoint(map);
            if (map != null && waypointPin?.m_iconElement != null)
            {
                waypointPin.m_iconElement.color = TargetHighlight.Tint(true, SharedUsesPersonalPoint);
                bool visible = host.Enabled.Value && (map.m_mode == Minimap.MapMode.Large ? host.LargeMap.Value : host.SmallMap.Value);
                if (waypointPin.m_uiElement != null) waypointPin.m_uiElement.gameObject.SetActive(visible);
            }
            navigation.Apply(map, host.Enabled.Value && !host.Window.Open ? target : null, tracking.Name);
            if (map != null && sharedWaypointPin?.m_iconElement != null)
            {
                sharedWaypointPin.m_iconElement.color = new Color(0.35f, 0.85f, 1);
                bool visible = host.Enabled.Value && host.ShareMarkers.Value && (map.m_mode == Minimap.MapMode.Large ? host.LargeMap.Value : host.SmallMap.Value);
                if (sharedWaypointPin.m_uiElement != null) sharedWaypointPin.m_uiElement.gameObject.SetActive(visible);
            }
            bool sharedVisible = host.Enabled.Value && host.ShareMarkers.Value && !host.Window.Open;
            sharedNavigation.Apply(map, sharedVisible ? sharedTarget : null, tracking.Name, shared: true, paired: target != null);
            bool arrows = host.Enabled.Value && host.ShowNavigationArrow.Value && !host.Window.Open;
            navigationHud.Apply(Minimap.instance, arrows ? target : null, tracking.Name, paired: sharedTarget != null && sharedVisible);
            sharedNavigationHud.Apply(Minimap.instance, arrows && sharedVisible ? sharedTarget : null, tracking.Name, shared: true, paired: target != null);
        }
        internal void Locate(MarkerRecord record)
        {
            var map = Minimap.instance; if (map == null || Player.m_localPlayer == null) return;
            Select(record);
            host.Window.Close(); map.SetMapMode(Minimap.MapMode.Large);
            HarmonyLib.AccessTools.Field(typeof(Minimap), "m_mapOffset").SetValue(map, record.position - Player.m_localPlayer.transform.position);
            HarmonyLib.AccessTools.Method(typeof(Minimap), "CenterMap").Invoke(map, new object[] { record.position });
            tracking.RequestPinRefresh();
        }
        internal void Reset() { RemoveWaypoint(); RemoveSharedWaypoint(); targets.ClearShared(); sharedObject = null; sharedObjectId = sharedObjectType = null; target = null; pointTarget = playerTarget = false; playerTargetId = default; publicPlayers.Clear(); notifications.Clear(); navigation.Dispose(); navigationHud.Dispose(); sharedNavigation.Dispose(); sharedNavigationHud.Dispose(); }
    }
}
