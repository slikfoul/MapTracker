using System;
using System.Collections.Generic;
using System.Text;

namespace MapTracker
{
    internal sealed class SharingBudget
    {
        internal const int PacketBytes = 4096, EnvelopeBytes = 128, SocketQueueBytes = 4096;
        private readonly float rate, capacity;
        private float available, updated;
        internal SharingBudget(float bytesPerSecond, float burstBytes)
        { rate = bytesPerSecond; capacity = burstBytes; available = burstBytes; }
        internal bool Allows(int bytes, float now)
        {
            available = Math.Min(capacity, available + Math.Max(0, now - updated) * rate); updated = now;
            return bytes > 0 && bytes <= available;
        }
        internal void Spend(int bytes) { available -= bytes; }
    }

    internal sealed class SharingDelta
    {
        private sealed class State { internal MarkerRecord Record; internal float Sent; }
        private readonly Dictionary<string, State> sent = new Dictionary<string, State>();
        private int cursor;
        internal List<MarkerRecord> Select(IReadOnlyList<MarkerRecord> records, float now, bool creatures)
        {
            var result = new List<MarkerRecord>();
            int examined = Math.Min(records.Count, 128);
            for (int i = 0; i < examined && result.Count < 8; i++)
            {
                if (cursor >= records.Count) cursor = 0;
                var record = records[cursor++];
                if (string.IsNullOrEmpty(record.id) || record.id.Length > 256 || string.IsNullOrEmpty(record.type) ||
                    record.type.Length > 256 || (record.portalTag?.Length ?? 0) > 256) continue;
                if (sent.TryGetValue(record.id, out var previous) && Equivalent(previous.Record, record, creatures) &&
                    now - previous.Sent < (creatures ? 3 : record.observed ? 15 : 120)) continue;
                result.Add(Copy(record));
            }
            return result;
        }
        internal void Commit(IReadOnlyList<MarkerRecord> records, float now)
        {
            // Selection is not delivery. Only receiver acknowledgements commit the cache.
            foreach (var record in records) sent[record.id] = new State { Record = Copy(record), Sent = now };
        }
        internal void Reset() { sent.Clear(); cursor = 0; }
        internal static bool Equivalent(MarkerRecord a, MarkerRecord b, bool creatures)
        {
            float x = a.position.x - b.position.x, y = a.position.y - b.position.y, z = a.position.z - b.position.z;
            return a.type == b.type && x * x + y * y + z * z <= (creatures ? 0.01f : 0) &&
                a.creatureLevel == b.creatureLevel && a.creatureRelation == b.creatureRelation &&
                a.collected == b.collected && a.visited == b.visited && a.completed == b.completed &&
                a.portalTag == b.portalTag && a.pickedTime == b.pickedTime && a.observed == b.observed && a.imported == b.imported;
        }
        internal static MarkerRecord Copy(MarkerRecord r) => new MarkerRecord {
            id = r.id, type = r.type, position = r.position, collected = r.collected, visited = r.visited,
            completed = r.completed, pickedTime = r.pickedTime, portalTag = r.portalTag, imported = r.imported,
            observed = r.observed, creatureLevel = r.creatureLevel, creatureRelation = r.creatureRelation
        };
    }

    internal sealed class SharingPacket
    {
        public int schema = 2, sequence;
        public string world, kind, session;
        public List<MarkerRecord> markers;
        public NavigationShare target;
    }
    internal sealed class SharingPeer
    {
        internal float Seen, Attempt;
        internal int NextSequence = 1, LastReceived;
        internal readonly SharingDelta Static = new SharingDelta(), Creatures = new SharingDelta();
        internal readonly SharingBudget Budget = new SharingBudget(2048, SharingBudget.PacketBytes + SharingBudget.EnvelopeBytes);
        internal SharingPacket Pending;
        internal string PendingJson;
        internal NavigationShare Target;
        internal bool NextCreatures;
        internal bool AcceptsDiscoveries, AcceptsTargets;
        internal string Session;
        internal bool Confirm(int sequence, float now)
        {
            if (Pending == null || Pending.sequence != sequence) return false;
            if (Pending.markers != null) (Pending.kind == "creatures" ? Creatures : Static).Commit(Pending.markers, now);
            Pending = null; PendingJson = null; return true;
        }
    }
    internal sealed class SharingInbox
    {
        internal sealed class Item { internal long Peer; internal string Json; internal int Bytes; }
        private readonly Queue<Item> messages = new Queue<Item>();
        private readonly HashSet<long> queuedPeers = new HashSet<long>();
        private int bytes;
        internal bool Enqueue(long peer, string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > SharingBudget.PacketBytes || queuedPeers.Contains(peer)) return false;
            int size = Encoding.UTF8.GetByteCount(json);
            if (size > SharingBudget.PacketBytes || bytes + size > 65536 || messages.Count >= 32) return false;
            messages.Enqueue(new Item { Peer = peer, Json = json, Bytes = size }); queuedPeers.Add(peer); bytes += size;
            return true;
        }
        internal Item Take()
        {
            if (messages.Count == 0) return null;
            var item = messages.Dequeue(); queuedPeers.Remove(item.Peer); bytes -= item.Bytes; return item;
        }
        internal void Clear() { messages.Clear(); queuedPeers.Clear(); bytes = 0; }
    }
}
