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
        private int discoveryStatusCount = -1;
        private bool discoveryStatusPending;
        private Button favoriteFilter;
        private bool favoritesOnly;
        private float nextDiscoveryRefresh;
        private readonly List<DiscoveryRow> discoveryRows = new List<DiscoveryRow>();
        private List<MarkerRecord> discoveries = new List<MarkerRecord>();
        private sealed class DiscoveryRow
        {
            internal RectTransform Root;
            internal MarkerRecord Record;
            internal Text Caption, FavoriteText, TargetText, NotifyText;
            internal Image Background;
            internal Button Favorite, Target, Notify;
            internal int Index = -1, Distance = int.MinValue;
            internal string Name, Category;
            internal bool Completed, Visited;
        }
        private void OpenDiscoveries()
        {
            ClosePresets();
            if (discoveryRoot == null) BuildDiscoveries();
            discoveryRoot.SetActive(true); discoveryRoot.transform.SetAsLastSibling(); nextDiscoveryRefresh = 0;
        }
        private void CloseDiscoveries() { window.Host.Tracking.Discoveries.CancelSearch(); if (discoveryRoot != null) { discoveryRoot.SetActive(false); Refresh(); } }
        private void RefreshDiscoveries() { window.Host.Tracking.Discoveries.CancelSearch(); nextDiscoveryRefresh = 0; }
        private bool CurrentDiscovery(MarkerRecord record) => window.Host.Tracking.Contains(record) && DiscoverySearch.Eligible(record, favoritesOnly);
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
            discoverySearch.onValueChanged.AddListener(_ => { RefreshDiscoveries(); discoveryScroll.verticalNormalizedPosition = 1; });
            var area = Rect("DiscoveryList", overlay); Stretch(area, 24, 112, 24, 70);
            discoveryScroll = Scroll(area, out discoveryContent); discoveryScroll.scrollSensitivity = nativeScroll ? list.scrollSensitivity : 20;
            favoriteFilter = Button(overlay, "Favorites only: off", () =>
            { favoritesOnly = !favoritesOnly; RefreshDiscoveries(); favoriteFilter.GetComponentInChildren<Text>().text = "Favorites only: " + (favoritesOnly ? "on" : "off"); });
            Pin((RectTransform)favoriteFilter.transform, Vector2.zero, new Vector2(24, 22), new Vector2(200, 34));
            var clear = Button(overlay, "Clear target", () => window.Host.Tracking.Discoveries.ClearTarget());
            Pin((RectTransform)clear.transform, new Vector2(1, 0), new Vector2(-24, 22), new Vector2(160, 34));
            discoveryStatus = Label("", overlay, 13, Beige, TextAnchor.MiddleCenter);
            discoveryStatusCount = -1;
            Region(discoveryStatus.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(240, 22), new Vector2(-196, 56));
        }
        private void TickDiscoveries()
        {
            if (discoveryRoot == null || !discoveryRoot.activeSelf) return;
            var tools = window.Host.Tracking.Discoveries;
            if (!tools.SearchPending && Time.realtimeSinceStartup >= nextDiscoveryRefresh)
            {
                tools.BeginSearch(discoverySearch.text, favoritesOnly);
            }
            bool wasPending = tools.SearchPending;
            tools.AdvanceSearch();
            if (!ReferenceEquals(discoveries, tools.SearchResults))
            {
                discoveries = tools.SearchResults;
                discoveryContent.sizeDelta = new Vector2(0, discoveries.Count * 56);
            }
            if (wasPending && !tools.SearchPending) nextDiscoveryRefresh = Time.realtimeSinceStartup + 0.5f;
            if (discoveryStatusCount != discoveries.Count || discoveryStatusPending != tools.SearchPending)
            {
                discoveryStatusCount = discoveries.Count; discoveryStatusPending = tools.SearchPending;
                discoveryStatus.text = discoveries.Count + " discovered locations" + (tools.SearchPending ? " · Searching…" : "");
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
                if (!CurrentDiscovery(r)) { row.Record = null; row.Root.gameObject.SetActive(false); continue; }
                if (row.Index != index) { row.Index = index; StretchTop(row.Root, 2, index * 56, 2, 54); }
                string name = window.Host.Tracking.Name(r);
                int distance = Mathf.RoundToInt(DiscoveryQuery.Distance(r, Player.m_localPlayer.transform.position));
                if (row.Name != name || row.Distance != distance || row.Category != r.entry.category || row.Completed != r.completed || row.Visited != r.visited)
                {
                    row.Name = name; row.Distance = distance; row.Category = r.entry.category; row.Completed = r.completed; row.Visited = r.visited;
                    row.Caption.text = name + "\n" + distance + " m · " + r.entry.category + (r.completed ? " · Completed" : r.visited ? " · Visited" : "");
                }
                bool target = tools.IsTarget(r);
                row.Background.color = target ? new Color(0.25f, 0.2f, 0.05f, 1) : new Color(0, 0, 0, 0.2f);
                row.FavoriteText.text = r.favorite ? "Fav+" : "Fav";
                row.TargetText.text = target ? "Target*" : "Target";
                row.NotifyText.text = tools.Notifies(r.type) ? "Notify*" : "Notify";
            }
        }
        private DiscoveryRow CreateDiscoveryRow()
        {
            var row = new DiscoveryRow { Root = Rect("DiscoveryRow", discoveryContent) };
            row.Background = row.Root.gameObject.AddComponent<Image>(); row.Background.color = new Color(0, 0, 0, 0.2f);
            row.Caption = Label("", row.Root, 15, Beige, TextAnchor.MiddleLeft); row.Caption.supportRichText = false;
            Stretch(row.Caption.rectTransform, 10, 2, 300, 2);
            row.Favorite = Button(row.Root, "Fav", () => { if (CurrentDiscovery(row.Record)) window.Host.Tracking.Discoveries.Favorite(row.Record); RefreshDiscoveries(); });
            Pin((RectTransform)row.Favorite.transform, new Vector2(1, 0.5f), new Vector2(-252, 0), new Vector2(38, 34));
            var locate = Button(row.Root, "Map", () => { if (CurrentDiscovery(row.Record)) window.Host.Tracking.Discoveries.Locate(row.Record); });
            Pin((RectTransform)locate.transform, new Vector2(1, 0.5f), new Vector2(-190, 0), new Vector2(70, 34));
            row.Target = Button(row.Root, "Target", () => { if (CurrentDiscovery(row.Record)) window.Host.Tracking.Discoveries.Select(row.Record); });
            Pin((RectTransform)row.Target.transform, new Vector2(1, 0.5f), new Vector2(-110, 0), new Vector2(82, 34));
            row.Notify = Button(row.Root, "Notify", () => { if (CurrentDiscovery(row.Record)) window.Host.Tracking.Discoveries.ToggleNotify(row.Record.type); });
            Pin((RectTransform)row.Notify.transform, new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(82, 34));
            row.FavoriteText = row.Favorite.GetComponentInChildren<Text>();
            row.TargetText = row.Target.GetComponentInChildren<Text>();
            row.NotifyText = row.Notify.GetComponentInChildren<Text>();
            return row;
        }
    }
}
