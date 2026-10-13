using System.Collections.Generic;
using UnityEngine;

namespace MapTracker
{
    internal sealed class NavigationTargets
    {
        internal MarkerRecord Personal, Shared;
        internal static bool SamePoint(MarkerRecord a, MarkerRecord b)
        {
            if (a == null || b == null) return false;
            float x = a.position.x - b.position.x, z = a.position.z - b.position.z, y = a.position.y - b.position.y;
            return x * x + z * z <= 0.25f && y * y <= 1;
        }
        internal static MarkerRecord FindObject(IDictionary<string, MarkerRecord> records, string id, string type) =>
            !string.IsNullOrEmpty(id) && records.TryGetValue(id, out var record) && record.type == type ? record : null;
        internal void SetShared(Vector3 position, string name)
        {
            string caption = "[Shared] " + NavigationShare.Caption(name);
            Shared = new MarkerRecord { id = "navigation:shared", type = "navigation:shared", position = position, nameCache = caption,
                entry = new CatalogEntry { key = "navigation:shared", name = caption, prefab = "Map destination", category = "Navigation" } };
        }
        internal void ClearShared() { Shared = null; }
    }
}
