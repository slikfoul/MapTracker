using UnityEngine.UI;

namespace MapTracker
{
    // Reuses the silhouette mesh effect; no textures/readbacks or per-frame processing.
    internal sealed class ObjectIconContrast
    {
        private Graphic target;
        private CreatureContour contour;
        internal void Apply(Graphic graphic, bool enabled, int thickness)
        {
            if (target != graphic)
            {
                if (contour != null) contour.enabled = false;
                target = graphic; contour = null;
            }
            if (target == null) return;
            if (contour == null)
            {
                if (!enabled) return;
                contour = target.gameObject.GetComponent<CreatureContour>() ?? target.gameObject.AddComponent<CreatureContour>();
            }
            if (contour.enabled != enabled) contour.enabled = enabled;
            contour.Thickness = thickness;
        }
    }
}
