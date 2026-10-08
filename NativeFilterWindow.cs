using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MapTracker
{
    internal sealed class FilterWindow
    {
        internal readonly Plugin Host;
        private readonly MapFilterButton mapButton;
        private readonly NativeFilterPanel panel;
        internal bool Open { get; private set; }
        internal string Search = "", Category = "";
        internal string FocusedKey;
        internal bool KeepOnly, SelectedOnly;
        internal FilterWindow(Plugin plugin)
        {
            Host = plugin; mapButton = new MapFilterButton(this); panel = new NativeFilterPanel(this);
        }
        internal void Toggle() { if (Player.m_localPlayer != null) { Open = !Open; panel.SetVisible(Open); } }
        internal void Close() { Open = false; panel.SetVisible(false); }
        internal void Focus(CatalogEntry entry)
        {
            if (entry == null || Player.m_localPlayer == null) return;
            Category = entry.category; Search = ""; FocusedKey = entry.key; KeepOnly = SelectedOnly = false;
            Open = true; panel.SetVisible(true); panel.Focus(entry.key);
        }
        internal void Tick()
        {
            mapButton.Tick();
            if (Player.m_localPlayer == null) Close();
            else if (Open && Input.GetKeyDown(KeyCode.Escape) && !panel.DismissPresets()) Close();
            if (Open) panel.Tick();
        }
        internal bool ContainsMapButtonPointer() { return mapButton.ContainsPointer(); }
        internal void Dispose() { Close(); panel.Dispose(); mapButton.Dispose(); }
        internal List<FilterChoice> Filtered()
        {
            var choices = FilterChoice.Build(Host.Catalog.Entries.Where(e => Category == "" || Category == e.category), e => e.FilterLabel);
            return choices.Where(c => (!KeepOnly || c.KeepVisible) && (!SelectedOnly || c.Selected) &&
                (Search.Length == 0 || c.Label.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.Members.Any(e => e.prefab.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0))).ToList();
        }
        internal void SetSelected(FilterChoice choice, bool selected)
        {
            bool autosave = Host.Config.SaveOnConfigSet; Host.Config.SaveOnConfigSet = false;
            try { foreach (var entry in choice.Members) entry.Selected.Value = selected; }
            finally { Host.Config.SaveOnConfigSet = autosave; }
            Host.Config.Save();
            if (SelectedOnly) panel.Refresh();
        }
        internal void SelectAll(bool selected)
        {
            bool autosave = Host.Config.SaveOnConfigSet; Host.Config.SaveOnConfigSet = false;
            try { foreach (var choice in Filtered()) foreach (var entry in choice.Members) entry.Selected.Value = selected; }
            finally { Host.Config.SaveOnConfigSet = autosave; }
            Host.Config.Save(); panel.Refresh();
        }
        internal void SetKeepVisible(FilterChoice choice, bool keep)
        {
            bool autosave = Host.Config.SaveOnConfigSet; Host.Config.SaveOnConfigSet = false;
            try { foreach (var entry in choice.Members) if (entry.KeepVisible != null) entry.KeepVisible.Value = keep; }
            finally { Host.Config.SaveOnConfigSet = autosave; }
            Host.Config.Save();
            if (KeepOnly) panel.Refresh();
        }
        internal static string GroupName(string group)
        {
            switch (group)
            {
                case "": return Plugin.Text("Все категории", "All categories");
                case "Ore": return Plugin.Text("Руда и залежи", group);
                case "Plants and pickables": return Plugin.Text("Растения и сбор", group);
                case "Creatures": return Plugin.Text("Существа", group);
                case "Dungeons": return Plugin.Text("Подземелья", group);
                case "Locations": return Plugin.Text("Локации", group);
                case "Portals": return Plugin.Text("Порталы", group);
                case "Containers": return Plugin.Text("Контейнеры", group);
                case "Dvergr loot": return Plugin.Text("Двергрские экстракторы", group);
                case "Sap collectors": return Plugin.Text("Сборщики сока", group);
                case "Sap roots": return Plugin.Text("Корни для сока", group);
                case "Wild beehives": return Plugin.Text("Дикие ульи", group);
                case "Player structures": return Plugin.Text("Постройки", group);
                case "Dropped items": return Plugin.Text("Предметы на земле", group);
                case "Trees": return Plugin.Text("Деревья", group);
                case "Destructibles": return Plugin.Text("Разрушаемые объекты", group);
                default: return Plugin.Text("Прочее", group);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class FilterInputPatch
    {
        private static void Postfix(ref bool __result) { if (Plugin.Instance?.Window?.Open == true) __result = false; }
    }
    [HarmonyPatch(typeof(TextInput), "IsVisible")]
    internal static class FilterCursorPatch
    {
        private static void Postfix(ref bool __result) { if (Plugin.Instance?.Window?.Open == true) __result = true; }
    }
}
