using System.Collections.Generic;
using UnityEngine;

namespace MapTracker
{
    internal sealed class CreatureNetworkDelta
    {
        private sealed class State { internal Vector3 Position; internal string Type; internal int Level, Relation; internal float Sent; }
        private readonly Dictionary<long, Dictionary<string, State>> peers = new Dictionary<long, Dictionary<string, State>>();
        private readonly List<string> expired = new List<string>();
        internal List<MarkerRecord> Select(long peer, IReadOnlyList<MarkerRecord> records, float now)
        {
            if (!peers.TryGetValue(peer, out var sent)) peers.Add(peer, sent = new Dictionary<string, State>());
            var result = new List<MarkerRecord>();
            foreach (var record in records)
            {
                if (sent.TryGetValue(record.id, out var previous))
                {
                    var shift = record.position - previous.Position;
                    if (now - previous.Sent < 1.5f && previous.Type == record.type && previous.Level == record.creatureLevel &&
                        previous.Relation == record.creatureRelation && shift.sqrMagnitude < 0.0025f) continue;
                }
                sent[record.id] = new State { Position = record.position, Type = record.type, Level = record.creatureLevel,
                    Relation = record.creatureRelation, Sent = now };
                result.Add(record);
            }
            expired.Clear(); foreach (var pair in sent) if (now - pair.Value.Sent > 4) expired.Add(pair.Key);
            foreach (string id in expired) sent.Remove(id);
            return result;
        }
        internal void Reset() { peers.Clear(); }
        internal void RemovePeer(long peer) { peers.Remove(peer); }
    }
}
