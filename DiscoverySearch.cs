using System;
using System.Collections.Generic;
using System.Linq;

namespace MapTracker
{
    // Runs on the game thread in small slices. Keys are detached from mutable
    // records before sorting, so movement or a favorite change cannot invalidate
    // the comparison while a refresh spans several frames.
    internal sealed class DiscoverySearch
    {
        private struct Candidate
        {
            internal MarkerRecord Record;
            internal string Id;
            internal float Distance;
            internal bool Favorite;
        }
        private IEnumerator<byte> preparation;
        private int generation;
        internal bool Pending => preparation != null;
        internal List<MarkerRecord> Results { get; private set; } = new List<MarkerRecord>();

        internal void Begin(IEnumerable<MarkerRecord> source, string search, bool favorites, float originX, float originZ,
            Func<MarkerRecord, string> name, Func<MarkerRecord, bool> current)
        {
            Cancel();
            preparation = Prepare(source, (search ?? "").Trim(), favorites, originX, originZ, name, current, generation).GetEnumerator();
        }
        internal bool Advance(int milliseconds = 2, int maximumItems = 512, Func<long> clock = null)
            => FrameWork.Advance(ref preparation, milliseconds, maximumItems, clock);
        internal void Cancel()
        {
            generation++;
            preparation?.Dispose(); preparation = null;
        }
        internal void Reset() { Cancel(); Results = new List<MarkerRecord>(); }
        internal static bool Eligible(MarkerRecord record, bool favorites) => record != null && !record.dynamic && !record.shared &&
            record.entry != null && !record.entry.creature && (!favorites || record.favorite);

        private IEnumerable<byte> Prepare(IEnumerable<MarkerRecord> source, string term, bool favorites, float originX, float originZ,
            Func<MarkerRecord, string> name, Func<MarkerRecord, bool> current, int request)
        {
            // Dictionary enumerators cannot be held across game frames. This
            // reference-only copy is still one O(n) allocation; the expensive
            // matching, name resolution and stable sort below are budgeted.
            var snapshot = source.ToArray();
            yield return 0;
            var candidates = new List<Candidate>();
            foreach (var record in snapshot)
            {
                if (current(record) && Eligible(record, favorites) && (term.Length == 0 || name(record).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (record.type ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (record.portalTag ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    float x = record.position.x - originX, z = record.position.z - originZ;
                    candidates.Add(new Candidate { Record = record, Id = record.id, Favorite = record.favorite,
                        Distance = (float)Math.Sqrt(x * x + z * z) });
                }
                yield return 0;
            }
            var ordered = candidates.ToArray();
            yield return 0;
            foreach (byte step in FrameWork.StableSort(ordered, ordered.Length, Compare)) yield return step;
            var result = new List<MarkerRecord>(ordered.Length);
            foreach (var candidate in ordered)
            {
                if (current(candidate.Record) && Eligible(candidate.Record, favorites)) result.Add(candidate.Record);
                yield return 0;
            }
            if (request == generation) Results = result;
        }
        private static int Compare(Candidate a, Candidate b)
        {
            int order = b.Favorite.CompareTo(a.Favorite);
            if (order == 0) order = a.Distance.CompareTo(b.Distance);
            return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id);
        }
    }
}
