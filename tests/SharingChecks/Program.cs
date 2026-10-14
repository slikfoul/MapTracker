using MapTracker;
using UnityEngine;
using System.Text;

static class Checks
{
    static int passed;
    static void Check(bool result, string label) { if(!result) throw new Exception("FAIL: "+label); passed++; System.Console.WriteLine("PASS: "+label); }
    sealed class Client
    {
        internal Plugin Host = new(); internal ZNet Net = new(); internal ZRoutedRpc Rpc = new(); internal MarkerSharing Sharing;
        internal bool Online = true;
        internal Client(long id) { Net.Id=Rpc.Id=id; Sharing=new(Host); }
        internal void Context() { ZNet.instance=Net; ZRoutedRpc.instance=Rpc; Player.m_localPlayer=Online ? new Player() : null; }
    }
    sealed class Sent { internal long Source, Destination; internal string Method, Json; internal float At, Due; internal int Bytes; }
    sealed class Network
    {
        internal List<Client> Clients = new(); internal List<Sent> Sent = new();
        readonly Queue<Sent> waiting = new();
        internal Func<Sent,bool> Drop = _=>false;
        internal Func<Sent,float> Delay = _=>0;
        internal Func<Sent,bool> Duplicate = _=>false;
        internal float FrameSeconds = 0.05f;
        internal float Now;
        internal Network(int players)
        {
            Time.realtimeSinceStartup=0;
            for(int i=1;i<=players;i++) {
                var client=new Client(i); Clients.Add(client);
                client.Rpc.Send=(source,destination,method,json)=> {
                    var packet=new Sent { Source=source,Destination=destination,Method=method,Json=json,At=Now,
                        Bytes=Encoding.UTF8.GetByteCount(json)+SharingBudget.EnvelopeBytes };
                    packet.Due=Now+Delay(packet);
                    Sent.Add(packet);
                    if(!Drop(packet)) {
                        waiting.Enqueue(packet);
                        if(Duplicate(packet))waiting.Enqueue(new Sent {Source=source,Destination=destination,Method=method,Json=json,At=Now,Due=packet.Due+0.2f,Bytes=packet.Bytes});
                    }
                };
            }
        }
        internal void Run(float seconds, Action<float> step=null)
        {
            float end=Now+seconds;
            while(Now<end) {
                Now+=FrameSeconds; Time.realtimeSinceStartup=Now; step?.Invoke(Now);
                foreach(var client in Clients) {client.Context();client.Sharing.Tick();}
                int pending=waiting.Count;
                for(int i=0;i<pending;i++) {
                    var packet=waiting.Dequeue();
                    if(packet.Due>Now) {waiting.Enqueue(packet);continue;}
                    foreach(var client in Clients) if(client.Online && client.Net.Id!=packet.Source && (packet.Destination==0 || packet.Destination==client.Net.Id)) {
                        client.Context(); if(client.Rpc.Handlers.TryGetValue(packet.Method,out var handler))handler(packet.Source,packet.Json);
                    }
                }
            }
        }
    }
    static MarkerRecord Record(int i) => new() {id="123:"+i,type="portal_wood",position=new Vector3(i,0,i),portalTag="Portal "+i};
    static bool IsData(Sent s) => s.Method=="MapTracker.Discoveries.Data.v3";
    static void Main()
    {
        var loading = new Network(2);
        foreach (var client in loading.Clients) client.Host.Tracking.Ready = false;
        loading.Clients[0].Host.Tracking.Static.Add(Record(1));
        loading.Run(10);
        Check(loading.Sent.Count == 0, "no announcements, snapshots or data before saved-marker preparation finishes");
        foreach (var client in loading.Clients) client.Host.Tracking.Ready = true;
        loading.Run(5);
        Check(loading.Clients[1].Host.Tracking.Received.ContainsKey("123:1"), "sharing starts normally after saved markers finish preparing");
        var off=new Network(2);
        foreach(var c in off.Clients){c.Host.ShareMarkers.Value=false;c.Host.ShareCreatures.Value=false;c.Host.OtherPlayerPins.Value=true;}
        off.Run(90);
        Check(off.Sent.Count==0,"both Share channels off produce zero mod traffic even when ordinary game pins are enabled");
        off.Clients[0].Context();off.Clients[0].Rpc.InvokeRoutedRPC(0,"MapTracker.Discoveries.Hello.v1","1:123");
        off.Run(1);
        Check(off.Sent.Count==1,"legacy v1 handshakes never trigger unbounded discovery traffic");
        off.Clients[0].Context();off.Clients[0].Rpc.InvokeRoutedRPC(0,"MapTracker.Discoveries.Hello.v2","2:123");
        off.Run(1);
        Check(off.Sent.Count==2,"legacy v2 capability handshakes cannot enable incompatible sharing");
        var creaturesOnly=new Network(2);
        creaturesOnly.Clients[0].Host.ShareMarkers.Value=false;
        creaturesOnly.Clients[0].Host.ShareCreatures.Value=true;
        creaturesOnly.Clients[1].Host.ShareCreatures.Value=true;
        creaturesOnly.Clients[0].Host.Tracking.Creatures.Add(new MarkerRecord {id="mob:1",type="leech",creatureLevel=1,creatureRelation=1});
        creaturesOnly.Clients[0].Host.Tracking.Static.Add(Record(1));
        creaturesOnly.Run(5);
        Check(creaturesOnly.Clients[1].Host.Tracking.Received.ContainsKey("mob:1") && !creaturesOnly.Clients[1].Host.Tracking.Received.ContainsKey("123:1"),
            "creature transmission works independently of static discovery transmission");
        var switching=new Network(2);
        switching.Clients[0].Host.Tracking.Static=Enumerable.Range(0,1000).Select(Record).ToList();
        switching.Run(2);
        switching.Clients[0].Host.ShareMarkers.Value=false;
        switching.Clients[0].Host.OtherPlayerPins.Value=false;
        int sentBeforeStop=switching.Sent.Count(s=>s.Source==1);
        switching.Run(35);
        Check(switching.Sent.Count(s=>s.Source==1)==sentBeforeStop,"switching sharing off during a transfer stops data, retries, acknowledgements and hello replies");
        switching.Clients[0].Host.ShareMarkers.Value=true;
        switching.Clients[0].Host.Tracking.Static[0].portalTag="After restart";
        switching.Run(12);
        Check(switching.Clients[1].Host.Tracking.Received["123:0"].portalTag=="After restart","a new sharing session resets receiver sequence state and propagates changes after re-enabling");

        var idle=new Network(2);
        idle.Clients[0].Host.Tracking.Static=Enumerable.Range(0,1000).Select(Record).ToList();
        idle.Run(100);
        Check(idle.Clients[1].Host.Tracking.Received.Count==1000,"initial retained discoveries arrive completely through acknowledged small batches");
        int before=idle.Sent.Count(IsData), applied=idle.Clients[1].Host.Tracking.Applied;
        idle.Run(20);
        Check(idle.Sent.Count(IsData)==before && idle.Clients[1].Host.Tracking.Applied==applied,"periodic presence does not resend an unchanged database");
        idle.Clients[0].Host.Tracking.Static[3].portalTag="Renamed";
        idle.Run(10);
        Check(idle.Clients[1].Host.Tracking.Received["123:3"].portalTag=="Renamed","a portal rename propagates as a delta after the initial sync");
        Check(idle.Sent.Where(IsData).All(s=>Encoding.UTF8.GetByteCount(s.Json)<=4096),"all marker packets respect the UTF-8 byte limit");

        var retries=new Network(2); int lost=0;
        retries.Clients[0].Host.Tracking.Static=Enumerable.Range(0,24).Select(Record).ToList();
        retries.Drop=s=>s.Method.EndsWith("Processed.v3") && lost++<3;
        retries.Run(45);
        Check(retries.Clients[1].Host.Tracking.Received.Count==24 && retries.Clients[1].Host.Tracking.Applied==24,"lost acknowledgements retry identical packets without applying duplicates");

        var blocked=new Network(3);
        blocked.Clients[0].Host.Tracking.Static=Enumerable.Range(0,1000).Select(Record).ToList();
        blocked.Drop=s=>s.Source==2 && s.Method.EndsWith("Processed.v3");
        blocked.Run(40);
        Check(blocked.Sent.Count(s=>IsData(s)&&s.Source==1&&s.Destination==2)<=6,"an unresponsive receiver is limited to one pending packet and slow retries");
        Check(blocked.Clients[2].Host.Tracking.Received.Count>100,"a stalled receiver does not block discovery sync to other players");

        var busy=new Network(2);
        busy.Clients[0].Host.Tracking.Static=Enumerable.Range(0,1000).Select(Record).ToList();
        foreach(var c in busy.Clients)c.Net.Peers[0].m_socket.Queue=4096;
        busy.Run(30); Check(busy.Sent.Count==0,"native send-queue congestion suppresses all mod traffic");
        foreach(var c in busy.Clients)c.Net.Peers[0].m_socket.Queue=0;
        busy.Run(5);Check(busy.Clients[1].Host.Tracking.Received.Count>0,"sharing resumes after native congestion clears");

        var stress=new Network(5);
        stress.Clients[0].Host.ShareCreatures.Value=true;
        stress.Clients[0].Host.Tracking.Static=Enumerable.Range(0,10000).Select(Record).ToList();
        stress.Clients[0].Host.Tracking.Creatures=Enumerable.Range(0,300).Select(i=>new MarkerRecord{id="mob:"+i,type="leech",creatureLevel=1,creatureRelation=1}).ToList();
        stress.Run(60,t=>{foreach(var r in stress.Clients[0].Host.Tracking.Creatures)r.position=new Vector3(t,0,0);});
        var sent=stress.Sent.Where(s=>s.Source==1).ToList();
        for(int second=0;second<60;second++) {
            int bytes=sent.Where(s=>s.At>=second&&s.At<second+1).Sum(s=>s.Bytes);
            Check(bytes<=8192+4224,"aggregate client traffic stays bounded in stress second "+second);
        }
        Check(sent.Sum(s=>s.Bytes)<=60*8192+4224,"five-player stress traffic satisfies the cumulative client budget");
        Check(sent.Where(IsData).Select(s=>s.At).Zip(sent.Where(IsData).Select(s=>s.At).Skip(1),(a,b)=>b-a).All(d=>d>=0.099f),"no update emits a burst of bulk packets to all peers");
        foreach(var c in stress.Clients.Skip(1))Check(c.Host.Tracking.Received.Count>0,"each remote player makes progress under the shared budget");

        var navigation=new Network(2);
        foreach(var c in navigation.Clients)c.Host.OtherPlayerPins.Value=false;
        navigation.Clients[0].Host.Tracking.Static.Add(Record(1));
        navigation.Run(3);Check(navigation.Clients[1].Host.Tracking.Received.Count==1,
            "saved discoveries arrive with Other player pins disabled at both clients");
        navigation.Clients[0].Context();navigation.Clients[0].Sharing.SendNavigationTarget(new Vector3(12,0,42),"Portal","portal_wood","123:1");
        navigation.Run(3);Check(navigation.Clients[1].Host.Tracking.Discoveries.Targets==1,"explicit shared navigation works independently of received discovery pins");
        navigation.Clients[1].Host.OtherPlayerPins.Value=true;
        int dataBeforePinToggle=navigation.Sent.Count(IsData);
        navigation.Run(5);Check(navigation.Clients[1].Host.Tracking.Received.Count==1 && navigation.Sent.Count(IsData)==dataBeforePinToggle,
            "toggling ordinary pin visibility neither restarts discovery transmission nor changes reception");

        var inbox=new SharingInbox();
        Check(!inbox.Enqueue(1,new string('界',1400)),"multibyte messages are bounded by bytes rather than only character count");
        Check(inbox.Enqueue(1,"{}")&&!inbox.Enqueue(1,"{}"),"one sender cannot fill the inbound queue with repeated packets");
        inbox.Take();Check(inbox.Enqueue(1,"{}"),"processing releases the inbound credit");

        var slow=new Network(3) {FrameSeconds=0.2f};
        slow.Delay=s=>0.6f+(s.Json.Length%7)*0.1f;
        slow.Clients[0].Host.Tracking.Static=Enumerable.Range(0,80).Select(Record).ToList();
        slow.Run(110);
        Check(slow.Clients.Skip(1).All(c=>c.Host.Tracking.Received.Count==80),"high latency, jitter and five updates per second still deliver complete discoveries");
        foreach(var source in slow.Clients)Check(slow.Sent.Where(s=>s.Source==source.Net.Id).Sum(s=>s.Bytes)<=110*8192+4224,
            "bad-connection traffic remains inside the cumulative budget for client "+source.Net.Id);

        var duplicates=new Network(2);
        duplicates.Duplicate=IsData;
        duplicates.Clients[0].Host.Tracking.Static=Enumerable.Range(0,80).Select(Record).ToList();
        duplicates.Run(20);
        Check(duplicates.Clients[1].Host.Tracking.Received.Count==80 && duplicates.Clients[1].Host.Tracking.Applied==80,
            "delayed duplicate packets do not create duplicate discoveries or repeat application");

        var delayedStop=new Network(2);
        delayedStop.Delay=_=>1;
        delayedStop.Clients[0].Host.Tracking.Static=Enumerable.Range(0,80).Select(Record).ToList();
        delayedStop.Run(5);
        delayedStop.Clients[0].Host.ShareMarkers.Value=false;
        delayedStop.Clients[0].Host.OtherPlayerPins.Value=false;
        int stopped=delayedStop.Sent.Count(s=>s.Source==1);
        delayedStop.Run(15);
        Check(delayedStop.Sent.Count(s=>s.Source==1)==stopped,"late packets and acknowledgements cannot restart traffic after all options are switched off");
        delayedStop.Clients[0].Host.ShareMarkers.Value=true;
        delayedStop.Clients[0].Host.Tracking.Static[0].portalTag="After delayed restart";
        delayedStop.Run(50);
        Check(delayedStop.Clients[1].Host.Tracking.Received["123:0"].portalTag=="After delayed restart",
            "re-enabling sharing after delayed acknowledgements propagates the current database");

        var disconnect=new Network(2);
        disconnect.Delay=_=>0.4f;
        disconnect.Clients[0].Host.Tracking.Static=Enumerable.Range(0,80).Select(Record).ToList();
        disconnect.Run(4);
        disconnect.Clients[1].Online=false;
        disconnect.Run(100);
        disconnect.Clients[1].Online=true;
        disconnect.Run(80);
        Check(disconnect.Clients[1].Host.Tracking.Received.Count==80,"a receiver returning after peer expiry resumes complete discovery synchronization");

        // Exercise every independent pair of static/live channels with ordinary pins hidden.
        for(int a=0;a<4;a++) for(int b=0;b<4;b++) {
            var matrix=new Network(2); int[] modes={a,b};
            for(int i=0;i<2;i++) {
                var client=matrix.Clients[i];client.Host.OtherPlayerPins.Value=false;
                client.Host.ShareMarkers.Value=(modes[i]&1)!=0;client.Host.ShareCreatures.Value=(modes[i]&2)!=0;
                client.Host.Tracking.Static.Add(Record(i+1));
                client.Host.Tracking.Creatures.Add(new MarkerRecord{id="matrix-mob:"+i,type="leech",creatureLevel=1,creatureRelation=1});
            }
            matrix.Run(5);
            bool staticExchange=(a&1)!=0&&(b&1)!=0, creatureExchange=(a&2)!=0&&(b&2)!=0;
            Check(matrix.Clients[0].Host.Tracking.Received.ContainsKey("123:2")==staticExchange &&
                  matrix.Clients[1].Host.Tracking.Received.ContainsKey("123:1")==staticExchange &&
                  matrix.Clients[0].Host.Tracking.Received.ContainsKey("matrix-mob:1")==creatureExchange &&
                  matrix.Clients[1].Host.Tracking.Received.ContainsKey("matrix-mob:0")==creatureExchange,
                  "independent bidirectional channel matrix "+a+"/"+b+" ignores ordinary pin visibility");
        }

        var queuedTarget = new Network(2);
        queuedTarget.Run(2); queuedTarget.Clients[0].Context();
        queuedTarget.Clients[0].Sharing.SendNavigationTarget(new Vector3(10,0,20), "Queued target");
        queuedTarget.Clients[0].Host.ShareMarkers.Value = false;
        foreach(var client in queuedTarget.Clients)client.Host.ShareCreatures.Value=true;
        queuedTarget.Run(12);
        Check(!queuedTarget.Sent.Any(s => s.Source==1 && IsData(s)),
            "turning off Share discoveries cancels an unsent shared target while the creature channel stays enabled");
        var retryTarget = new Network(2);
        retryTarget.Drop = s => s.Method=="MapTracker.Discoveries.Processed.v3";
        retryTarget.Run(2); retryTarget.Clients[0].Context();
        retryTarget.Clients[0].Sharing.SendNavigationTarget(new Vector3(10,0,20), "Pending target");
        retryTarget.Run(1);
        int targetPackets = retryTarget.Sent.Count(s => s.Source==1 && IsData(s));
        Check(targetPackets == 1, "an unacknowledged target is pending before cancellation");
        retryTarget.Clients[0].Host.ShareMarkers.Value = false;
        foreach(var client in retryTarget.Clients)client.Host.ShareCreatures.Value=true;
        retryTarget.Run(20);
        Check(retryTarget.Sent.Count(s => s.Source==1 && IsData(s)) == targetPackets,
            "turning off Share discoveries cancels target retries without disabling creature exchange");
        var deltaCache = new SharingDelta();
        deltaCache.Commit(Enumerable.Range(0,10000).Select(Record).ToArray(), 0);
        deltaCache.Prune(new HashSet<string> { "123:1", "123:2" });
        var sentField = typeof(SharingDelta).GetField("sent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Check(((System.Collections.IDictionary)sentField.GetValue(deltaCache)).Count == 2,
            "retired creature delta entries are released instead of accumulating for the entire session");
        deltaCache.Prune(new HashSet<string>());
        Check(((System.Collections.IDictionary)sentField.GetValue(deltaCache)).Count == 0,
            "an empty creature snapshot releases all acknowledged creature cache entries");
        var dense=new Network(2);
        foreach(var client in dense.Clients){client.Host.ShareCreatures.Value=true;client.Host.OtherPlayerPins.Value=false;}
        dense.Clients[0].Host.Tracking.Static=Enumerable.Range(0,10000).Select(Record).ToList();
        dense.Clients[0].Host.Tracking.Creatures=Enumerable.Range(0,300).Select(i=>new MarkerRecord{id="dense:"+i,type="leech",creatureLevel=1,creatureRelation=1}).ToList();
        dense.Run(180,t=>{foreach(var creature in dense.Clients[0].Host.Tracking.Creatures)creature.position=new Vector3(t,0,0);});
        var denseReceiver=dense.Clients[1].Host.Tracking;
        Check(denseReceiver.CreatureUntil.Count==300,"all creatures reach a distant client while a large discovery database is synchronizing");
        Check(denseReceiver.SimulatedOldFlickers>0 && denseReceiver.CreatureFlickers==0,
            "the crowded transfer reproduces fifteen-second flicker and avoids it with a budget-aware lease");
        foreach(var client in dense.Clients)client.Host.ShareCreatures.Value=false;
        dense.Run(CreatureSharingLease.Maximum+1);
        Check(denseReceiver.CreatureUntil.Values.All(until=>until<dense.Now),
            "creature observations still expire after transmission stops; leases cannot retain them indefinitely");
        Check(CreatureSharingLease.Seconds(1000000,32)==CreatureSharingLease.Maximum && CreatureSharingLease.Seconds(0,1)==CreatureSharingLease.Minimum,
            "adaptive creature lifetime remains bounded under extreme counts");
        var unsent=new SharingPeer { Pending=new SharingPacket { sequence=4,kind="discoveries",markers=new List<MarkerRecord>{Record(4)} } };
        Check(!unsent.Confirm(4,1)&&unsent.Pending!=null,
            "an acknowledgement cannot commit or discard a packet still waiting for outbound bandwidth");
        unsent.PendingSent=true;
        Check(unsent.Confirm(4,1)&&unsent.Pending==null&&!unsent.PendingSent,
            "a delivered pending packet is committed once and releases the send queue");
        foreach(var client in dense.Clients)Check(dense.Sent.Where(s=>s.Source==client.Net.Id).Sum(s=>s.Bytes)<=dense.Now*8192+4224,
            "dense moving-creature and archive synchronization stays inside the unchanged traffic cap for client "+client.Net.Id);
        System.Console.WriteLine("Sharing transport checks passed: "+passed);
    }
}
