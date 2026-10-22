using MapTracker;

// The production scheduler is linked unchanged. These doubles model transport,
// a clock and marker storage; they do not claim to simulate Valheim physics.
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public Vector3(float a, float b, float c) { x=a; y=b; z=c; } }
    public static class Time { public static float realtimeSinceStartup; }
}
public class Player { public static Player m_localPlayer = new(); }
public class Minimap { public static Minimap instance = new(); }
public class ZNetPeer
{
    public long m_uid = 999;
    public FakeSocket m_socket = new();
    public bool IsReady() => true;
}
public class FakeSocket { public int Queue; public int GetSendQueueSize() => Queue; }
public class ZNet
{
    public static ZNet instance;
    public long Id;
    public List<ZNetPeer> Peers = new() { new() };
    public long GetWorldUID() => 123;
    public static long GetUID() => instance.Id;
    public List<ZNetPeer> GetPeers() => Peers;
}
public class ZRoutedRpc
{
    public const long Everybody = 0;
    public static ZRoutedRpc instance;
    public long Id;
    public Action<long,long,string,string> Send;
    public Dictionary<string, Action<long,string>> Handlers = new();
    public void Register<T>(string method, Action<long,T> handler) => Handlers.Add(method, (peer,json) => handler(peer,(T)(object)json));
    public void InvokeRoutedRPC(long peer, string method, params object[] parameters) => Send(Id,peer,method,(string)parameters[0]);
}
namespace MapTracker
{
    public sealed class MarkerRecord
    {
        public string id, type, portalTag;
        public UnityEngine.Vector3 position;
        public bool collected, visited, completed, imported, observed, shared, dynamic;
        public long pickedTime;
        public long completionRevision, completionAuthor;
        public UnityEngine.Vector3? dungeonOrigin;
        public string[] dungeonAliases;
        public int creatureLevel, creatureRelation;
    }
    internal static class PlayerNavigation
    { internal static bool Finite(UnityEngine.Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z); }
    internal class Setting { internal bool Value; internal Setting(bool value) { Value=value; } }
    internal class FakeCatalog { internal bool Ready = true; }
    internal class FakeDiscoveries
    {
        internal int Targets;
        internal void SelectSharedTarget(Minimap map, UnityEngine.Vector3 position, string name, string type, string id) { Targets++; }
    }
    internal class FakeTracking
    {
        internal bool Ready = true;
        internal List<MarkerRecord> Static = new(), Creatures = new();
        internal Dictionary<string,MarkerRecord> Received = new();
        internal int Applied;
        internal Dictionary<string,float> CreatureUntil = new();
        internal int CreatureFlickers, SimulatedOldFlickers;
        private Dictionary<string,float> oldUntil = new();
        internal FakeDiscoveries Discoveries = new();
        internal IReadOnlyList<MarkerRecord> OwnDiscoveries() => Static;
        // Transport fixtures provide observation state directly; native-world
        // observation refresh is covered separately by the production roster tests.
        internal void PrepareSharedDiscovery(MarkerRecord record) { }
        internal List<MarkerRecord> OwnCreatures() => new(Creatures);
        internal bool Contains(MarkerRecord record) => Static.Contains(record) || Creatures.Contains(record);
        internal bool ContainsDiscovery(string id) => Static.Exists(record => record.id == id && !record.shared && !record.dynamic);
        internal void ReceiveDiscoveries(long peer, List<MarkerRecord> records)
        { foreach(var record in records) { Received[record.id]=record; Applied++; } }
        internal void ReceiveCreatures(long peer, List<MarkerRecord> records, int leaseSeconds=30)
        {
            foreach(var record in records) {
                float now=UnityEngine.Time.realtimeSinceStartup;
                if(CreatureUntil.TryGetValue(record.id,out float until)&&until<now)CreatureFlickers++;
                if(oldUntil.TryGetValue(record.id,out float old)&&old<now)SimulatedOldFlickers++;
                CreatureUntil[record.id]=now+leaseSeconds;oldUntil[record.id]=now+15;
            }
            ReceiveDiscoveries(peer,records);
        }
    }
    internal class Plugin
    {
        internal Setting Enabled = new(true), ShareMarkers = new(true), OtherPlayerPins = new(true), ShareCreatures = new(false);
        internal FakeCatalog Catalog = new();
        internal FakeTracking Tracking = new();
    }
}
