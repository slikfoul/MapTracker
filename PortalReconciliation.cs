using System.Collections.Generic;

namespace MapTracker
{
    // Require repeated, continuous checks of a ready area before retiring an
    // old discovery whose destruction event was missed in an earlier session.
    internal sealed class PortalReconciliation
    {
        private sealed class Missing
        {
            internal float first, last;
            internal int checks;
        }
        private readonly Dictionary<string, Missing> missing = new Dictionary<string, Missing>();
        internal bool Check(string id, bool areaReady, bool exists, float now)
        {
            if (!areaReady || exists) { missing.Remove(id); return false; }
            if (!missing.TryGetValue(id, out var state) || now < state.last || now - state.last > 3f)
                missing[id] = state = new Missing { first = now };
            state.last = now; state.checks++;
            if (state.checks < 3 || now - state.first < 12f) return false;
            missing.Remove(id);
            return true;
        }
        internal void Clear() => missing.Clear();
        internal void Forget(string id) => missing.Remove(id);
    }
}
