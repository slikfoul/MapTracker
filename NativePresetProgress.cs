using UnityEngine;
using UnityEngine.UI;

namespace MapTracker
{
    internal sealed partial class NativeFilterPanel
    {
        private RectTransform presetProgressRoot;
        private const float PresetProgressWidth = 440, PresetProgressHeight = 126, PresetProgressStep = 138;
        private sealed class PresetProgressRow
        {
            internal RectTransform Root, Fill;
            internal Image Color;
            internal Text Title, Caption, State, Percent;
            internal PresetTransfer.Progress Previous;
            internal string Phase;
            internal int Value = -1;
        }
        private PresetProgressRow outgoingProgressRow, incomingProgressRow;
        private void DisposePresetProgress()
        {
            if (presetProgressRoot != null) UnityEngine.Object.Destroy(presetProgressRoot.gameObject);
            presetProgressRoot = null; outgoingProgressRow = incomingProgressRow = null;
        }
        private void TickPresetProgress(PresetTransfer transfer)
        {
            bool outgoing = transfer.OutgoingProgress != null, incoming = transfer.IncomingProgress != null;
            if (!outgoing && !incoming)
            { if (presetProgressRoot != null) presetProgressRoot.gameObject.SetActive(false); return; }
            Canvas canvas = Minimap.instance?.m_largeRoot?.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return;
            if (presetProgressRoot != null && presetProgressRoot.parent != canvas.transform) DisposePresetProgress();
            if (root == null) { if (!Build()) return; root.SetActive(window.Open); }
            if (presetProgressRoot == null)
            {
                presetProgressRoot = Rect("PresetTransferProgress", canvas.transform);
                Pin(presetProgressRoot, new Vector2(0.5f, 0), new Vector2(0, 32), new Vector2(PresetProgressWidth, PresetProgressStep * 2));
                outgoingProgressRow = BuildPresetProgressRow(presetProgressRoot);
                incomingProgressRow = BuildPresetProgressRow(presetProgressRoot);
            }
            bool visible = !Menu.IsVisible() && !InventoryGui.IsVisible() && !global::Console.IsVisible() &&
                Chat.instance?.HasFocus() != true && Player.m_localPlayer != null && !Player.m_localPlayer.IsDead() && !Player.m_localPlayer.IsTeleporting();
            presetProgressRoot.gameObject.SetActive(visible);
            if (!visible) return;
            presetProgressRoot.SetAsLastSibling();
            if (canvas.transform is RectTransform parent)
                presetProgressRoot.sizeDelta = new Vector2(Mathf.Min(PresetProgressWidth, parent.rect.width - 40), PresetProgressStep * 2);
            UpdatePresetProgressRow(outgoingProgressRow, transfer.OutgoingProgress, incoming ? PresetProgressStep : 0, false);
            UpdatePresetProgressRow(incomingProgressRow, transfer.IncomingProgress, 0, true);
        }
        private PresetProgressRow BuildPresetProgressRow(RectTransform parent)
        {
            var row = new PresetProgressRow { Root = Rect("Transfer", parent) };
            var background = row.Root.gameObject.AddComponent<Image>();
            background.color = new Color(0.10f, 0.07f, 0.04f, 1); background.raycastTarget = false;
            var frame = Rect("WoodFrame", row.Root); Stretch(frame, 0, 0, 0, 0);
            var wood = frame.gameObject.AddComponent<Image>(); wood.sprite = panelSprite; wood.type = Image.Type.Sliced;
            wood.material = panelMaterial; wood.raycastTarget = false;
            row.Title = Label("", row.Root, 24, Orange, TextAnchor.MiddleLeft, true);
            StretchTop(row.Title.rectTransform, 22, 10, 76, 29);
            row.Caption = Label("", row.Root, 16, Beige, TextAnchor.MiddleLeft);
            row.Caption.supportRichText = false; StretchTop(row.Caption.rectTransform, 22, 42, 22, 23);
            row.State = Label("", row.Root, 13, Beige, TextAnchor.MiddleLeft);
            StretchTop(row.State.rectTransform, 22, 68, 22, 20);
            row.Percent = Label("", row.Root, 17, Orange, TextAnchor.MiddleRight);
            StretchTop(row.Percent.rectTransform, 22, 12, 22, 25);
            var track = Rect("Track", row.Root); StretchTop(track, 22, 94, 22, 18);
            var trackImage = track.gameObject.AddComponent<Image>(); trackImage.sprite = fieldSprite; trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color(0.5f, 0.43f, 0.32f); trackImage.raycastTarget = false;
            var inner = Rect("Inset", track); Stretch(inner, 3, 3, 3, 3);
            row.Fill = Rect("Progress", inner); Stretch(row.Fill, 0, 0, 0, 0);
            row.Color = row.Fill.gameObject.AddComponent<Image>(); row.Color.sprite = fieldSprite; row.Color.type = Image.Type.Sliced;
            row.Color.color = Orange; row.Color.raycastTarget = false;
            // These cards never capture mouse input or lock player movement.
            return row;
        }
        private void UpdatePresetProgressRow(PresetProgressRow row, PresetTransfer.Progress progress, float bottom, bool incoming)
        {
            row.Root.gameObject.SetActive(progress != null);
            if (progress == null) return;
            Region(row.Root, Vector2.zero, new Vector2(1, 0), new Vector2(0, bottom), new Vector2(0, bottom + PresetProgressHeight));
            int value = progress.Percent;
            if (row.Previous == progress && row.Value == value && row.Phase == progress.Phase) return;
            row.Previous = progress; row.Value = value; row.Phase = progress.Phase;
            row.Title.text = incoming ? "Receiving preset" : "Sending preset";
            row.State.text = progress.Phase;
            row.Caption.text = progress.Name + (incoming ? " — from " : " — to ") + progress.Player;
            row.Percent.text = value + "%";
            row.Fill.anchorMax = new Vector2(value / 100f, 1);
            row.Color.color = progress.Saved ? new Color(0.45f, 0.8f, 0.35f) : progress.Active ? Orange : new Color(0.8f, 0.35f, 0.25f);
        }
    }
}
