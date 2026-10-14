using System;
using UnityEngine;

namespace MapTracker
{
    // Retain our own alpha mask between native refreshes; other mods' groups
    // remain untouched. Tracking enforces this after the frame's Update calls.
    internal sealed class NativeNameVisibility : IDisposable
    {
        private GameObject root;
        private CanvasGroup mask;
        internal void Apply(GameObject target, bool visible)
        {
            if (root != target)
            {
                Dispose(); root = target;
                if (root != null)
                {
                    mask = root.AddComponent<CanvasGroup>();
                    mask.interactable = false; mask.blocksRaycasts = false;
                }
            }
            if (mask != null && mask.alpha != (visible ? 1 : 0)) mask.alpha = visible ? 1 : 0;
            // Native map refreshes activate name objects. Mask crowded labels instead
            // of deactivating/reactivating them on every refresh (TMP/Canvas rebuilds).
            if (root != null && visible && !root.activeSelf) root.SetActive(true);
        }
        public void Dispose()
        {
            if (root != null) root.SetActive(false);
            if (mask != null) { mask.alpha = 1; UnityEngine.Object.Destroy(mask); }
            root = null; mask = null;
        }
    }
}
