using System;
using Newtonsoft.Json;
using UnityEngine;

namespace MapTracker
{
    [Serializable]
    public sealed class NavigationShare
    {
        [JsonProperty(Required = Required.Always)] public int schema = 1;
        [JsonProperty(Required = Required.Always)] public string world;
        [JsonProperty(Required = Required.Always)] public string name;
        public string type;
        public string objectId;
        [JsonProperty(Required = Required.Always)] public Vector3 position;
        internal static bool Allowed(NavigationShare target, string currentWorld, bool enabled, bool sharing)
        {
            if (!enabled || !sharing || target == null || target.schema != 1 || string.IsNullOrEmpty(currentWorld) ||
                target.world != currentWorld || string.IsNullOrWhiteSpace(target.name) || target.name.Length > 160 ||
                (target.type != null && target.type.Length > 256) || (target.objectId != null && target.objectId.Length > 256)) return false;
            var p = target.position;
            return PlayerNavigation.Finite(p) && Math.Abs(p.x) <= 20000 && Math.Abs(p.z) <= 20000 && Math.Abs(p.y) <= 3000;
        }
        internal static string Caption(string name) => string.IsNullOrWhiteSpace(name) ? "Map destination" :
            (name.Length > 160 ? name.Substring(0, 160) : name).Replace('\r', ' ').Replace('\n', ' ');
    }
}
