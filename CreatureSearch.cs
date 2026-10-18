using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTracker
{
    // A transient local HUD target, never a map pin, saved discovery or shared destination.
    internal sealed class CreatureSearch
    {
        private readonly HashSet<MarkerRecord> reached = new HashSet<MarkerRecord>();
        private readonly List<MarkerRecord> expired = new List<MarkerRecord>();
        internal MarkerRecord Target { get; private set; }
        internal MarkerRecord Notice { get; private set; }
        internal bool Ready { get; private set; }

        internal static bool Selected(MarkerRecord record, ISet<string> types, Vector3 origin) =>
            record?.entry?.creature == true && types.Contains(record.type) &&
            PlayerNavigation.Finite(record.position) && PlayerNavigation.Finite(origin) &&
            !NavigationArrival.Reached(origin, record.position, false, false, false);

        internal static MarkerRecord Choose(IEnumerable<MarkerRecord> records, ISet<string> types,
            MarkerRecord current, Vector3 origin, Func<MarkerRecord, bool> available, ISet<MarkerRecord> excluded = null)
        {
            if (types.Count == 0) return null;
            // Availability refreshes a local creature's current position before the arrival test.
            if (current != null && (excluded == null || !excluded.Contains(current)) && available(current) && Selected(current, types, origin)) return current;
            MarkerRecord nearest = null;
            double best = double.MaxValue;
            foreach (var record in records)
            {
                if (record?.entry?.creature != true || !types.Contains(record.type) || excluded != null && excluded.Contains(record) ||
                    !available(record) || !Selected(record, types, origin)) continue;
                double x = (double)record.position.x - origin.x, z = (double)record.position.z - origin.z;
                double distance = x * x + z * z;
                if (distance < best || distance == best && StringComparer.Ordinal.Compare(record.id, nearest?.id) < 0)
                { nearest = record; best = distance; }
            }
            return nearest;
        }
        internal void Update(IEnumerable<MarkerRecord> records, ISet<string> types, Vector3 origin, Func<MarkerRecord, bool> available)
        {
            if (types.Count == 0) { Reset(); return; }
            // Only this detection encounter is remembered. Unloaded/dead/hidden creatures
            // leave this small set; no history is saved or sent to another player.
            expired.Clear();
            foreach (var record in reached)
                if (!types.Contains(record.type) || !available(record)) expired.Add(record);
            foreach (var record in expired) reached.Remove(record);
            var previous = Target;
            if (Target != null && types.Contains(Target.type) && available(Target) &&
                NavigationArrival.Reached(origin, Target.position, false, false, false)) reached.Add(Target);
            Target = Choose(records, types, Target, origin, available, reached);
            if (!ReferenceEquals(previous, Target)) { Notice = null; Ready = false; }
        }
        internal MarkerRecord PrepareNotice(Func<MarkerRecord, string> name)
        {
            if (Target == null || Ready) return null;
            return Notice ?? (Notice = new MarkerRecord { id = Target.id, type = Target.type, entry = Target.entry, nameCache = name(Target) });
        }
        internal void NoticeShown(MarkerRecord notice)
        { if (notice != null && ReferenceEquals(notice, Notice) && Target != null) Ready = true; }
        internal void Reset() { Target = Notice = null; Ready = false; reached.Clear(); expired.Clear(); }
    }
}
