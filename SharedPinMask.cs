using UnityEngine;

namespace MapTracker
{
    // A persistent zero-alpha layer prevents UI rebuilds from flashing a hidden
    // pin. Also deactivate the root so native map clicks cannot select it.
    internal sealed class SharedPinMask
    {
        private readonly Part icon = new Part(), name = new Part();
        internal void Hide(Minimap.PinData pin)
        {
            icon.Hide(pin.m_uiElement != null ? pin.m_uiElement.gameObject : null);
            name.Hide(pin.m_NamePinData?.PinNameGameObject);
        }
        internal void Restore() { icon.Release(true); name.Release(true); }
        private sealed class Part
        {
            private GameObject root;
            private CanvasGroup mask;
            private bool previouslyActive;
            internal void Hide(GameObject target)
            {
                if (root != target)
                {
                    Release(false); root = target;
                    if (root != null)
                    {
                        previouslyActive = root.activeSelf;
                        root.SetActive(false);
                        // Keep other mods' CanvasGroups untouched; ours composes
                        // with their alpha and is removed when this filter lifts.
                        mask = root.AddComponent<CanvasGroup>();
                        mask.interactable = false; mask.blocksRaycasts = false;
                    }
                }
                if (mask != null && mask.alpha != 0) mask.alpha = 0;
                if (root != null && root.activeSelf) root.SetActive(false);
            }
            internal void Release(bool restoreActive)
            {
                if (!restoreActive && root != null) root.SetActive(false);
                if (mask != null) { mask.alpha = 1; Object.Destroy(mask); }
                if (restoreActive && root != null) root.SetActive(previouslyActive);
                root = null; mask = null;
            }
        }
    }
}
