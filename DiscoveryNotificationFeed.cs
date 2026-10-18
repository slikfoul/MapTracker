using System;
using System.Collections.Generic;

namespace MapTracker
{
    internal sealed class DiscoveryNotificationFeed
    {
        private readonly Queue<MarkerRecord> pending = new Queue<MarkerRecord>();
        private readonly HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        internal MarkerRecord Current { get; private set; }
        private float remaining, lastTick;
        private bool wasVisible;
        internal bool Enqueue(MarkerRecord item)
        {
            if (item == null || pending.Count >= 64 || !ids.Add(item.id)) return false;
            pending.Enqueue(item); return true;
        }
        internal void Tick(float now, bool visible)
        {
            if (visible && wasVisible && Current != null) remaining -= Math.Max(0, now - lastTick);
            lastTick = now; wasVisible = visible;
            if (!visible) return;
            if (Current != null && remaining <= 0) { ids.Remove(Current.id); Current = null; }
            if (Current == null && pending.Count > 0) { Current = pending.Dequeue(); remaining = 4; }
        }
        internal void Remove(MarkerRecord item)
        {
            if (item == null) return;
            if (ReferenceEquals(Current, item)) { ids.Remove(item.id); Current = null; remaining = 0; }
            int count = pending.Count;
            for (int i = 0; i < count; i++)
            {
                var notice = pending.Dequeue();
                if (ReferenceEquals(notice, item)) ids.Remove(notice.id);
                else pending.Enqueue(notice);
            }
        }
        internal void Clear()
        { pending.Clear(); ids.Clear(); Current = null; remaining = lastTick = 0; wasVisible = false; }
    }
}
