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
            if (root != null && root.activeSelf != visible) root.SetActive(visible);
        }
        public void Dispose()
        {
            if (root != null) root.SetActive(false);
            if (mask != null) UnityEngine.Object.Destroy(mask);
            root = null; mask = null;
        }
    }
}
