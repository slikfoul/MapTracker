using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MapTracker
{
    public sealed class FilterPresetType
    {
        public bool selected, keep;
    }
    public sealed class FilterPreset
    {
        public string name;
        public Dictionary<string, FilterPresetType> types = new Dictionary<string, FilterPresetType>();
        public bool hostile = true, friendly, animals, tamed;
        public bool? groupPlants;
    }
    public sealed class FilterPresetDocument
    {
        public int schema = 1;
        public List<FilterPreset> presets = new List<FilterPreset>();
        public List<string> deletedBuiltIns = new List<string>();
    }
    // One profile-wide document: arbitrary display names never become paths.
    public sealed class FilterPresetStore
    {
        private readonly string path;
        public FilterPresetStore(string file) { path = file; }
        public List<FilterPreset> Read()
        {
            return ReadDocument().presets.OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        private FilterPresetDocument ReadDocument()
        {
            if (!File.Exists(path)) return new FilterPresetDocument();
            var document = DataJson.Read<FilterPresetDocument>(File.ReadAllText(path, Encoding.UTF8));
            if (document == null || document.schema != 1 || document.presets == null)
                throw new InvalidDataException("Unsupported filter preset document.");
            var result = document.presets;
            if (result.Any(p => p == null || string.IsNullOrWhiteSpace(p.name) || p.types == null) ||
                result.GroupBy(p => p.name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidDataException("Invalid filter presets.");
            if (document.deletedBuiltIns == null) document.deletedBuiltIns = new List<string>();
            return document;
        }
        public bool BuiltInDeleted(BuiltInFilterPreset kind) => ReadDocument().deletedBuiltIns.Contains(kind.ToString());
        public void DeleteBuiltIn(BuiltInFilterPreset kind)
        {
            var document = ReadDocument();
            if (document.deletedBuiltIns.Contains(kind.ToString())) return;
            document.deletedBuiltIns.Add(kind.ToString()); Write(document);
        }
        public void Save(FilterPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.name) || preset.name.Trim().Length > 80 || preset.types == null)
                throw new ArgumentException("Enter a preset name (1–80 characters).");
            preset.name = preset.name.Trim();
            var document = ReadDocument();
            var presets = document.presets;
            presets.RemoveAll(p => string.Equals(p.name, preset.name, StringComparison.OrdinalIgnoreCase));
            presets.Add(preset); Write(document);
        }
        public bool Delete(string name)
        {
            var document = ReadDocument();
            var presets = document.presets;
            if (presets.RemoveAll(p => string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase)) == 0) return false;
            Write(document); return true;
        }
        private void Write(FilterPresetDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, DataJson.Write(document, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    internal static class FilterPresets
    {
        internal static FilterPreset Capture(Plugin host, string name)
        {
            return new FilterPreset
            {
                name = name, hostile = host.Hostile.Value, friendly = host.Friendly.Value,
                animals = host.Animals.Value, tamed = host.Tamed.Value,
                groupPlants = host.GroupPlants.Value,
                types = host.Catalog.Entries.ToDictionary(e => e.key, e => new FilterPresetType
                { selected = e.Selected.Value, keep = e.KeepVisible != null && e.KeepVisible.Value })
            };
        }
        internal static void Apply(Plugin host, FilterPreset preset)
        {
            bool autosave = host.Config.SaveOnConfigSet; host.Config.SaveOnConfigSet = false;
            try
            {
                foreach (var entry in host.Catalog.Entries)
                {
                    preset.types.TryGetValue(entry.key, out var saved);
                    entry.Selected.Value = saved != null && saved.selected;
                    if (entry.KeepVisible != null) entry.KeepVisible.Value = saved != null && saved.keep;
                }
                host.Hostile.Value = preset.hostile; host.Friendly.Value = preset.friendly;
                host.Animals.Value = preset.animals; host.Tamed.Value = preset.tamed;
                if (preset.groupPlants.HasValue) host.GroupPlants.Value = preset.groupPlants.Value;
            }
            finally { host.Config.SaveOnConfigSet = autosave; }
            host.Config.Save(); host.Tracking.RequestPinRefresh();
        }
    }
}
