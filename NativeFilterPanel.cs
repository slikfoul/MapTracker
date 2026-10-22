using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.U2D;

namespace MapTracker
{
    internal sealed partial class NativeFilterPanel
    {
        private readonly FilterWindow window;
        private GameObject root;
        private RectTransform panel, listContent, categoryContent;
        private ScrollRect list, categoryList;
        private bool nativeScroll;
        private Text summary, footer;
        private Font bodyFont, titleFont;
        private Sprite panelSprite, buttonSprite, fieldSprite, checkSprite, checkboxSprite;
        private Material panelMaterial;
        private readonly List<Sprite> ownedSprites = new List<Sprite>();
        private readonly List<Row> rows = new List<Row>();
        private List<FilterChoice> entries = new List<FilterChoice>();
        private readonly List<GameObject> categories = new List<GameObject>();
        private readonly List<string> categoryKeys = new List<string>();
        private int previousCount = -1, first = -1;
        private float nextRefresh;
        private float nextCatalogRefresh;
        private string categorySignature;
        private bool needsRefresh = true, updating;
        private string pendingFocus;
        private InputField search;
        private Button keepOnly, selectedOnly;
        private const float RowHeight = 38;
        private static readonly Color Orange = new Color(1f, 0.631f, 0.235f);
        private static readonly Color Beige = new Color(0.853f, 0.725f, 0.533f);
        private sealed class Row { internal RectTransform Root; internal Toggle Toggle, Keep, Notify; internal Text Text; internal Image Icon; internal DungeonGraphic Dungeon; internal FilterChoice Entry; }
        internal NativeFilterPanel(FilterWindow filter) { window = filter; }

