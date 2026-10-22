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
        // Acknowledgements need only the fields compared by the wire delta, not
        // MarkerRecord's Unity references and presentation/cache bookkeeping.
        // Reuse each state on refresh; detach aliases from pending packet data.
        private sealed class State
        {
            private string type, portalTag;
            private UnityEngine.Vector3 position;
            private bool collected, visited, completed, observed, imported;
            private long completionRevision, completionAuthor, pickedTime;
            private int creatureLevel, creatureRelation;
            private UnityEngine.Vector3? dungeonOrigin;
            private string[] dungeonAliases;
            internal float Sent;
            internal void Capture(MarkerRecord record, float now)
            {
                type = record.type; portalTag = record.portalTag; position = record.position;
                collected = record.collected; visited = record.visited; completed = record.completed;
                observed = record.observed; imported = record.imported;
                completionRevision = record.completionRevision; completionAuthor = record.completionAuthor;
                pickedTime = record.pickedTime; creatureLevel = record.creatureLevel; creatureRelation = record.creatureRelation;
                dungeonOrigin = record.dungeonOrigin;
                if (!SameAliases(dungeonAliases, record.dungeonAliases))
                    dungeonAliases = record.dungeonAliases == null ? null : (string[])record.dungeonAliases.Clone();
                Sent = now;
            }
            internal bool EquivalentTo(MarkerRecord record, bool creatures)
            {
                float x = position.x - record.position.x, y = position.y - record.position.y, z = position.z - record.position.z;
                return type == record.type && x * x + y * y + z * z <= (creatures ? 0.01f : 0) &&
                    creatureLevel == record.creatureLevel && creatureRelation == record.creatureRelation &&
                    collected == record.collected && visited == record.visited && completed == record.completed &&
                    completionRevision == record.completionRevision && completionAuthor == record.completionAuthor &&
                    portalTag == record.portalTag && pickedTime == record.pickedTime && observed == record.observed && imported == record.imported &&
                    Nullable.Equals(dungeonOrigin, record.dungeonOrigin) && SameAliases(dungeonAliases, record.dungeonAliases);
            }
        }
        // Only nonempty selections escape into pending packets. Empty callers
        // inspect Count and never mutate this shared zero-capacity result.
        private static readonly List<MarkerRecord> Empty = new List<MarkerRecord>(0);
        private readonly Dictionary<string, State> sent = new Dictionary<string, State>();
        private readonly List<string> retired = new List<string>();
        private readonly Dictionary<string, MarkerRecord> urgent = new Dictionary<string, MarkerRecord>();
        internal Func<MarkerRecord, bool> Current;
        internal Func<string, bool> Retained;
        internal Action<MarkerRecord> Prepare;
        private int emptyExamined, emptyCount = -1;
        private float idleUntil;
        internal void Prioritize(MarkerRecord record)
        {
            if (!Eligible(record)) return;
            if (urgent.Count >= 64 && !urgent.ContainsKey(record.id))
            {
                string first = null; foreach (var id in urgent.Keys) { first = id; break; }
                urgent.Remove(first);
            }
            urgent[record.id] = record;
            idleUntil = 0;
        }
        private static bool Eligible(MarkerRecord record) => record != null &&
            !string.IsNullOrEmpty(record.id) && record.id.Length <= 256 && !string.IsNullOrEmpty(record.type) &&
            record.type.Length <= 256 && (record.portalTag?.Length ?? 0) <= 256;
        private static bool Selected(List<MarkerRecord> records, string id)
        { foreach (var record in records) if (record.id == id) return true; return false; }
        private int cursor;
        internal List<MarkerRecord> Select(IReadOnlyList<MarkerRecord> records, float now, bool creatures)
        {
            if (records.Count != emptyCount)
            { emptyCount = records.Count; emptyExamined = 0; idleUntil = 0; }
            if (now < idleUntil && urgent.Count == 0) return Empty;
            List<MarkerRecord> result = null;
            retired.Clear();
            foreach (var pair in urgent)
            {
                var record = pair.Value;
                if (pair.Key != record.id || !Eligible(record) || Current != null && !Current(record))
                { retired.Add(pair.Key); continue; }
                Prepare?.Invoke(record);
                if (sent.TryGetValue(record.id, out var previous) && previous.EquivalentTo(record, creatures))
                { retired.Add(pair.Key); continue; }
                if (result == null) result = new List<MarkerRecord>(8);
                result.Add(Copy(record)); if (result.Count == 8) break;
            }
            foreach (var id in retired) urgent.Remove(id);
            retired.Clear();
            int examined = Math.Min(records.Count, 128);
            for (int i = 0; i < examined && (result == null || result.Count < 8); i++)
            {
                if (cursor >= records.Count) cursor = 0;
                var record = records[cursor++];
                if (!Eligible(record) || result != null && Selected(result, record.id)) continue;
                Prepare?.Invoke(record);
                if (sent.TryGetValue(record.id, out var previous) && previous.EquivalentTo(record, creatures) &&
                    now - previous.Sent < (creatures ? 3 : record.observed ? 15 : 120)) continue;
                if (result == null) result = new List<MarkerRecord>(8);
                result.Add(Copy(record));
            }
            if (result != null) emptyExamined = 0;
            else
            {
                emptyExamined += examined;
                // Empty pages may precede unsent entries in a large archive.
                // Pause only after a complete unchanged sweep, never between
                // productive pages. Manual progress bypasses this short pause.
                if (emptyExamined >= records.Count)
                { idleUntil = now + 0.1f; emptyExamined = 0; }
            }
            return result ?? Empty;
        }
        internal void Commit(IReadOnlyList<MarkerRecord> records, float now)
        {
            // Selection is not delivery. Only receiver acknowledgements commit the cache.
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                // A delayed ACK must not resurrect a retired discovery's cache.
                if (Retained != null && !Retained(record.id)) { Forget(record.id); continue; }
                if (!sent.TryGetValue(record.id, out var state)) sent.Add(record.id, state = new State());
                state.Capture(record, now);
                if (urgent.TryGetValue(record.id, out var waiting) && Equivalent(waiting, record, false)) urgent.Remove(record.id);
            }
        }
        internal void Reset()
        { sent.Clear(); urgent.Clear(); cursor = 0; emptyExamined = 0; emptyCount = -1; idleUntil = 0; }
        internal void Forget(string id)
        {
            if (id == null) return;
            sent.Remove(id); urgent.Remove(id); emptyExamined = 0; idleUntil = 0;
        }
        internal void Prune(ISet<string> current)
        {
            retired.Clear();
            foreach (var id in sent.Keys) if (!current.Contains(id)) retired.Add(id);
            foreach (var id in retired) sent.Remove(id);
            retired.Clear();
        }
        internal static bool Equivalent(MarkerRecord a, MarkerRecord b, bool creatures)
        {
            float x = a.position.x - b.position.x, y = a.position.y - b.position.y, z = a.position.z - b.position.z;
            return a.type == b.type && x * x + y * y + z * z <= (creatures ? 0.01f : 0) &&
                a.creatureLevel == b.creatureLevel && a.creatureRelation == b.creatureRelation &&
                a.collected == b.collected && a.visited == b.visited && a.completed == b.completed &&
                a.completionRevision == b.completionRevision && a.completionAuthor == b.completionAuthor &&
                a.portalTag == b.portalTag && a.pickedTime == b.pickedTime && a.observed == b.observed && a.imported == b.imported &&
                Nullable.Equals(a.dungeonOrigin, b.dungeonOrigin) &&
                SameAliases(a.dungeonAliases, b.dungeonAliases);
        }
        internal static bool SameAliases(string[] a, string[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            int count = a?.Length ?? 0;
            if (count != (b?.Length ?? 0)) return false;
            for (int i = 0; i < count; i++) if (a[i] != b[i]) return false;
            return true;
        }
        internal static MarkerRecord Copy(MarkerRecord r) => new MarkerRecord {
            id = r.id, type = r.type, position = r.position, collected = r.collected, visited = r.visited,
            completed = r.completed, pickedTime = r.pickedTime, portalTag = r.portalTag, imported = r.imported,
            completionRevision = r.completionRevision, completionAuthor = r.completionAuthor,
            observed = r.observed, creatureLevel = r.creatureLevel, creatureRelation = r.creatureRelation,
            dungeonOrigin = r.dungeonOrigin, dungeonAliases = r.dungeonAliases == null ? null : (string[])r.dungeonAliases.Clone()
        };
    }

    internal sealed class SharingPacket
    {
        public int schema = 2, sequence;
        public string world, kind, session;
        public int creatureLeaseSeconds;
        public List<MarkerRecord> markers;
        public NavigationShare target;
    }
    internal static class CreatureSharingLease
    {
        internal const int Minimum = 30, Maximum = 180;
        internal static int Seconds(int creatures, int recipients)
        {
            // Reserve half the unchanged bandwidth budget for static data/acks.
            // Allow one conservative full position-update cycle plus network jitter.
            double rate = Math.Min(2048, 8192.0 / Math.Max(1, recipients));
            return Math.Max(Minimum, Math.Min(Maximum, 15 + (int)Math.Ceiling(Math.Max(0, creatures) * 512.0 / rate)));
        }
        internal static int Clamp(int seconds) => Math.Max(Minimum, Math.Min(Maximum, seconds));
    }
    internal sealed class SharingPeer
    {
        internal float Seen, Attempt;
        internal int NextSequence = 1, LastReceived;
        internal readonly SharingDelta Static = new SharingDelta(), Creatures = new SharingDelta();
        internal readonly SharingBudget Budget = new SharingBudget(2048, SharingBudget.PacketBytes + SharingBudget.EnvelopeBytes);
        internal SharingPacket Pending;
        internal bool PendingSent;
        internal string PendingJson;
        internal NavigationShare Target;
        internal bool NextCreatures;
        internal bool AcceptsDiscoveries, AcceptsCreatures, AcceptsTargets;
        internal string Session;
        internal bool Confirm(int sequence, float now)
        {
            if (Pending == null || !PendingSent || Pending.sequence != sequence) return false;
            if (Pending.markers != null) (Pending.kind == "creatures" ? Creatures : Static).Commit(Pending.markers, now);
            Pending = null; PendingJson = null; PendingSent = false; return true;
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
