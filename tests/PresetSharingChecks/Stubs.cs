// The real protocol, persistence and transport are linked unchanged. These
// doubles supply only the game clock, online roster, socket and config surface.
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public Vector3(float a, float b, float c) { x=a; y=b; z=c; } }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace BepInEx { public static class Paths { public static string ConfigPath = Path.Combine(AppContext.BaseDirectory, "preset-checks-" + Guid.NewGuid().ToString("N")); } }
public struct ZDOID { public long UserID; public bool IsNone() => UserID==0; }
public class Player { public static Player m_localPlayer = new(); }
public class FakeSocket { public int Queue; public int GetSendQueueSize() => Queue; }
public class ZNetPeer { public FakeSocket m_socket = new(); public bool IsReady() => true; }
public class ZNet
{
    public struct PlayerInfo { public string m_name; public ZDOID m_characterID; public bool m_publicPosition; }
    public static ZNet instance; public long Id, World=123;
    public List<PlayerInfo> Players=new(); public List<ZNetPeer> Peers=new(){new()};
    public List<PlayerInfo> GetPlayerList()=>Players;
    public List<ZNetPeer> GetPeers()=>Peers;
    public long GetWorldUID()=>World;
    public static long GetUID()=>instance.Id;
}
public class ZRoutedRpc
{
    public static ZRoutedRpc instance; public long Id;
    public Dictionary<string,Action<long,string>> Handlers=new();
    public Action<long,long,string,string> Send;
    public void Register<T>(string method,Action<long,T> handler)=>Handlers.Add(method,(peer,json)=>handler(peer,(T)(object)json));
    public void InvokeRoutedRPC(long peer,string method,params object[] args)=>Send(Id,peer,method,(string)args[0]);
}
namespace MapTracker
{
    public enum BuiltInFilterPreset { Default, Gathering, Exploration, Household }
    internal class Setting { public bool Value; public Setting(bool value=false){Value=value;} }
    public class CatalogEntry { public string key,category; public bool creature; internal Setting Selected=new(),KeepVisible=new(); }
    internal class FakeCatalog { internal List<CatalogEntry> Entries=new(); }
    internal class FakeConfig { internal bool SaveOnConfigSet; internal int Saved; internal void Save(){Saved++;} }
    internal class FakeDiscoveries { internal bool Notifies(string key)=>false; internal void SetNotify(IEnumerable<CatalogEntry> entries,bool value){} }
    internal class FakeTracking { internal FakeDiscoveries Discoveries=new(); internal void RequestPinRefresh(){} }
    internal class FakeWindow { internal int Refreshes; internal void Refresh(){Refreshes++;} }
    internal class Plugin
    {
        internal Setting Enabled=new(true),BlockPresetOffers=new();
        internal FakeCatalog Catalog=new(); internal FakeConfig Config=new(); internal FakeTracking Tracking=new(); internal FakeWindow Window=new();
    }
    public class MarkerRecord
    {
        public string id,type,portalTag; public UnityEngine.Vector3 position;
        public bool collected,visited,completed,imported,observed; public long pickedTime,completionRevision,completionAuthor;
        public int creatureLevel,creatureRelation; public UnityEngine.Vector3? dungeonOrigin; public string[] dungeonAliases;
    }
    internal static class PlayerNavigation { internal static bool Finite(UnityEngine.Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z); }
}
