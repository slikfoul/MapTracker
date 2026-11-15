using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using MapTracker;
using UnityEngine;

static class Checks
{
    static readonly Assembly A = typeof(MarkerRecord).Assembly;
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly Type Tracking = A.GetType("MapTracker.Tracking")!;
    static readonly CatalogEntry Portal = new() { key="prefab:portal_stone", category="Portals" };
    static int passed;
    static void Check(bool ok,string label) { if(!ok)throw new Exception(label); passed++;System.Console.WriteLine("PASS: "+label); }
    static object Get(object o,string field) => o.GetType().GetField(field,F)!.GetValue(o)!;
    static void Set(object o,string field,object v) => o.GetType().GetField(field,F)!.SetValue(o,v);
    static object Call(object o,string method,params object[] args) => o.GetType().GetMethod(method,F)!.Invoke(o,args)!;
    static MarkerRecord Record(string id,bool deleted=false)
    {
        var r=new MarkerRecord {id=id,type=Portal.key,position=new Vector3(1708,73,8645),collected=deleted};
        typeof(MarkerRecord).GetField("entry",F)!.SetValue(r,Portal);return r;
    }
    sealed class Client
    {
        internal object Instance=RuntimeHelpers.GetUninitializedObject(Tracking);
        internal Dictionary<string,MarkerRecord> Records=new();
        internal object Ledger=>Get(Instance,"portalRetirements");
        internal Client()
        {
            Set(Instance,"records",Records);
            Set(Instance,"locallyHiddenPortals",new HashSet<string>());
            foreach(var name in new[]{"pinnedRecords","dynamicRecords","liveRecords","remoteStaticRecords","pinChanges","portalRecords"})Set(Instance,name,new HashSet<MarkerRecord>());
            foreach(var pair in new[]{("portalRetirements","PortalRetirements"),("portalReconciliation","PortalReconciliation"),("discoveryRoster","DiscoveryRoster")})
                Set(Instance,pair.Item1,Activator.CreateInstance(A.GetType("MapTracker."+pair.Item2)!,true)!);
        }
        internal void Receive(MarkerRecord r)
        {
            if((bool)Call(Instance,"HandlePortalState",r))return;
            A.GetType("MapTracker.SharedDiscoveries")!.GetMethod("Receive",S)!.Invoke(null,new object[]{Records,r,2L,0f,null!});
        }
        internal void Remove(string id)=>Call(Instance,"RetirePortal",Records[id]);
        internal IReadOnlyList<MarkerRecord> Outbound=>(IReadOnlyList<MarkerRecord>)Call(Instance,"OwnDiscoveries");
    }
    static void Main()
    {
        var a=new Client(); var b=new Client();
        a.Receive(Record("77:123")); b.Receive(Record("77:123"));
        a.Remove("77:123");
        Check(a.Records.Count==0 && a.Outbound.Count==1 && a.Outbound[0].collected,"retirement removes the visible record and queues one deletion even for a peer discovery");
        Check((bool)Call(a.Instance,"ContainsOutboundDiscovery",a.Outbound[0]),"deletion is eligible for prioritized transmission although absent from visible records");
        var wire=DataJson.Read<MarkerRecord>(DataJson.Write(a.Outbound[0]));
        typeof(MarkerRecord).GetField("entry",F)!.SetValue(wire,Portal);
        b.Receive(wire);
        Check(b.Records.Count==0 && b.Outbound.Count==1,"another player removes its stored portal and retains the deletion for offline peers");
        for(int i=0;i<100;i++){a.Receive(Record("77:123"));b.Receive(Record("77:123"));b.Receive(Record("77:123",true));}
        Check(a.Records.Count==0 && b.Records.Count==0 && b.Outbound.Count==1,"delayed discoveries and duplicate deletions cannot resurrect or multiply a portal");
        b.Receive(Record("77:124"));b.Receive(Record("77:123",true));
        Check(b.Records.Count==1 && b.Records.ContainsKey("77:124"),"a new native ID at identical coordinates survives deletion of the old portal");
        var beforeDeletion=new Client();beforeDeletion.Receive(Record("77:123"));beforeDeletion.Receive(Record("77:124"));
        Check(beforeDeletion.Records.Count==2,"old and rebuilt portals never merge solely because their positions match");
        var clean=A.GetType("MapTracker.SharedDiscoveries")!.GetMethod("RemoveDuplicates",S)!;
        clean.Invoke(null,new object[]{beforeDeletion.Records,(Action<MarkerRecord>)(_=>{})});
        Check(beforeDeletion.Records.Count==2,"archive deduplication preserves distinct native portal identities");
        beforeDeletion.Receive(Record("77:123",true));
        Check(beforeDeletion.Records.Count==1 && beforeDeletion.Records.ContainsKey("77:124"),"late deletion selects the old ID, not the replacement at the same position");
        beforeDeletion.Receive(Record("77:124",true));
        Check(beforeDeletion.Records.Count==0 && beforeDeletion.Outbound.Count==2,"a subsequently destroyed replacement has its own independent retirement");
        var unseen=new Client();unseen.Receive(Record("77:123",true));unseen.Receive(Record("77:123"));
        Check(unseen.Records.Count==0 && unseen.Outbound.Count==1,"a deletion arriving before discovery blocks the later stale packet");
        var nonPortal=Record("77:200",true);typeof(MarkerRecord).GetField("entry",F)!.SetValue(nonPortal,new CatalogEntry{key="ore",category="Ore"});
        Check(!(bool)Call(unseen.Instance,"HandlePortalState",nonPortal),"collected resources retain their existing behavior");
        unseen.Receive(Record("invalid-id",true));
        Check(unseen.Outbound.Count==1,"malformed native identities cannot create deletion state");

        // Exercise the actual sliced save writer and actual Tracking.Load, not a mock serializer.
        object prepared=null!;
        var prepare=A.GetType("MapTracker.SavedMarkerPreparation")!.GetMethod("PrepareWithRetirements",S)!;
        var rows=((Dictionary<string,MarkerRecord>)Get(b.Ledger,"records")).Values;
        var steps=(IEnumerable<byte>)prepare.Invoke(null,new object[]{"world-a","player-a",b.Records.Values,rows,(Action<object>)(f=>prepared=f),null!})!;
        foreach(var step in steps){}
        var output=new StringWriter();Call(prepared,"WriteTo",output);
        var file=DataJson.Read<MarkerFile>(output.ToString());
        Check(file.markers.Count==1 && file.removedPortals.Count==1 && file.removedPortals[0].id=="77:123","save file separates visible portals from persistent deletion identities");
        string path=Path.Combine(Path.GetTempPath(),"maptracker-portal-check-"+Guid.NewGuid()+".json");
        try
        {
            File.WriteAllText(path,output.ToString());var reload=new Client();
            Set(reload.Instance,"savePath",path);Set(reload.Instance,"world","world-a");Set(reload.Instance,"character","player-a");Call(reload.Instance,"Load");
            reload.Receive(Record("77:123"));reload.Receive(Record("77:125"));
            Check(reload.Records.Count==2 && !reload.Records.ContainsKey("77:123"),"restart reload rejects old packets while accepting new portals at the same position");
            var otherWorld=new Client();Set(otherWorld.Instance,"savePath",path);Set(otherWorld.Instance,"world","world-b");Set(otherWorld.Instance,"character","player-a");Call(otherWorld.Instance,"Load");
            otherWorld.Receive(Record("77:123"));
            Check(otherWorld.Records.ContainsKey("77:123"),"deletions never cross world boundaries");
        }
        finally { File.Delete(path); }
        Check(DataJson.Read<MarkerFile>("{\"schema\":1,\"markers\":[]}").removedPortals.Count==0,"older saves without a deletion list remain readable");
        var local=new Client(); var friend=new Client();
        local.Receive(Record("88:1"));friend.Receive(Record("88:1"));
        var hiddenRecord=local.Records["88:1"];
        hiddenRecord.shared=false;
        Call(Get(local.Instance,"discoveryRoster"),"Update",hiddenRecord);
        Check(local.Outbound.Count==1,"locally observed portal starts in outbound discoveries");
        Check((bool)Call(local.Instance,"HidePortalLocally",hiddenRecord),"portal map deletion accepts a current portal");
        Check(local.Records.Count==0 && local.Outbound.Count==0 && friend.Records.Count==1 &&
            !((bool)Call(local.Ledger,"Contains","88:1")),"local removal sends no deletion and leaves another player's map intact");
        Check(!(bool)Call(local.Instance,"ContainsOutboundDiscovery",hiddenRecord),"a queued copy is no longer eligible after local removal");
        for(int i=0;i<100;i++)local.Receive(Record("88:1"));
        Check(local.Records.Count==0,"repeated archived discoveries do not undo local removal");
        object hiddenFile=null!;
        foreach(var step in (IEnumerable<byte>)prepare.Invoke(null,new object[]{"world-a","player-a",local.Records.Values,
            Array.Empty<MarkerRecord>(),(Action<object>)(f=>hiddenFile=f),Get(local.Instance,"locallyHiddenPortals")})!){}
        var hiddenOutput=new StringWriter();Call(hiddenFile,"WriteTo",hiddenOutput);
        var savedLocal=DataJson.Read<MarkerFile>(hiddenOutput.ToString());
        Check(savedLocal.locallyHiddenPortals.SequenceEqual(new[]{"88:1"}) && savedLocal.removedPortals.Count==0,
            "local suppression is saved separately from synchronized destruction");
        string localPath=Path.Combine(Path.GetTempPath(),"maptracker-local-portal-"+Guid.NewGuid()+".json");
        try
        {
            File.WriteAllText(localPath,hiddenOutput.ToString());var reload=new Client();
            Set(reload.Instance,"savePath",localPath);Set(reload.Instance,"world","world-a");Set(reload.Instance,"character","player-a");Call(reload.Instance,"Load");
            reload.Receive(Record("88:1"));
            Check(reload.Records.Count==0,"local portal removal survives a world reload");
            var seen=Record("88:1");seen.observed=true;reload.Receive(seen);
            Check(reload.Records.ContainsKey("88:1") && ((HashSet<string>)Get(reload.Instance,"locallyHiddenPortals")).Count==0,
                "a friend's positive sighting restores the same portal and clears local suppression");
        }
        finally { File.Delete(localPath); }
        local.Receive(Record("88:2"));
        Check(local.Records.ContainsKey("88:2") && !local.Records.ContainsKey("88:1"),"a different portal ID at the same place is never locally hidden");
        Call(local.Instance,"RestoreLocallyHiddenPortal","88:1");local.Receive(Record("88:1"));
        Check(local.Records.ContainsKey("88:1"),"a successful local world scan can clear suppression for rediscovery");
        Call(local.Instance,"HidePortalLocally",local.Records["88:1"]);
        local.Receive(Record("88:1",true));var obsolete=Record("88:1");obsolete.observed=true;local.Receive(obsolete);
        Check(!local.Records.ContainsKey("88:1") && (bool)Call(local.Ledger,"Contains","88:1"),
            "confirmed destruction supersedes local hiding and rejects even stale positive sightings");
        var other=new MarkerRecord{id="88:3",type="ore"};typeof(MarkerRecord).GetField("entry",F)!.SetValue(other,new CatalogEntry{key="ore",category="Ore"});
        local.Records[other.id]=other;
        Check(!(bool)Call(local.Instance,"HidePortalLocally",other) && local.Records.ContainsKey(other.id),"map removal leaves non-portal discoveries untouched");
        Check(DataJson.Read<MarkerFile>("{\"schema\":1,\"markers\":[]}").locallyHiddenPortals.Count==0,"older saves do not require local suppression metadata");
        System.Console.WriteLine("Portal retirement checks passed: "+passed);
    }
}