        internal void SetVisible(bool visible)
        {
            if (root != null) root.SetActive(visible);
            if (!visible) ClosePresets();
            if (!visible) CloseDiscoveries();
            if (visible) needsRefresh = true;
        }
        internal void Refresh() { needsRefresh = true; first = -1; }
        internal void Focus(string key)
        {
            ClosePresets();
            pendingFocus = key; Refresh();
            HighlightCategory();
            if (search != null) search.SetTextWithoutNotify("");
            HighlightKeepOnly();
        }
        internal void Tick()
        {
            if (root == null) { Dispose(); if (!Build()) return; }
            TickPresets();
            TickDiscoveries();
            root.SetActive(true);
            if (root.transform.GetSiblingIndex() != root.transform.parent.childCount - 1) root.transform.SetAsLastSibling();
            var canvas = root.transform.parent as RectTransform;
            if (canvas != null)
                panel.sizeDelta = new Vector2(Mathf.Min(1020, canvas.rect.width - 40), Mathf.Min(720, canvas.rect.height - 40));
            if (previousCount != window.Host.Catalog.Entries.Count && Time.realtimeSinceStartup >= nextCatalogRefresh)
            {
                previousCount = window.Host.Catalog.Entries.Count;
                string signature = string.Join("|", window.Host.Catalog.Entries.Select(e => e.category).Distinct().OrderBy(c => c).ToArray());
                if (signature != categorySignature) { RebuildCategories(); categorySignature = signature; }
                needsRefresh = true; nextCatalogRefresh = Time.realtimeSinceStartup + 0.5f;
            }
            if (needsRefresh)
            {
                entries = window.Filtered();
                listContent.sizeDelta = new Vector2(0, entries.Count * RowHeight);
                needsRefresh = false; first = -1;
            }
            if (pendingFocus != null)
            {
                int index = entries.FindIndex(e => e.Contains(pendingFocus));
                Canvas.ForceUpdateCanvases();
                float range = Mathf.Max(1, listContent.rect.height - list.viewport.rect.height);
                list.verticalNormalizedPosition = index < 0 ? 1 : 1 - Mathf.Clamp01(index * RowHeight / range);
                first = -1; pendingFocus = null;
            }
            HandleScroll();
            RenderRows();
            if (Time.realtimeSinceStartup >= nextRefresh)
            {
                summary.text = window.Host.Catalog.Ready ? FilterWindow.GroupName(window.Category) +
                    (window.KeepOnly ? Plugin.Text(" · Сохраняемые", " · Keep discovered") : "") +
                    (window.SelectedOnly ? Plugin.Text(" · Включённые", " · Show object") : "") :
                    Plugin.Text("Сбор каталога: ", "Collecting catalog: ") + window.Host.Catalog.Processed + "/" + window.Host.Catalog.Total;
                footer.text = Plugin.Text("Найдено: ", "Matches: ") + entries.Count + "     " +
                    Plugin.Text("Выбрано в списке: ", "Selected in list: ") + entries.Count(e => e.Selected);
                nextRefresh = Time.realtimeSinceStartup + 0.5f;
            }
        }
        private bool Build()
        {
            Minimap map = Minimap.instance;
            Canvas canvas = map?.m_largeRoot?.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return false;
            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            bodyFont = fonts.FirstOrDefault(f => f.name == "AveriaSerifLibre-Bold") ?? fonts.FirstOrDefault(f => f.name.IndexOf("Averia", StringComparison.OrdinalIgnoreCase) >= 0);
            titleFont = fonts.FirstOrDefault(f => f.name.Equals("Norsebold", StringComparison.OrdinalIgnoreCase)) ?? bodyFont;
            if (bodyFont == null) throw new InvalidOperationException("Valheim UI font is not available yet.");
            var atlases = Resources.FindObjectsOfTypeAll<SpriteAtlas>();
            panelSprite = FindSprite(atlases, "woodpanel_trophys");
            buttonSprite = FindSprite(atlases, "button");
            fieldSprite = FindSprite(atlases, "text_field");
            checkboxSprite = FindSprite(atlases, "checkbox");
            checkSprite = FindSprite(atlases, "checkbox_marker");
            panelMaterial = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "litpanel");
            root = Rect("MapTrackerFilterOverlay", canvas.transform).gameObject;
            Stretch((RectTransform)root.transform, 0, 0, 0, 0);
            Image shade = root.AddComponent<Image>(); shade.color = new Color(0, 0, 0, 0.28f);
            // A solid backing keeps the game from showing through translucent pixels in the native panel texture.
            panel = Rect("WoodPanel", root.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1020, 720);
            panel.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.07f, 0.04f, 1f);
            RectTransform frame = Rect("WoodFrame", panel);
            Stretch(frame, 0, 0, 0, 0);
            Image wood = frame.gameObject.AddComponent<Image>();
            wood.sprite = panelSprite; wood.type = Image.Type.Sliced;
            wood.material = panelMaterial; wood.color = Color.white; wood.raycastTarget = false;
            Text title = Label("MapTracker", panel, 34, Orange, TextAnchor.MiddleCenter, true);
            StretchTop(title.rectTransform, 70, 12, 70, 44);
            var drag = title.gameObject.AddComponent<FilterPanelDrag>(); drag.Target = panel;
            title.raycastTarget = true;
            summary = Label("", panel, 16, Beige, TextAnchor.MiddleCenter);
            StretchTop(summary.rectTransform, 26, 57, 26, 24);
            Button close = Button(panel, "×", window.Close);
            Pin((RectTransform)close.transform, new Vector2(1, 1), new Vector2(-20, -18), new Vector2(34, 34));
            search = Search(panel);
            StretchTop((RectTransform)search.transform, 26, 91, 26, 36);
            search.onValueChanged.AddListener(value =>
            {
                window.Search = value.Trim();
                if (window.Search.Length > 0)
                {
                    window.Category = ""; window.KeepOnly = window.SelectedOnly = false; window.FocusedKey = null;
                    HighlightCategory(); HighlightKeepOnly();
                }
                Refresh(); list.verticalNormalizedPosition = 1;
            });
            RectTransform left = Rect("Categories", panel);
            Region(left, new Vector2(0, 0), new Vector2(0.28f, 1), new Vector2(26, 64), new Vector2(-8, -146));
            categoryList = Scroll(left, out categoryContent);
            RectTransform right = Rect("Types", panel);
            Region(right, new Vector2(0.28f, 0), Vector2.one, new Vector2(8, 108), new Vector2(-26, -169));
            var headers = Rect("TypeHeaders", panel);
            Region(headers, new Vector2(0.28f, 1), Vector2.one, new Vector2(8, -166), new Vector2(-26, -133));
            var objectHeader = Label(Plugin.Text("Показывать объект", "Show object"), headers, 14, Beige, TextAnchor.MiddleLeft);
            Stretch(objectHeader.rectTransform, 8, 0, 120, 0);
            var keepHeader = Label(Plugin.Text("После ухода", "Keep discovered"), headers, 13, Beige, TextAnchor.MiddleCenter);
            Pin(keepHeader.rectTransform, new Vector2(1, 0.5f), new Vector2(-86, 0), new Vector2(112, 30));
            var notifyHeader = Label("Notify", headers, 13, Beige, TextAnchor.MiddleCenter);
            Pin(notifyHeader.rectTransform, new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(78, 30));
            list = Scroll(right, out listContent);
            float sensitivity = NativeScrollSensitivity();
            nativeScroll = sensitivity > 0;
            list.scrollSensitivity = categoryList.scrollSensitivity = nativeScroll ? sensitivity : 0;
            Button all = Button(panel, Plugin.Text("Выбрать все", "Select all"), () => window.SelectAll(true));
            Region((RectTransform)all.transform, new Vector2(0.28f, 0), new Vector2(0.52f, 0), new Vector2(8, 66), new Vector2(-4, 98));
            Button clear = Button(panel, Plugin.Text("Снять все", "Clear all"), () => window.SelectAll(false));
            Region((RectTransform)clear.transform, new Vector2(0.52f, 0), new Vector2(0.76f, 0), new Vector2(4, 66), new Vector2(-4, 98));
            Button presets = Button(panel, Plugin.Text("Пресеты", "Presets"), OpenPresets);
            Region((RectTransform)presets.transform, new Vector2(0.76f, 0), new Vector2(1, 0), new Vector2(4, 66), new Vector2(-26, 98));
            Button export = Button(panel, Plugin.Text("Выгрузить каталог", "Export catalog"), window.Host.Catalog.Export);
            Pin((RectTransform)export.transform, new Vector2(1, 0), new Vector2(-26, 20), new Vector2(210, 32));
            keepOnly = Button(panel, Plugin.Text("Только сохраняемые", "Keep discovered only"), () =>
            {
                window.KeepOnly = !window.KeepOnly;
                if (window.KeepOnly) window.SelectedOnly = false;
                Refresh(); list.verticalNormalizedPosition = 1;
                HighlightKeepOnly();
            });
            Region((RectTransform)keepOnly.transform, new Vector2(0.28f, 0), new Vector2(0.52f, 0), new Vector2(8, 20), new Vector2(-4, 52));
            selectedOnly = Button(panel, Plugin.Text("Только включённые", "Show object only"), () =>
            {
                window.SelectedOnly = !window.SelectedOnly;
                if (window.SelectedOnly)
                {
                    window.KeepOnly = false; window.Category = ""; window.Search = ""; window.FocusedKey = null;
                    search.SetTextWithoutNotify(""); HighlightCategory();
                }
                Refresh(); list.verticalNormalizedPosition = 1;
                HighlightKeepOnly();
            });
            Region((RectTransform)selectedOnly.transform, new Vector2(0.52f, 0), new Vector2(0.76f, 0), new Vector2(4, 20), new Vector2(-4, 52));
            footer = Label("", panel, 13, Beige, TextAnchor.MiddleLeft);
            Region(footer.rectTransform, Vector2.zero, new Vector2(0.28f, 0), new Vector2(26, 12), new Vector2(-8, 56));
            list.onValueChanged.AddListener(_ => RenderRows());
            needsRefresh = true; previousCount = -1;
            HighlightKeepOnly();
            return true;
        }
        private void HighlightKeepOnly()
        {
            HighlightViewButton(keepOnly, window.KeepOnly);
            HighlightViewButton(selectedOnly, window.SelectedOnly);
        }
        private static void HighlightViewButton(Button button, bool active)
        {
            if (button == null) return;
            var colors = button.colors;
            colors.normalColor = active ? new Color(1, 0.75f, 0.38f) : Color.white;
            colors.highlightedColor = active ? new Color(1, 0.85f, 0.55f) : new Color(1, 0.9f, 0.7f);
            colors.selectedColor = colors.normalColor; button.colors = colors;
        }
        private Sprite FindSprite(SpriteAtlas[] atlases, string name)
        {
            foreach (var atlas in atlases.OrderByDescending(a => a.name == "UIAtlas"))
            {
                var sprite = atlas.GetSprite(name);
                if (sprite != null) { ownedSprites.Add(sprite); return sprite; }
            }
            return Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == name || s.name == name + "(Clone)");
        }
        private void RebuildCategories()
        {
            foreach (var category in categories) UnityEngine.Object.Destroy(category);
            categories.Clear(); categoryKeys.Clear();
            var groups = new List<string> { "" };
            groups.AddRange(window.Host.Catalog.Entries.Where(e => !e.technical && !CatalogContent.IsServiceDefinition(e)).Select(e => e.category).Distinct().OrderBy(FilterWindow.GroupName));
            for (int i = 0; i < groups.Count; i++)
            {
                string group = groups[i];
                Button button = Button(categoryContent, FilterWindow.GroupName(group), () =>
                {
                    window.Category = group; window.Search = ""; search.SetTextWithoutNotify(""); window.FocusedKey = null;
                    Refresh(); HighlightCategory(); list.verticalNormalizedPosition = 1;
                });
                StretchTop((RectTransform)button.transform, 4, i * 42, 4, 38);
                categories.Add(button.gameObject);
                categoryKeys.Add(group);
            }
            categoryContent.sizeDelta = new Vector2(0, groups.Count * 42);
            HighlightCategory();
        }
        private void HighlightCategory()
        {
            for (int i = 0; i < categories.Count; i++)
            {
                var button = categories[i].GetComponent<Button>();
                bool active = categoryKeys[i] == window.Category;
                var colors = button.colors;
                colors.normalColor = active ? new Color(1, 0.75f, 0.38f) : Color.white;
                colors.highlightedColor = active ? new Color(1, 0.85f, 0.55f) : new Color(1, 0.9f, 0.7f);
                colors.selectedColor = active ? new Color(1, 0.85f, 0.55f) : Color.white;
                button.colors = colors;
                categories[i].GetComponentInChildren<Text>().color = active ? Color.white : Orange;
            }
        }
        private void RenderRows()
        {
            if (list == null || list.viewport == null) return;
            int wanted = Mathf.Min(48, Mathf.CeilToInt(list.viewport.rect.height / RowHeight) + 2);
            while (rows.Count < wanted) { rows.Add(CreateRow()); first = -1; }
            int start = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0, listContent.anchoredPosition.y) / RowHeight), 0, entries.Count);
            if (first == start && !needsRefresh) return;
            first = start; updating = true;
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    Row row = rows[i]; int index = start + i;
                    row.Root.gameObject.SetActive(i < wanted && index < entries.Count);
                    if (index >= entries.Count || i >= wanted) continue;
                    row.Entry = entries[index];
                    StretchTop(row.Root, 4, index * RowHeight, 4, RowHeight - 2);
                    row.Toggle.SetIsOnWithoutNotify(row.Entry.Selected);
                    row.Keep.interactable = row.Entry.CanKeep;
                    row.Keep.SetIsOnWithoutNotify(row.Entry.KeepVisible);
                    row.Notify.interactable = row.Entry.Members.Exists(e => e.category != "Dropped items");
                    row.Notify.SetIsOnWithoutNotify(row.Entry.Members.Exists(e => window.Host.Tracking.Discoveries.Notifies(e.key)));
                    row.Text.text = row.Entry.Label;
                    bool focused = row.Entry.Contains(window.FocusedKey);
                    row.Root.GetComponent<Image>().color = focused ? new Color(0.35f, 0.22f, 0.07f, 0.95f) : new Color(0, 0, 0, 0.2f);
                    row.Text.color = focused ? Orange : Beige;
                    row.Icon.sprite = row.Entry.Icon ?? CategoryIcon(row.Entry.Members[0].category);
                    bool hasSymbol = DungeonIcon.HasSymbol(row.Entry.Members[0]);
                    row.Icon.gameObject.SetActive(hasSymbol || row.Icon.sprite != null);
                    row.Icon.enabled = !hasSymbol;
                    row.Dungeon.gameObject.SetActive(hasSymbol);
                    if (hasSymbol) { row.Dungeon.Kind = DungeonIcon.Kind(row.Entry.Members[0].prefab); row.Dungeon.SetVerticesDirty(); }
                }
            }
            finally { updating = false; }
        }
        private Row CreateRow()
        {
            var row = new Row { Root = Rect("Type", listContent) };
            Image bg = row.Root.gameObject.AddComponent<Image>(); bg.color = new Color(0, 0, 0, 0.2f);
            row.Toggle = row.Root.gameObject.AddComponent<Toggle>(); row.Toggle.targetGraphic = bg;
            var box = Rect("Checkbox", row.Root);
            Pin(box, new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(25, 25));
            Image boxImage = box.gameObject.AddComponent<Image>(); boxImage.sprite = checkboxSprite; boxImage.type = Image.Type.Sliced;
            var check = Rect("Check", box); Stretch(check, 3, 3, 3, 3);
            Image checkImage = check.gameObject.AddComponent<Image>(); checkImage.sprite = checkSprite; checkImage.color = Orange;
            row.Toggle.graphic = checkImage;
            var icon = Rect("Icon", row.Root);
            Pin(icon, new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(28, 28));
            row.Icon = icon.gameObject.AddComponent<Image>(); row.Icon.preserveAspect = true; row.Icon.raycastTarget = false;
            var dungeonIcon = Rect("DungeonIcon", icon); Stretch(dungeonIcon, 0, 0, 0, 0);
            row.Dungeon = dungeonIcon.gameObject.AddComponent<DungeonGraphic>(); row.Dungeon.color = Color.white; row.Dungeon.raycastTarget = false;
            dungeonIcon.gameObject.SetActive(false);
            row.Text = Label("", row.Root, 17, Beige, TextAnchor.MiddleLeft);
            Stretch(row.Text.rectTransform, 80, 0, 198, 0);
            var remember = Rect("KeepDiscovered", row.Root);
            Pin(remember, new Vector2(1, 0.5f), new Vector2(-134, 0), new Vector2(25, 25));
            var rememberImage = remember.gameObject.AddComponent<Image>(); rememberImage.sprite = checkboxSprite; rememberImage.type = Image.Type.Sliced;
            row.Keep = remember.gameObject.AddComponent<Toggle>(); row.Keep.targetGraphic = rememberImage;
            var remembered = Rect("Checked", remember); Stretch(remembered, 3, 3, 3, 3);
            var rememberedImage = remembered.gameObject.AddComponent<Image>(); rememberedImage.sprite = checkSprite; rememberedImage.color = Orange;
            row.Keep.graphic = rememberedImage;
            var notify = Rect("NotifyDiscovery", row.Root);
            Pin(notify, new Vector2(1, 0.5f), new Vector2(-45, 0), new Vector2(25, 25));
            var notifyImage = notify.gameObject.AddComponent<Image>(); notifyImage.sprite = checkboxSprite; notifyImage.type = Image.Type.Sliced;
            row.Notify = notify.gameObject.AddComponent<Toggle>(); row.Notify.targetGraphic = notifyImage;
            var notifyCheck = Rect("Checked", notify); Stretch(notifyCheck, 3, 3, 3, 3);
            var notifyCheckImage = notifyCheck.gameObject.AddComponent<Image>(); notifyCheckImage.sprite = checkSprite; notifyCheckImage.color = Orange;
            row.Notify.graphic = notifyCheckImage;
            row.Notify.onValueChanged.AddListener(value => { if (!updating && row.Entry != null) window.Host.Tracking.Discoveries.SetNotify(row.Entry.Members, value); });
            row.Keep.onValueChanged.AddListener(value => { if (!updating && row.Entry != null) window.SetKeepVisible(row.Entry, value); });
            row.Toggle.onValueChanged.AddListener(value =>
            { if (!updating && row.Entry != null) window.SetSelected(row.Entry, value); });
            return row;
        }
        private static Sprite CategoryIcon(string category)
        {
            Minimap map = Minimap.instance;
            if (map == null) return null;
            Minimap.PinType type = category == "Dungeons" ? Minimap.PinType.Death :
                category == "Player structures" || category == "Portals" || category == "Locations" ? Minimap.PinType.Icon1 : Minimap.PinType.Icon3;
            return map.m_icons.FirstOrDefault(icon => icon.m_name == type).m_icon;
        }
        private ScrollRect Scroll(RectTransform parent, out RectTransform content)
        {
            parent.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.02f, 0.01f, 0.85f);
            ScrollRect scroll = parent.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 0;
            var viewport = Rect("Viewport", parent); Stretch(viewport, 2, 2, 17, 2);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            scroll.viewport = viewport; scroll.content = content;
            var bar = Rect("Scrollbar", parent);
            Region(bar, new Vector2(1, 0), Vector2.one, new Vector2(-13, 3), new Vector2(-3, -3));
            bar.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.08f, 0.05f);
            Scrollbar scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            var handle = Rect("Handle", bar); Stretch(handle, 0, 0, 0, 0);
            Image handleImage = handle.gameObject.AddComponent<Image>(); handleImage.sprite = buttonSprite; handleImage.color = Beige;
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage; scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            return scroll;
        }
        private static float NativeScrollSensitivity()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null) return -1;
            ScrollRect original = null;
            if (gui.m_achievementsListRoot != null) original = gui.m_achievementsListRoot.GetComponentInParent<ScrollRect>(true);
            if (original == null && gui.m_recipeListRoot != null) original = gui.m_recipeListRoot.GetComponentInParent<ScrollRect>(true);
            return original != null ? original.scrollSensitivity : -1;
        }
        private void HandleScroll()
        {
            if (presetRoot != null && presetRoot.activeSelf) return;
            if (nativeScroll) return;
            float delta = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(delta) < 0.0001f) return;
            ScrollRect target = null;
            if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)list.transform, ZInput.pointerPosition, null)) target = list;
            else if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)categoryList.transform, ZInput.pointerPosition, null)) target = categoryList;
            if (target == null) return;
            float maximum = Mathf.Max(0, target.content.rect.height - target.viewport.rect.height);
            if (maximum <= 0) return;
            float y = target.content.anchoredPosition.y - Mathf.Sign(delta) * 80f;
            target.StopMovement();
            target.content.anchoredPosition = new Vector2(target.content.anchoredPosition.x, Mathf.Clamp(y, 0, maximum));
        }
        private Button Button(Transform parent, string caption, UnityAction action)
        {
            RectTransform rect = Rect("Button", parent);
            Image image = rect.gameObject.AddComponent<Image>(); image.sprite = buttonSprite; image.type = Image.Type.Sliced;
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1, 0.9f, 0.7f); colors.pressedColor = Orange; button.colors = colors;
            button.onClick.AddListener(action);
            Text text = Label(caption, rect, 16, Orange, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 4, 1, 4, 1);
            return button;
        }
        private InputField Search(Transform parent)
        {
            RectTransform rect = Rect("Search", parent);
            Image image = rect.gameObject.AddComponent<Image>(); image.sprite = fieldSprite; image.type = Image.Type.Sliced;
            InputField input = rect.gameObject.AddComponent<InputField>(); input.targetGraphic = image;
            Text text = Label("", rect, 17, Beige, TextAnchor.MiddleLeft); Stretch(text.rectTransform, 12, 2, 12, 2);
            Text placeholder = Label(Plugin.Text("Поиск по названию…", "Search by name…"), rect, 17, new Color(0.55f, 0.49f, 0.4f), TextAnchor.MiddleLeft);
            Stretch(placeholder.rectTransform, 12, 2, 12, 2);
            input.textComponent = text; input.placeholder = placeholder; input.text = window.Search;
            return input;
        }
        private Text Label(string value, Transform parent, int size, Color color, TextAnchor alignment, bool title = false)
        {
            RectTransform rect = Rect("Text", parent);
            Text text = rect.gameObject.AddComponent<Text>(); text.text = value;
            text.font = title ? titleFont : bodyFont; text.fontSize = size; text.color = color; text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false;
            Outline outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(0, 0, 0, 0.85f); outline.effectDistance = new Vector2(1, -1);
            return text;
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false); return (RectTransform)go.transform;
        }
        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        { Region(rect, Vector2.zero, Vector2.one, new Vector2(left, bottom), new Vector2(-right, -top)); }
        private static void StretchTop(RectTransform rect, float left, float top, float right, float height)
        { Region(rect, new Vector2(0, 1), Vector2.one, new Vector2(left, -top - height), new Vector2(-right, -top)); }
        private static void Region(RectTransform rect, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high; }
        private static void Pin(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = size; }
        internal void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            foreach (var sprite in ownedSprites) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            ownedSprites.Clear(); categories.Clear(); categoryKeys.Clear(); rows.Clear(); entries.Clear();
            presetButtons.Clear(); presetActive.Clear(); discoveryRows.Clear(); discoveries.Clear();
            root = null; panel = listContent = categoryContent = null;
            list = categoryList = null; summary = footer = null; search = null; keepOnly = selectedOnly = null;
            presetRoot = null; presetContent = null; presetScroll = null; presetName = null; presetStatus = null;
            presetLoad = presetSave = presetDelete = null; selectedPreset = null; builtInPreset = null;
            discoveryRoot = null; discoveryContent = null; discoveryScroll = null; discoverySearch = null;
            discoveryStatus = null; favoriteFilter = null; favoritesOnly = false;
            previousCount = first = -1; categorySignature = pendingFocus = null;
            nextRefresh = nextCatalogRefresh = nextDiscoveryRefresh = 0; needsRefresh = true; updating = false;
        }
    }
    public sealed class FilterPanelDrag : MonoBehaviour, IDragHandler
    {
        internal RectTransform Target;
        public void OnDrag(PointerEventData pointer)
        {
            if (Target == null) return;
            Canvas canvas = Target.GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1;
            Target.anchoredPosition += pointer.delta / Mathf.Max(scale, 0.01f);
            if (Target.parent is RectTransform parent)
            {
                float x = Mathf.Max(0, (parent.rect.width - Target.rect.width) * 0.5f);
                float y = Mathf.Max(0, (parent.rect.height - Target.rect.height) * 0.5f);
                Target.anchoredPosition = new Vector2(Mathf.Clamp(Target.anchoredPosition.x, -x, x), Mathf.Clamp(Target.anchoredPosition.y, -y, y));
            }
        }
    }
}
