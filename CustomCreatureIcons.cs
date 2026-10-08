using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MapTracker
{
    internal static class CustomCreatureIcons
    {
        private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        private static MethodInfo decode;
        internal static Sprite For(string prefab)
        {
            switch (prefab)
            {
                case "Bat": case "Bat_Swamp": return Load("bat");
                case "Hen": return Load("hen");
                case "Chicken": return Load("chick");
                case "Boar_piggy": return Load("boar-young");
                case "Wolf_cub": return Load("wolf-cub");
                case "Asksvin_hatchling": return Load("asksvin-hatchling");
                case "Lox_Calf": return Load("lox-calf");
                default: return null;
            }
        }
        private static Sprite Load(string name)
        {
            if (icons.TryGetValue(name, out var cached)) return cached;
            Texture2D texture = null;
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MapTracker.CreatureIcons." + name + ".png"))
                {
                    if (stream == null) throw new FileNotFoundException("Embedded creature icon is missing: " + name);
                    using (var bytes = new MemoryStream())
                    {
                        stream.CopyTo(bytes);
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        texture.name = "MapTracker_" + name;
                        texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
                        // The game's image module targets a newer netstandard facade than
                        // this BepInEx plugin. Invoke its native decoder without binding
                        // that facade into our net462 compile-time references.
                        if (decode == null) decode = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true)
                            .GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                        if (decode == null || !(bool)decode.Invoke(null, new object[] { texture, bytes.ToArray(), true }))
                            throw new InvalidDataException("Invalid creature icon: " + name);
                    }
                }
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
                sprite.name = "MapTracker_" + name; icons[name] = sprite; return sprite;
            }
            catch (Exception e)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                icons[name] = null;
                Plugin.Instance?.Report("Could not load creature icon " + name, e);
                return null;
            }
        }
        internal static void Dispose()
        {
            foreach (var sprite in icons.Values)
                if (sprite != null)
                { UnityEngine.Object.Destroy(sprite.texture); UnityEngine.Object.Destroy(sprite); }
            icons.Clear();
        }
    }
}
