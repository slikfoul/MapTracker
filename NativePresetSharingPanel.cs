using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MapTracker
{
    internal sealed partial class NativeFilterPanel
    {
        private GameObject sharingRoot;
        private string sharingMode, incomingId;
        private RectTransform sharingDialog, recipientsContent;
        private Text sharingText;
        private sealed class RecipientRow
        {
            internal Button Button;
            internal Text Label;
            internal PresetSharing.Recipient Recipient;
            internal int Index = -1;
        }
        private readonly PresetRecipientRows<RecipientRow> recipientRows = new PresetRecipientRows<RecipientRow>();
        private Button emptyRecipients;
        private FilterPreset sharingPreset;
        private float nextPlayers;
        private RectTransform recipientHint, recipientHintSource;
        private Text recipientHintText;
        internal bool SharingModalOpen => sharingRoot != null && sharingRoot.activeInHierarchy;
        private void CloseSharing()
        {
            HideRecipientHint();
            if (sharingRoot != null) { sharingRoot.SetActive(false); UnityEngine.Object.Destroy(sharingRoot); }
            sharingRoot = null; sharingDialog = recipientsContent = null; sharingText = null;
            sharingMode = incomingId = null; sharingPreset = null; recipientRows.Clear(); emptyRecipients = null;
        }
        internal bool DismissSharing()
        {
            if (!SharingModalOpen) return false;
            if (sharingMode == "incoming" && window.Host.PresetSharing.Transfer?.Pending?.Accepted != true)
                window.Host.PresetSharing.Decide(false);
            CloseSharing(); return true;
        }
        private bool SharingFrame(string title, string mode, float height)
        {
            CloseSharing();
            if (root == null) { if (!Build()) return false; root.SetActive(window.Open); }
            Canvas canvas = Minimap.instance?.m_largeRoot?.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return false;
            sharingMode = mode;
            var overlay = Rect("MapTrackerPresetShare", canvas.transform); Stretch(overlay, 0, 0, 0, 0);
            sharingRoot = overlay.gameObject;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);
            sharingDialog = Rect("Dialog", overlay);
            sharingDialog.anchorMin = sharingDialog.anchorMax = sharingDialog.pivot = new Vector2(0.5f, 0.5f);
            sharingDialog.sizeDelta = new Vector2(520, height);
            sharingDialog.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.07f, 0.04f, 1);
            var frame = Rect("WoodFrame", sharingDialog); Stretch(frame, 0, 0, 0, 0);
            var wood = frame.gameObject.AddComponent<Image>(); wood.sprite = panelSprite; wood.type = Image.Type.Sliced;
            wood.material = panelMaterial; wood.raycastTarget = false;
            var heading = Label(title, sharingDialog, 28, Orange, TextAnchor.MiddleCenter, true);
            StretchTop(heading.rectTransform, 35, 15, 35, 42);
            sharingText = Label("", sharingDialog, 17, Beige, TextAnchor.UpperCenter);
            sharingText.supportRichText = false; StretchTop(sharingText.rectTransform, 25, 65, 25, 88);
            return true;
        }
        private void OpenShareRecipients(FilterPreset preset)
        {
            if (!SharingFrame("Share preset", "recipients", 440)) return;
            sharingPreset = preset; sharingText.text = "Choose an online player for “" + preset.name + "”.\nThe player must have a version of MapTracker that supports preset sharing.";
            var area = Rect("OnlinePlayers", sharingDialog); Stretch(area, 25, 160, 25, 70);
            var scroll = Scroll(area, out recipientsContent); scroll.scrollSensitivity = nativeScroll ? list.scrollSensitivity : 20;
            var cancel = Button(sharingDialog, "Cancel", CloseSharing);
            Region((RectTransform)cancel.transform, Vector2.zero, new Vector2(1, 0), new Vector2(25, 22), new Vector2(-25, 55));
            window.Host.PresetSharing.CheckRecipients(true);
            nextPlayers = Time.realtimeSinceStartup + 1; RefreshSharePlayers();
        }
        private void RefreshSharePlayers()
        {
            window.Host.PresetSharing.CheckRecipients();
            var players = window.Host.PresetSharing.Recipients();
            recipientRows.Refresh(players, CreateRecipientRow, UpdateRecipientRow, RemoveRecipientRow);
            if (players.Count == 0 && emptyRecipients == null)
            {
                emptyRecipients = Button(recipientsContent, "No other players online", () => { });
                emptyRecipients.interactable = false;
                StretchTop((RectTransform)emptyRecipients.transform, 0, 0, 0, 38);
            }
            else if (players.Count != 0 && emptyRecipients != null)
            {
                emptyRecipients.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(emptyRecipients.gameObject); emptyRecipients = null;
            }
            var size = new Vector2(0, Mathf.Max(1, players.Count) * 42);
            if (recipientsContent.sizeDelta != size) recipientsContent.sizeDelta = size;
        }
        private RecipientRow CreateRecipientRow(PresetSharing.Recipient recipient)
        {
            var row = new RecipientRow();
            row.Button = Button(recipientsContent, recipient.Name, () => PresetAction(() =>
            {
                if (window.Host.PresetSharing.Send(row.Recipient.Id, sharingPreset))
                { CloseSharing(); presetStatus.text = "Waiting for the player to accept…"; }
                else sharingText.text = "The player is unavailable or does not support preset sharing.";
            }));
            row.Label = row.Button.GetComponentInChildren<Text>(); row.Label.supportRichText = false;
            var rect = (RectTransform)row.Button.transform;
            var hover = row.Button.gameObject.AddComponent<PresetRecipientHover>();
            hover.Enter = () => ShowRecipientHint(row.Recipient.Hint, rect);
            hover.Exit = () => { if (recipientHintSource == rect) HideRecipientHint(); };
            return row;
        }
        private void UpdateRecipientRow(RecipientRow row, PresetSharing.Recipient recipient, int index)
        {
            var previous = row.Recipient;
            row.Recipient = recipient;
            if (previous == null || previous.Name != recipient.Name) row.Label.text = recipient.Name;
            if (previous == null || previous.State != recipient.State)
            {
                row.Label.color = recipient.CanShare ? Orange : new Color(0.52f, 0.52f, 0.52f);
                row.Button.interactable = recipient.CanShare;
                if (recipientHintSource == (RectTransform)row.Button.transform)
                    ShowRecipientHint(recipient.Hint, recipientHintSource);
            }
            if (row.Index != index)
            {
                row.Index = index;
                StretchTop((RectTransform)row.Button.transform, 0, index * 42, 0, 38);
                row.Button.transform.SetSiblingIndex(index);
                if (recipientHintSource == (RectTransform)row.Button.transform) PositionRecipientHint(recipientHintSource);
            }
        }
        private void RemoveRecipientRow(RecipientRow row)
        {
            if (recipientHintSource == (RectTransform)row.Button.transform) HideRecipientHint();
            // Destroy is deferred until the end of the frame. Hide departed rows
            // immediately, before another row occupies the same position.
            row.Button.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(row.Button.gameObject);
        }
        private void HideRecipientHint()
        {
            if (recipientHint != null) { recipientHint.gameObject.SetActive(false); UnityEngine.Object.Destroy(recipientHint.gameObject); }
            recipientHint = recipientHintSource = null; recipientHintText = null;
        }
        private void ShowRecipientHint(string text, RectTransform source)
        {
            if (sharingRoot == null || sharingMode != "recipients") return;
            if (recipientHint != null && recipientHintSource == source)
            {
                if (recipientHintText.text != text) recipientHintText.text = text;
                PositionRecipientHint(source);
                return;
            }
            HideRecipientHint();
            var parent = (RectTransform)sharingRoot.transform;
            recipientHint = Rect("PlayerPresetHint", parent);
            recipientHintSource = source;
            recipientHint.anchorMin = recipientHint.anchorMax = new Vector2(0.5f, 0.5f);
            recipientHint.pivot = new Vector2(0.5f, 1);
            PositionRecipientHint(source);
            var background = recipientHint.gameObject.AddComponent<Image>();
            background.color = new Color(0.10f, 0.07f, 0.04f, 1); background.raycastTarget = false;
            var frame = Rect("WoodFrame", recipientHint); Stretch(frame, 0, 0, 0, 0);
            var wood = frame.gameObject.AddComponent<Image>(); wood.sprite = panelSprite; wood.type = Image.Type.Sliced;
            wood.material = panelMaterial; wood.raycastTarget = false;
            var label = recipientHintText = Label(text, recipientHint, 15, Beige, TextAnchor.MiddleLeft);
            label.supportRichText = false; Stretch(label.rectTransform, 16, 10, 16, 10);
            recipientHint.SetAsLastSibling();
        }
        private void PositionRecipientHint(RectTransform source)
        {
            var parent = (RectTransform)sharingRoot.transform;
            float width = Mathf.Min(390, parent.rect.width - 30);
            recipientHint.sizeDelta = new Vector2(width, 76);
            Vector3 point = parent.InverseTransformPoint(source.TransformPoint(new Vector3(source.rect.center.x, source.rect.yMin, 0)));
            float x = Mathf.Clamp(point.x, parent.rect.xMin + width * 0.5f + 12, parent.rect.xMax - width * 0.5f - 12);
            float y = Mathf.Max(parent.rect.yMin + 88, point.y - 8);
            recipientHint.anchoredPosition = new Vector2(x, y) - parent.rect.center;
        }
        private void ShowPresetOffer(PresetTransfer.Incoming incoming)
        {
            if (!SharingFrame("Shared preset", "incoming", 275)) return;
            incomingId = incoming.Offer.id;
            sharingText.text = incoming.Sender + " wants to share the preset “" + incoming.Offer.name + "” with you.\nAccepting adds it to your list without applying it.";
            var accept = Button(sharingDialog, "Accept", () => { window.Host.PresetSharing.Decide(true); CloseSharing(); });
            var decline = Button(sharingDialog, "Decline", () => { window.Host.PresetSharing.Decide(false); CloseSharing(); });
            Region((RectTransform)accept.transform, Vector2.zero, new Vector2(0.5f, 0), new Vector2(25, 25), new Vector2(-6, 62));
            Region((RectTransform)decline.transform, new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(6, 25), new Vector2(-25, 62));
        }
        internal void TickSharing()
        {
            var transfer = window.Host.PresetSharing?.Transfer;
            if (transfer == null) { CloseSharing(); DisposePresetProgress(); return; }
            TickPresetProgress(transfer);
            if (sharingMode == "incoming" && (transfer.Pending == null || transfer.Pending.Accepted)) CloseSharing();
            if (transfer.Pending != null && !transfer.Pending.Accepted && incomingId != transfer.Pending.Offer.id)
                ShowPresetOffer(transfer.Pending);
            if (transfer.Notice != null)
            {
                string notice = transfer.Notice;
                if (SharingFrame("Shared preset", "notice", 250))
                {
                    sharingText.text = notice; transfer.Notice = null;
                    if (presetRoot != null && presetRoot.activeSelf) PresetAction(RefreshPresets);
                    var close = Button(sharingDialog, "Close", CloseSharing);
                    Region((RectTransform)close.transform, Vector2.zero, new Vector2(1, 0), new Vector2(25, 25), new Vector2(-25, 62));
                }
            }
            if (sharingRoot == null) return;
            if (sharingMode == "recipients" && Time.realtimeSinceStartup >= nextPlayers)
            { RefreshSharePlayers(); nextPlayers = Time.realtimeSinceStartup + 1; }
            bool visible = !Menu.IsVisible() && !InventoryGui.IsVisible() && !global::Console.IsVisible() &&
                Chat.instance?.HasFocus() != true && Player.m_localPlayer != null && !Player.m_localPlayer.IsDead() && !Player.m_localPlayer.IsTeleporting();
            sharingRoot.SetActive(visible);
            if (!visible) return;
            sharingRoot.transform.SetAsLastSibling();
            if (sharingRoot.transform.parent is RectTransform parent)
                sharingDialog.sizeDelta = new Vector2(Mathf.Min(520, parent.rect.width - 30), Mathf.Min(sharingMode == "recipients" ? 440 : 275, parent.rect.height - 30));
        }
    }
    public sealed class PresetRecipientHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal Action Enter, Exit;
        public void OnPointerEnter(PointerEventData data) { Enter?.Invoke(); }
        public void OnPointerExit(PointerEventData data) { Exit?.Invoke(); }
        private void OnDisable() { Exit?.Invoke(); }
    }
    public sealed class SharePresetGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = GetPixelAdjustedRect();
            var a = new Vector2(rect.xMin + rect.width * 0.20f, rect.center.y);
            var b = new Vector2(rect.xMin + rect.width * 0.78f, rect.yMin + rect.height * 0.80f);
            var c = new Vector2(b.x, rect.yMin + rect.height * 0.20f);
            float radius = Mathf.Min(rect.width, rect.height) * 0.14f;
            Edge(mesh, a, b, radius * 0.55f); Edge(mesh, a, c, radius * 0.55f);
            Dot(mesh, a, radius); Dot(mesh, b, radius); Dot(mesh, c, radius);
        }
        private void Edge(VertexHelper mesh, Vector2 a, Vector2 b, float width)
        {
            Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x) * width;
            int start = mesh.currentVertCount;
            mesh.AddVert(a - n, color, Vector2.zero); mesh.AddVert(a + n, color, Vector2.zero);
            mesh.AddVert(b + n, color, Vector2.zero); mesh.AddVert(b - n, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
        private void Dot(VertexHelper mesh, Vector2 center, float radius)
        {
            int start = mesh.currentVertCount; mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
            }
            for (int i = 0; i < 12; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 12);
        }
    }
}
