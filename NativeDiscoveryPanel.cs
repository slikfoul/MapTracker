using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed partial class NativeFilterPanel
    {
        private GameObject discoveryRoot;
        private RectTransform discoveryContent;
        private ScrollRect discoveryScroll;
        private InputField discoverySearch;
        private Text discoveryStatus;
        private Button favoriteFilter;
        private bool favoritesOnly;
        private float nextDiscoveryRefresh;
        private readonly List<DiscoveryRow> discoveryRows = new List<DiscoveryRow>();
        private List<MarkerRecord> discoveries = new List<MarkerRecord>();
        private sealed class DiscoveryRow { internal RectTransform Root; internal MarkerRecord Record; internal Text Caption; internal Button Favorite, Target, Notify; }
        private void OpenDiscoveries()
        {
            ClosePresets();
            if (discoveryRoot == null) BuildDiscoveries();
            discoveryRoot.SetActive(true); discoveryRoot.transform.SetAsLastSibling(); nextDiscoveryRefresh = 0;
        }
        private void CloseDiscoveries() { if (discoveryRoot != null) { discoveryRoot.SetActive(false); Refresh(); } }
        internal bool DismissDiscoveries()
        { if (discoveryRoot == null || !discoveryRoot.activeSelf) return false; CloseDiscoveries(); return true; }
        private void BuildDiscoveries()
        {
            var overlay = Rect("DiscoveredLocations", panel); Stretch(overlay, 0, 0, 0, 0); discoveryRoot = overlay.gameObject;
            overlay.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.07f, 0.04f, 1);
            var title = Label("Discoveries", overlay, 30, Orange, TextAnchor.MiddleCenter, true);
            StretchTop(title.rectTransform, 40, 12, 40, 40);
            var close = Button(overlay, "×", CloseDiscoveries);
            Pin((RectTransform)close.transform, Vector2.one, new Vector2(-20, -18), new Vector2(34, 34));
            discoverySearch = Search(overlay); ((Text)discoverySearch.placeholder).text = "Find a portal, dungeon or resource…";
            StretchTop((RectTransform)discoverySearch.transform, 24, 64, 24, 36);
            discoverySearch.onValueChanged.AddListener(_ => { nextDiscoveryRefresh = 0; discoveryScroll.verticalNormalizedPosition = 1; });
            var area = Rect("DiscoveryList", overlay); Stretch(area, 24, 112, 24, 70);
            discoveryScroll = Scroll(area, out discoveryContent); discoveryScroll.scrollSensitivity = nativeScroll ? list.scrollSensitivity : 20;
            favoriteFilter = Button(overlay, "Favorites only: off", () =>
            { favoritesOnly = !favoritesOnly; nextDiscoveryRefresh = 0; favoriteFilter.GetComponentInChildren<Text>().text = "Favorites only: " + (favoritesOnly ? "on" : "off"); });
            Pin((RectTransform)favoriteFilter.transform, Vector2.zero, new Vector2(24, 22), new Vector2(200, 34));
            var clear = Button(overlay, "Clear target", () => window.Host.Tracking.Discoveries.ClearTarget());
            Pin((RectTransform)clear.transform, new Vector2(1, 0), new Vector2(-24, 22), new Vector2(160, 34));
            discoveryStatus = Label("", overlay, 13, Beige, TextAnchor.MiddleCenter);
            Region(discoveryStatus.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(240, 22), new Vector2(-196, 56));
        }
        private void TickDiscoveries()
        {
            if (discoveryRoot == null || !discoveryRoot.activeSelf) return;
            var tools = window.Host.Tracking.Discoveries;
            if (Time.realtimeSinceStartup >= nextDiscoveryRefresh)
            {
                discoveries = tools.Search(discoverySearch.text, favoritesOnly);
                discoveryContent.sizeDelta = new Vector2(0, discoveries.Count * 56);
                discoveryStatus.text = discoveries.Count + " discovered locations";
                nextDiscoveryRefresh = Time.realtimeSinceStartup + 0.5f;
            }
            int wanted = Mathf.Min(24, Mathf.CeilToInt(discoveryScroll.viewport.rect.height / 56) + 2);
            while (discoveryRows.Count < wanted) discoveryRows.Add(CreateDiscoveryRow());
            int firstIndex = Mathf.Max(0, Mathf.FloorToInt(discoveryContent.anchoredPosition.y / 56));
            for (int i = 0; i < discoveryRows.Count; i++)
            {
                var row = discoveryRows[i]; int index = firstIndex + i;
                row.Root.gameObject.SetActive(i < wanted && index < discoveries.Count);
                if (index >= discoveries.Count || i >= wanted) continue;
                row.Record = discoveries[index]; var r = row.Record;
                StretchTop(row.Root, 2, index * 56, 2, 54);
                string caption = window.Host.Tracking.Name(r) + "\n" + Mathf.RoundToInt(DiscoveryQuery.Distance(r, Player.m_localPlayer.transform.position)) + " m · " +
                    r.entry.category + (r.completed ? " · Completed" : r.visited ? " · Visited" : "");
                if (row.Caption.text != caption) row.Caption.text = caption;
                row.Root.GetComponent<Image>().color = tools.IsTarget(r) ? new Color(0.25f, 0.2f, 0.05f, 1) : new Color(0, 0, 0, 0.2f);
                row.Favorite.GetComponentInChildren<Text>().text = r.favorite ? "Fav+" : "Fav";
                row.Target.GetComponentInChildren<Text>().text = tools.IsTarget(r) ? "Target*" : "Target";
                row.Notify.GetComponentInChildren<Text>().text = tools.Notifies(r.type) ? "Notify*" : "Notify";
            }
        }
        private DiscoveryRow CreateDiscoveryRow()
        {
            var row = new DiscoveryRow { Root = Rect("DiscoveryRow", discoveryContent) };
            row.Root.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.2f);
            row.Caption = Label("", row.Root, 15, Beige, TextAnchor.MiddleLeft); row.Caption.supportRichText = false;
            Stretch(row.Caption.rectTransform, 10, 2, 300, 2);
            row.Favorite = Button(row.Root, "Fav", () => { if (row.Record != null) window.Host.Tracking.Discoveries.Favorite(row.Record); nextDiscoveryRefresh = 0; });
            Pin((RectTransform)row.Favorite.transform, new Vector2(1, 0.5f), new Vector2(-252, 0), new Vector2(38, 34));
            var locate = Button(row.Root, "Map", () => { if (row.Record != null) window.Host.Tracking.Discoveries.Locate(row.Record); });
            Pin((RectTransform)locate.transform, new Vector2(1, 0.5f), new Vector2(-190, 0), new Vector2(70, 34));
            row.Target = Button(row.Root, "Target", () => { if (row.Record != null) window.Host.Tracking.Discoveries.Select(row.Record); });
            Pin((RectTransform)row.Target.transform, new Vector2(1, 0.5f), new Vector2(-110, 0), new Vector2(82, 34));
            row.Notify = Button(row.Root, "Notify", () => { if (row.Record != null) window.Host.Tracking.Discoveries.ToggleNotify(row.Record.type); });
            Pin((RectTransform)row.Notify.transform, new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(82, 34));
            return row;
        }
    }
}
