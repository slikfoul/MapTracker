using UnityEngine;

namespace MapTracker
{
    internal sealed class SightVisibility
    {
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Vector3[] samples = new Vector3[6];
        private Plane[] planes;
        private Camera camera;
        private int frame = -1, remaining;
        private readonly int blockers = LayerMask.GetMask("Default", "Default_small", "static_solid", "piece", "terrain", "vehicle", "character", "character_net");
        internal bool Check(ZNetView view, Vector3 position, Player player, out bool visible)
        {
            visible = false;
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount; remaining = 64;
                camera = Utils.GetMainCamera();
                planes = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
            }
            if (camera == null || player == null) return true;
            var bounds = new Bounds(position + Vector3.up, new Vector3(1, 2, 1));
            bool found = false;
            foreach (var collider in view.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.bounds.center.y > 3000f) continue;
                if (found) bounds.Encapsulate(collider.bounds); else { bounds = collider.bounds; found = true; }
            }
            if (!found)
                foreach (var renderer in view.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer.bounds.center.y > 3000f) continue;
                    if (found) bounds.Encapsulate(renderer.bounds); else { bounds = renderer.bounds; found = true; }
                }
            if (!GeometryUtility.TestPlanesAABB(planes, bounds)) return true;
            // Reserve the worst-case rays for one object before starting it.
            // Tracking keeps its cursor here when the frame's budget is used up.
            if (remaining < samples.Length * 2) return false;
            Vector3 eye = player.GetEyePoint();
            samples[0] = bounds.center;
            samples[1] = bounds.center + Vector3.up * bounds.extents.y;
            samples[2] = bounds.center + Vector3.right * bounds.extents.x;
            samples[3] = bounds.center - Vector3.right * bounds.extents.x;
            samples[4] = bounds.center + Vector3.forward * bounds.extents.z;
            samples[5] = bounds.center - Vector3.forward * bounds.extents.z;
            foreach (Vector3 point in samples)
            {
                Vector3 viewport = camera.WorldToViewportPoint(point);
                if (viewport.z <= camera.nearClipPlane || viewport.z > camera.farClipPlane ||
                    viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) continue;
                if (ParticleMist.IsMistBlocked(eye, point)) continue;
                if (!Clear(eye, point, view.transform, player.transform)) continue;
                if (!Clear(camera.transform.position, point, view.transform, player.transform)) continue;
                visible = true; return true;
            }
            return true;
        }
        private bool Clear(Vector3 origin, Vector3 target, Transform objectRoot, Transform playerRoot)
        {
            remaining--;
            Vector3 direction = target - origin; float distance = direction.magnitude;
            if (distance < 0.01f) return true;
            int count = Physics.RaycastNonAlloc(origin, direction / distance, hits, distance, blockers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            float objectHit = float.MaxValue, obstruction = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Transform hit = hits[i].collider.transform;
                if (hit.IsChildOf(playerRoot)) continue;
                if (hit.IsChildOf(objectRoot)) objectHit = Mathf.Min(objectHit, hits[i].distance);
                else obstruction = Mathf.Min(obstruction, hits[i].distance);
            }
            return obstruction == float.MaxValue || objectHit <= obstruction + 0.02f;
        }
    }
}
