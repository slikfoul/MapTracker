using System;
using UnityEngine;

namespace MapTracker
{
    internal sealed class PlayerMarkerLayer : IDisposable
    {
        private readonly Part large = new Part(), small = new Part();
        internal void Apply(Minimap map, bool raised)
        {
            large.Update(map != null ? map.m_largeMarker : null, raised);
            small.Update(map != null ? map.m_smallMarker : null, raised);
        }
        public void Dispose() { large.Release(); small.Release(); }
        private sealed class Part
        {
            private Transform marker;
            private Canvas canvas;
            private bool owned, previousOverride;
            private int previousOrder, previousLayer;
            internal void Update(Transform target, bool raised)
            {
                if (marker != target)
                {
                    Release(); marker = target;
                    if (marker != null)
                    {
                        canvas = marker.GetComponent<Canvas>(); owned = canvas == null;
                        if (owned) canvas = marker.gameObject.AddComponent<Canvas>();
                        previousOverride = canvas.overrideSorting;
                        previousOrder = canvas.sortingOrder; previousLayer = canvas.sortingLayerID;
                    }
                }
                if (canvas == null || marker == null) return;
                var parentCanvas = marker.parent != null ? marker.parent.GetComponentInParent<Canvas>()?.rootCanvas : null;
                if (raised && parentCanvas != null)
                {
                    // Raise the arrow alone. Reordering its ancestor could move
                    // the opaque map image over every label in a modded hierarchy.
                    if (!canvas.overrideSorting) canvas.overrideSorting = true;
                    if (canvas.sortingOrder != parentCanvas.sortingOrder + 1) canvas.sortingOrder = parentCanvas.sortingOrder + 1;
                    if (canvas.sortingLayerID != parentCanvas.sortingLayerID) canvas.sortingLayerID = parentCanvas.sortingLayerID;
                }
                else Restore();
            }
            private void Restore()
            {
                if (canvas == null) return;
                if (canvas.overrideSorting != previousOverride) canvas.overrideSorting = previousOverride;
                if (canvas.sortingOrder != previousOrder) canvas.sortingOrder = previousOrder;
                if (canvas.sortingLayerID != previousLayer) canvas.sortingLayerID = previousLayer;
            }
            internal void Release()
            {
                Restore(); if (owned && canvas != null) UnityEngine.Object.Destroy(canvas);
                canvas = null; marker = null; owned = false;
            }
        }
    }
}
