using System.Collections;
using System.Collections.Generic;

namespace MapTracker
{
    // Maintain membership when a discovery changes instead of rebuilding the
    // entire outbound archive every two seconds. IDs may change on a live record;
    // reference identity keeps its slot valid across that rebinding.
    internal sealed class DiscoveryRoster : IReadOnlyList<MarkerRecord>
    {
        private struct Slot { internal bool Priority; internal int Index; }
        private readonly List<MarkerRecord> priority = new List<MarkerRecord>();
        private readonly List<MarkerRecord> ordinary = new List<MarkerRecord>();
        private readonly Dictionary<MarkerRecord, Slot> slots = new Dictionary<MarkerRecord, Slot>();
        public int Count => priority.Count + ordinary.Count;
        public MarkerRecord this[int index] => index < priority.Count ? priority[index] : ordinary[index - priority.Count];
        internal void Update(MarkerRecord record)
        {
            bool eligible = record != null && !record.dynamic && !record.shared && CatalogContent.CanTrack(record.entry);
            if (!eligible) { if (record != null) Remove(record); return; }
            bool important = record.entry.dungeon || record.entry.category == "Portals";
            if (slots.TryGetValue(record, out var previous))
            {
                if (previous.Priority == important) return;
                Remove(record);
            }
            var list = important ? priority : ordinary;
            slots.Add(record, new Slot { Priority = important, Index = list.Count }); list.Add(record);
        }
        internal void Remove(MarkerRecord record)
        {
            if (!slots.TryGetValue(record, out var previous)) return;
            var list = previous.Priority ? priority : ordinary;
            int last = list.Count - 1;
            if (previous.Index != last)
            {
                var moved = list[last]; list[previous.Index] = moved;
                slots[moved] = new Slot { Priority = previous.Priority, Index = previous.Index };
            }
            list.RemoveAt(last); slots.Remove(record);
        }
        internal void Clear() { priority.Clear(); ordinary.Clear(); slots.Clear(); }
        public IEnumerator<MarkerRecord> GetEnumerator()
        { foreach (var record in priority) yield return record; foreach (var record in ordinary) yield return record; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
