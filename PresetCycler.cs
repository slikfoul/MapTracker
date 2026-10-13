using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace MapTracker
{
    internal sealed class PresetCycler
    {
        private readonly Plugin host;
        private readonly FilterPresetStore store = new FilterPresetStore(Path.Combine(Paths.ConfigPath, "MapTracker", "presets.json"));
        private string current;
        internal PresetCycler(Plugin plugin) { host = plugin; }
        internal void Loaded(string name) { current = name; }
        internal void Cycle()
        {
            try
            {
                if (!host.Catalog.Ready) return;
                var document = store.ReadSnapshot();
                var available = new List<FilterPreset>();
                foreach (BuiltInFilterPreset kind in Enum.GetValues(typeof(BuiltInFilterPreset)))
                    if (!document.deletedBuiltIns.Contains(kind.ToString()))
                    { var preset = BuiltInFilterPresets.Build(host.Catalog.Entries, kind); preset.name = "Built-in: " + kind; available.Add(preset); }
                available.AddRange(document.presets.OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase));
                if (available.Count == 0) { Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "No presets available."); return; }
                int index = available.FindIndex(p => p.name == current);
                var selected = available[(index + 1) % available.Count];
                FilterPresets.Apply(host, selected); current = selected.name;
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Preset: " + current);
                host.Window.Refresh();
            }
            catch (Exception e) { host.Report("Preset cycling failed", e); }
        }
    }
}
