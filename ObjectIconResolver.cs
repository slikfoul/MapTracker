using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MapTracker
{
    internal static class ObjectIconResolver
    {
        private sealed class Binding { public string key { get; set; } public string source { get; set; } }
        private sealed class Document
        {
            public int schema { get; set; }
            public List<Binding> icons { get; set; }
            public string[] excluded { get; set; }
            public string[] nativeMarkers { get; set; }
        }
        private static readonly Dictionary<string, string> assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> excluded = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> nativeMarkers = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static bool loaded;
        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MapTracker.ObjectIcons.catalog.json"))
                using (var reader = new StreamReader(stream ?? throw new FileNotFoundException("Object icon catalog is missing.")))
                {
                    var doc = DataJson.Read<Document>(reader.ReadToEnd());
                    if (doc?.schema != 1 || doc.icons == null) throw new InvalidDataException("Invalid object icon catalog.");
                    foreach (var binding in doc.icons)
                        if (!string.IsNullOrEmpty(binding.key) && !string.IsNullOrEmpty(binding.source)) assignments.Add(binding.key, binding.source);
                    foreach (string key in doc.excluded ?? new string[0]) excluded.Add(key);
                    foreach (string key in doc.nativeMarkers ?? new string[0]) nativeMarkers.Add(key);
                }
            }
            catch (Exception e)
            {
                assignments.Clear(); excluded.Clear(); nativeMarkers.Clear();
                Plugin.Instance?.Report("Could not read the object icon catalog", e);
            }
        }
        internal static bool IsServiceDefinition(CatalogEntry entry)
        {
            EnsureLoaded();
            // A mod may turn a native internal item into a real item with artwork.
            return entry != null && entry.Icon == null && !string.IsNullOrEmpty(entry.key) && excluded.Contains(entry.key);
        }
        internal static bool HasAssignment(string key) { EnsureLoaded(); return key != null && assignments.ContainsKey(key); }
        internal static bool HasNativeMarker(string key) { EnsureLoaded(); return key != null && nativeMarkers.Contains(key); }
        internal static Sprite Resolve(CatalogEntry entry, GameObject self = null)
        {
            if (entry == null || entry.creature || entry.Icon != null || DungeonIcon.HasSymbol(entry)) return entry?.Icon;
            EnsureLoaded();
            if (entry.key == null || !assignments.TryGetValue(entry.key, out string source)) return null;
            if (sprites.TryGetValue(source, out var existing) && existing != null) return existing;
            int split = source.IndexOf(':');
            if (split <= 0) return null;
            string kind = source.Substring(0, split), name = source.Substring(split + 1);
            Sprite icon = null;
            if (kind == "model") icon = CustomCreatureIcons.Load(name, "ObjectIcons");
            else if (kind == "item")
            {
                var item = self != null && name == entry.prefab ? self.GetComponent<ItemDrop>() : ObjectDB.instance?.GetItemPrefab(name)?.GetComponent<ItemDrop>();
                icon = item?.m_itemData.GetIcon();
            }
            else if (kind == "piece")
            {
                var prefab = self != null && name == entry.prefab ? self : ZNetScene.instance?.GetPrefab(name);
                icon = prefab?.GetComponent<Piece>()?.m_icon;
            }
            if (icon != null) sprites[source] = icon;
            return icon;
        }
        internal static void Dispose()
        {
            // Native artwork belongs to the game; the shared PNG loader owns only
            // the embedded textures and releases those alongside creature portraits.
            sprites.Clear(); assignments.Clear(); excluded.Clear(); nativeMarkers.Clear(); loaded = false;
        }
    }
}
