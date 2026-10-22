using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed partial class NativeFilterPanel
    {
        private GameObject presetRoot;
        private RectTransform presetContent;
        private ScrollRect presetScroll;
        private InputField presetName;
        private Text presetStatus;
        private Button presetLoad, presetSave, presetDelete;
        private string selectedPreset;
        private BuiltInFilterPreset? builtInPreset;
        private readonly List<Button> presetButtons = new List<Button>();
        private readonly List<Button> presetShareButtons = new List<Button>();
        private string previousShareStatus;
        private readonly List<bool> presetActive = new List<bool>();
        private readonly FilterPresetStore presetStore = new FilterPresetStore(Path.Combine(Paths.ConfigPath, "MapTracker", "presets.json"));
        private void OpenPresets()
        {
            CloseDiscoveries();
            if (presetRoot == null) BuildPresets();
            presetRoot.SetActive(true); presetRoot.transform.SetAsLastSibling();
            PresetAction(() => { RefreshPresets(); presetStatus.text = ""; });
        }
        private void ClosePresets() { if (presetRoot != null) presetRoot.SetActive(false); if (sharingMode == "recipients") CloseSharing(); }
        internal bool DismissPresets()
        {
            if (presetRoot == null || !presetRoot.activeSelf) return false;
            ClosePresets(); return true;
        }
        private void TickPresets()
        {
            if (presetRoot == null || !presetRoot.activeSelf) return;
            bool ready = window.Host.Catalog.Ready;
            presetLoad.interactable = ready && (builtInPreset.HasValue || selectedPreset != null);
            presetSave.interactable = ready && !string.IsNullOrWhiteSpace(presetName.text);
            presetDelete.interactable = builtInPreset.HasValue || selectedPreset != null;
            foreach (var button in presetShareButtons) button.interactable = ready && window.Host.Enabled.Value && window.Host.PresetSharing?.Transfer?.Busy == false;
            string status = window.Host.PresetSharing?.Transfer?.Status;
            if (!string.IsNullOrEmpty(status) && status != previousShareStatus)
            { presetStatus.text = status; previousShareStatus = status; }
            if (!ready) presetStatus.text = Plugin.Text("Дождись окончания загрузки каталога.", "Wait for the catalog to finish loading.");
        }
        private void BuildPresets()
        {
            var overlay = Rect("PresetOverlay", panel); Stretch(overlay, 0, 0, 0, 0);
            presetRoot = overlay.gameObject;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.65f);
            var dialog = Rect("PresetPanel", overlay);
            Region(dialog, new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f), Vector2.zero, Vector2.zero);
            dialog.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.07f, 0.04f, 1);
            var frame = Rect("WoodFrame", dialog); Stretch(frame, 0, 0, 0, 0);
            var wood = frame.gameObject.AddComponent<Image>(); wood.sprite = panelSprite; wood.type = Image.Type.Sliced;
            wood.material = panelMaterial; wood.raycastTarget = false;
            var title = Label(Plugin.Text("Пресеты", "Presets"), dialog, 30, Orange, TextAnchor.MiddleCenter, true);
            StretchTop(title.rectTransform, 40, 12, 40, 38);
            var close = Button(dialog, "×", ClosePresets);
            Pin((RectTransform)close.transform, Vector2.one, new Vector2(-16, -14), new Vector2(32, 32));
            var area = Rect("PresetList", dialog); Stretch(area, 24, 64, 24, 150);
            presetScroll = Scroll(area, out presetContent);
            presetScroll.scrollSensitivity = nativeScroll ? list.scrollSensitivity : 20;
            var nameLabel = Label(Plugin.Text("Название пресета", "Preset name"), dialog, 14, Beige, TextAnchor.MiddleLeft);
            Region(nameLabel.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(24, 121), new Vector2(-24, 144));
            presetName = Search(dialog); presetName.text = ""; presetName.characterLimit = 80;
            ((Text)presetName.placeholder).text = Plugin.Text("Введи название для сохранения…", "Enter a name to save…");
            Region((RectTransform)presetName.transform, Vector2.zero, new Vector2(1, 0), new Vector2(24, 88), new Vector2(-24, 120));
            presetLoad = Button(dialog, Plugin.Text("Загрузить пресет", "Load preset"), () => PresetAction(() =>
            {
                if (!window.Host.Catalog.Ready) return;
                if (builtInPreset.HasValue)
                    FilterPresets.Apply(window.Host, BuiltInFilterPresets.Build(window.Host.Catalog.Entries, builtInPreset.Value));
                else
                {
                    var saved = presetStore.Read().Find(p => string.Equals(p.name, selectedPreset, StringComparison.OrdinalIgnoreCase));
                    if (saved == null) throw new InvalidOperationException("Preset no longer exists.");
                    FilterPresets.Apply(window.Host, saved);
                }
                Refresh(); window.Host.Tracking.RequestPinRefresh();
                window.Host.Presets.Loaded(builtInPreset.HasValue ? "Built-in: " + builtInPreset.Value : selectedPreset);
                presetStatus.text = Plugin.Text("Пресет загружен.", "Preset loaded.");
            }));
            Region((RectTransform)presetLoad.transform, Vector2.zero, new Vector2(0.333f, 0), new Vector2(24, 46), new Vector2(-4, 78));
            presetSave = Button(dialog, Plugin.Text("Сохранить пресет", "Save preset"), () => PresetAction(() =>
            {
                if (!window.Host.Catalog.Ready) return;
                string name = presetName.text.Trim();
                presetStore.Save(FilterPresets.Capture(window.Host, name));
                selectedPreset = name; builtInPreset = null; RefreshPresets();
                presetStatus.text = Plugin.Text("Пресет сохранён. Такое же имя заменяет прежний набор.", "Preset saved. The same name replaces the previous set.");
            }));
            Region((RectTransform)presetSave.transform, new Vector2(0.333f, 0), new Vector2(0.666f, 0), new Vector2(4, 46), new Vector2(-4, 78));
            presetDelete = Button(dialog, Plugin.Text("Удалить пресет", "Delete preset"), () => PresetAction(() =>
            {
                if (builtInPreset.HasValue) presetStore.DeleteBuiltIn(builtInPreset.Value);
                else if (selectedPreset != null) presetStore.Delete(selectedPreset);
                else return;
                builtInPreset = null; selectedPreset = null; presetName.text = ""; RefreshPresets();
                presetStatus.text = Plugin.Text("Пресет удалён. Текущий фильтр не изменён.", "Preset deleted. Current filters are unchanged.");
            }));
            Region((RectTransform)presetDelete.transform, new Vector2(0.666f, 0), new Vector2(1, 0), new Vector2(4, 46), new Vector2(-24, 78));
            presetStatus = Label("", dialog, 13, Beige, TextAnchor.MiddleLeft);
            Region(presetStatus.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(24, 6), new Vector2(-24, 40));
        }
        private void RefreshPresets()
        {
            // Read first: invalid storage must not remove the existing list or be overwritten.
            var snapshot = presetStore.ReadSnapshot();
            var saved = snapshot.presets;
            saved.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name));
            foreach (var button in presetButtons) UnityEngine.Object.Destroy(button.gameObject);
            foreach (var button in presetShareButtons) UnityEngine.Object.Destroy(button.gameObject);
            presetShareButtons.Clear();
            presetButtons.Clear(); presetActive.Clear();
            if (!snapshot.deletedBuiltIns.Contains(BuiltInFilterPreset.Default.ToString()))
                AddPresetButton(Plugin.Text("Дефолт (встроенный)", "Default (built-in)"), null, BuiltInFilterPreset.Default);
            if (!snapshot.deletedBuiltIns.Contains(BuiltInFilterPreset.Gathering.ToString()))
                AddPresetButton(Plugin.Text("Собирательство (встроенный)", "Gathering (built-in)"), null, BuiltInFilterPreset.Gathering);
            if (!snapshot.deletedBuiltIns.Contains(BuiltInFilterPreset.Exploration.ToString()))
                AddPresetButton(Plugin.Text("Разведка (встроенный)", "Exploration (built-in)"), null, BuiltInFilterPreset.Exploration);
            if (!snapshot.deletedBuiltIns.Contains(BuiltInFilterPreset.Household.ToString()))
                AddPresetButton(Plugin.Text("Хозяйство (встроенный)", "Household (built-in)"), null, BuiltInFilterPreset.Household);
            foreach (var preset in saved) AddPresetButton(preset.name, preset.name, null);
            presetContent.sizeDelta = new Vector2(0, presetButtons.Count * 42);
            HighlightPreset();
        }
        private void AddPresetButton(string caption, string name, BuiltInFilterPreset? basic)
        {
            int index = presetButtons.Count;
            var button = Button(presetContent, caption, () =>
            {
                selectedPreset = name; builtInPreset = basic;
                presetName.text = basic.HasValue ? "" : name;
                for (int i = 0; i < presetActive.Count; i++) presetActive[i] = i == index;
                presetStatus.text = ""; HighlightPreset();
            });
            button.GetComponentInChildren<Text>().supportRichText = false;
            StretchTop((RectTransform)button.transform, 4, presetButtons.Count * 42, 48, 38);
            var share = Button(presetContent, "", () => PresetAction(() =>
            {
                FilterPreset preset;
                if (basic.HasValue) { preset = BuiltInFilterPresets.Build(window.Host.Catalog.Entries, basic.Value); preset.name = basic.Value.ToString(); }
                else preset = presetStore.Read().Find(p => string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase));
                if (preset == null) throw new InvalidOperationException("Preset no longer exists.");
                OpenShareRecipients(preset);
            }));
            var shareRect = (RectTransform)share.transform;
            Region(shareRect, new Vector2(1, 1), Vector2.one, new Vector2(-44, -index * 42 - 38), new Vector2(-4, -index * 42));
            var glyph = Rect("ShareIcon", share.transform); Stretch(glyph, 9, 8, 9, 8);
            var graphic = glyph.gameObject.AddComponent<SharePresetGraphic>(); graphic.color = Orange; graphic.raycastTarget = false;
            presetShareButtons.Add(share);
            presetButtons.Add(button);
            presetActive.Add(basic.HasValue ? basic == builtInPreset : !builtInPreset.HasValue &&
                string.Equals(name, selectedPreset, StringComparison.OrdinalIgnoreCase));
        }
        private void HighlightPreset()
        {
            for (int i = 0; i < presetButtons.Count; i++)
            {
                var button = presetButtons[i];
                bool active = presetActive[i];
                var colors = button.colors;
                colors.normalColor = active ? new Color(1, 0.75f, 0.38f) : Color.white;
                colors.highlightedColor = active ? new Color(1, 0.85f, 0.55f) : new Color(1, 0.9f, 0.7f);
                colors.selectedColor = colors.normalColor; button.colors = colors;
            }
        }
        private void PresetAction(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                window.Host.Report("Filter preset operation failed", error);
                presetStatus.text = Plugin.Text("Не удалось выполнить действие. Подробности в журнале.", "Action failed. See the log for details.");
            }
        }
    }
}
