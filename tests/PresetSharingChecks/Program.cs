using MapTracker;
using System.IO.Compression;
using System.Text;

static class Checks
{
    static int passed;
    static void Check(bool result,string label){if(!result)throw new Exception("FAIL: "+label);passed++;System.Console.WriteLine("PASS: "+label);}
    static bool Rejects(Action action){try{action();return false;}catch(InvalidDataException){return true;}catch(ArgumentException){return true;}}
    static FilterPreset Sample(string name="Scout",int count=30)=>new(){name=name,types=Enumerable.Range(0,count).ToDictionary(i=>"prefab:"+i,new Func<int,FilterPresetType>(i=>new(){selected=i%2==0,keep=i%3==0,notify=i%3==0?null:i%2==0}))};
    static void Pass(PresetTransfer source,long sourceId,PresetTransfer target,string name="Alice",float now=1,bool blocked=false)
    {var delivery=source.Take();if(delivery!=null){source.Sent(delivery);target.Receive(sourceId,name,DataJson.Read<PresetMessage>(DataJson.Write(delivery.Message)),now,blocked);}}
    sealed class Client
    {
        internal long Id; internal Plugin Host=new();internal ZNet Net=new();internal ZRoutedRpc Rpc=new();internal PresetSharing Share;
        internal Client(long id){Id=Net.Id=Rpc.Id=id;Share=new(Host);}
        internal void Context(){ZNet.instance=Net;ZRoutedRpc.instance=Rpc;Player.m_localPlayer=new();}
    }
    sealed class Sent {internal long From,To;internal string Method,Json;internal float At;internal int Bytes;}
    sealed class Network
    {
        internal Client A=new(1),B=new(2);internal List<Sent> Sent=new();internal Queue<Sent> Queue=new();internal float Now;
        internal Network()
        {
            foreach(var client in new[]{A,B}){
                client.Net.Players=new(){new(){m_name="Alice",m_characterID=new(){UserID=1}},new(){m_name="Bob",m_characterID=new(){UserID=2}}};
                client.Rpc.Send=(from,to,method,json)=>{var s=new Sent{From=from,To=to,Method=method,Json=json,At=Now,Bytes=Encoding.UTF8.GetByteCount(json)+128};Sent.Add(s);Queue.Enqueue(s);};
            }
            Run(0.1f);
        }
        internal void Run(float seconds)
        {
            float end=Now+seconds;
            for(;Now<end;Now+=0.05f){UnityEngine.Time.realtimeSinceStartup=Now;
                foreach(var client in new[]{A,B}){client.Context();client.Share.Tick();}
                while(Queue.TryDequeue(out var s)){var target=s.To==1?A:s.To==2?B:null;
                    if(target==null)continue;target.Context();if(target.Rpc.Handlers.TryGetValue(s.Method,out var handler))handler(s.From,s.Json);}
            }
        }
        internal void Start(FilterPreset preset){A.Context();Check(A.Share.Send(2,preset),"explicit addressed offer starts");}
    }
    public static void Main()
    {
        var original=Sample("Scout “North”",5000);var packed=PresetCodec.Encode(original);var decoded=PresetCodec.Decode(packed);
        Check(decoded.name==original.name&&decoded.types.Count==5000&&decoded.types.All(p=>p.Value.selected==original.types[p.Key].selected&&p.Value.keep==original.types[p.Key].keep&&p.Value.notify==original.types[p.Key].notify),"all filters and nullable Notify values round-trip in a large preset");
        Check(Rejects(()=>PresetCodec.Encode(Sample("bad\nname")))&&Rejects(()=>PresetCodec.Encode(Sample("x",8193))),"invalid names and excessive type counts are rejected");
        using(var memory=new MemoryStream()){
            using(var zip=new GZipStream(memory,CompressionMode.Compress,true)){var raw=new byte[PresetCodec.MaxRaw+1];zip.Write(raw);}
            Check(Rejects(()=>PresetCodec.Decode(memory.ToArray())),"compressed input cannot expand past the raw byte limit");
        }
        Check(Rejects(()=>PresetCodec.Decode(new byte[PresetCodec.MaxPacked+1])),"oversized compressed payload is rejected");
        var batchedInbox=new PresetInbox();
        Check(Enumerable.Range(0,44).All(_=>batchedInbox.Enqueue(1,new string('x',2200))),"all chunks of a maximum-size preset may arrive in one network batch without per-peer drops");
        int batched=0;while(batchedInbox.Take()!=null)batched++;
        Check(batched==44&&!batchedInbox.Enqueue(1,new string('x',4097)),"batched chunks are retained and oversized RPC input is rejected");
        var folder=Path.Combine(BepInEx.Paths.ConfigPath,"storage");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"presets.json");var store=new FilterPresetStore(path);
        store.Save(Sample("Scout"));string name=store.Import(Sample("Scout"));
        Check(name=="Scout (2)"&&store.Read().Count==2&&File.Exists(path+".bak"),"import preserves an existing preset and backs up the document");
        Check(store.Import(Sample("Default"))=="Default (2)","imported built-in names do not collide with local built-ins");
        string longName=new('x',80);store.Save(Sample(longName));Check(store.Import(Sample(longName)).Length==80,"collision suffix respects the name limit");
        File.WriteAllText(path,"broken");string corrupt=File.ReadAllText(path);try{store.Import(Sample("new"));}catch(Newtonsoft.Json.JsonException){}
        Check(File.ReadAllText(path)==corrupt,"invalid local storage is never overwritten by an import");

        var saved=new List<FilterPreset>();var a=new PresetTransfer("123",p=>p.name);var b=new PresetTransfer("123",p=>{saved.Add(p);return p.name;});
        a.Start(2,original,0);Pass(a,1,b);
        Check(b.Pending!=null&&b.Pending.Sender=="Alice"&&!b.Pending.Accepted&&saved.Count==0&&a.Take()==null,"offer carries metadata only; no filters are sent before consent");
        Check(a.OutgoingProgress.Percent==0&&a.OutgoingProgress.Phase.Contains("acceptance")&&b.IncomingProgress==null,"sender waits at zero percent and receiver has no progress card before consent");
        b.Decide(true,2);Pass(b,2,a,"Bob",2);var chunks=new List<PresetTransfer.Delivery>();PresetTransfer.Delivery chunk;
        while((chunk=a.Take())!=null)chunks.Add(chunk);
        Check(a.OutgoingProgress.Bytes==0&&b.IncomingProgress.Percent==0,"queuing payload without a successful socket send does not advance progress");
        a.SendingPaused(true);Check(a.OutgoingProgress.Phase=="Waiting for connection","network congestion is shown as a paused send");
        a.SendingPaused(false);
        foreach(var c in chunks){a.Sent(c);a.Sent(c);}
        Check(a.OutgoingProgress.Bytes==packed.Length&&a.OutgoingProgress.Percent==99&&a.OutgoingProgress.Phase.Contains("confirmation"),"successful sends count unique bytes and stop at 99 percent until the receiver confirms saving");
        Check(chunks.Count>1&&chunks.All(c=>Encoding.UTF8.GetByteCount(DataJson.Write(c.Message))+128<=4096),"large payload is split into bounded RPC packets");
        foreach(var c in chunks.AsEnumerable().Reverse()){b.Receive(1,"Alice",c.Message,3,false);b.Receive(1,"Alice",c.Message,3,false);}
        Check(saved.Count==1&&saved[0].types.Count==5000&&b.Pending==null,"out-of-order duplicate chunks import exactly one preset");
        Check(b.IncomingProgress.Bytes==packed.Length&&b.IncomingProgress.Percent==100&&b.IncomingProgress.Saved,"receiver counts unique chunks and reports 100 percent only after the preset is saved");
        Pass(b,2,a,"Bob",4);Check(!a.Busy&&a.Status.Contains("added"),"sender receives confirmation only after successful saving");
        Check(a.OutgoingProgress.Percent==100&&!a.OutgoingProgress.Active&&a.OutgoingProgress.Saved,"sender reaches 100 percent on save confirmation");
        b.Tick(10,false,_=>true);a.Tick(10,false,_=>true);
        Check(a.OutgoingProgress==null&&b.IncomingProgress==null,"completed progress cards disappear after their display interval");
        var other=new PresetTransfer("123",p=>throw new Exception("must not save"));a.Start(2,Sample(),40);Pass(a,1,other,now:40,blocked:true);
        Check(other.Pending==null&&other.Take()==null,"blocked recipient neither prompts nor replies");
        a.Tick(200,false,_=>true);Check(!a.Busy,"unanswered or blocked offers expire without retries");
        a.Start(2,Sample(),201);Pass(a,1,other,now:201);other.Decide(false,202);Pass(other,2,a,"Bob",202);
        Check(!a.Busy&&a.Status.Contains("declined")&&other.Pending==null,"decline ends the offer without sending filters");
        Check(!a.OutgoingProgress.Active&&!a.OutgoingProgress.Saved&&a.OutgoingProgress.Percent==0,"decline never shows a completed progress bar");
        a.Start(2,Sample(),240);Pass(a,1,other,now:240);other.Decide(true,241);other.Tick(242,true,_=>true);
        Check(other.Pending==null&&other.Take()==null,"turning blocking on cancels a queued acceptance silently");
        Check(other.IncomingProgress==null,"blocking hides a cancelled incoming transfer's progress");
        a.Tick(500,false,_=>false);Check(!a.Busy,"disconnect cancels sender state");
        a.Start(2,Sample(),501);var offer=a.Take();other.Receive(1,"Alice",offer.Message,501,false);other.Decide(true,502);
        var accept=other.Take();a.Receive(99,"Mallory",accept.Message,503,false);
        Check(a.Take()==null,"acceptance from a different peer cannot release preset data");
        offer.Message.world="different";var wrongWorld=new PresetTransfer("123",p=>p.name);wrongWorld.Receive(1,"Alice",offer.Message,504,false);
        Check(wrongWorld.Pending==null,"cross-world offers are ignored");
        var fail=new PresetTransfer("123",p=>throw new IOException("disk full"));var sender=new PresetTransfer("123",p=>p.name);
        sender.Start(2,Sample(),0);Pass(sender,1,fail);fail.Decide(true,1);Pass(fail,2,sender,"Bob");while(sender.HasOutput)Pass(sender,1,fail);
        Pass(fail,2,sender,"Bob");Check(!sender.Busy&&sender.Status.Contains("could not")&&fail.Notice.Contains("kept"),"save failure leaves existing presets intact and reports failure");
        Check(!fail.IncomingProgress.Active&&!fail.IncomingProgress.Saved&&fail.IncomingProgress.Percent<100&&sender.OutgoingProgress.Percent<100,"a failed disk write shows failure rather than 100 percent on either side");
        var tamper=new PresetTransfer("123",p=>throw new Exception("bad hash saved"));sender.Start(2,Sample(),40);var tampered=sender.Take();tampered.Message.hash=new string('0',64);tamper.Receive(1,"Alice",tampered.Message,40,false);tamper.Decide(true,41);Pass(tamper,2,sender,"Bob",41);while(sender.HasOutput)Pass(sender,1,tamper,now:42);
        Check(tamper.Pending==null&&tamper.Notice.Contains("Could not"),"checksum mismatch does not import a preset");
        var receivedA=new List<string>();var receivedB=new List<string>();
        var bothA=new PresetTransfer("123",p=>{receivedA.Add(p.name);return p.name;});
        var bothB=new PresetTransfer("123",p=>{receivedB.Add(p.name);return p.name;});
        bothA.Start(2,Sample("From A",1000),0);bothB.Start(1,Sample("From B",1000),0);
        Pass(bothA,1,bothB);Pass(bothB,2,bothA,"Bob");bothA.Decide(true,2);bothB.Decide(true,2);
        for(int i=0;i<30;i++){Pass(bothA,1,bothB,now:3+i);Pass(bothB,2,bothA,"Bob",3+i);}
        Check(receivedA.SequenceEqual(new[]{"From B"})&&receivedB.SequenceEqual(new[]{"From A"})&&!bothA.Busy&&!bothB.Busy,"simultaneous sharing in both directions keeps consent and payloads separate");
        var missing=new PresetTransfer("123",p=>throw new Exception("disconnected import"));
        bothA.Start(2,Sample(),100);Pass(bothA,1,missing,now:100);missing.Decide(true,101);missing.Tick(102,false,_=>false);
        Check(missing.Pending==null&&missing.Take()==null,"recipient discards an accepted transfer and queued acceptance when the sender disconnects");

        var net=new Network();net.Run(180);Check(net.Sent.Count==0,"idle manual preset sharing produces zero announcements, data or acknowledgements");
        net.A.Context();var roster=PresetSharing.Players();Check(roster.Count==1&&roster[0].Id==2,"online recipient roster excludes self and includes players with private positions");
        Check(!net.A.Share.Recipients().Single().CanShare&&!net.A.Share.Send(2,Sample()),"unverified players cannot be selected or sent a preset");
        net.A.Share.CheckRecipients(true);net.Run(2);net.A.Context();
        Check(net.A.Share.Recipients().Single().CanShare&&net.Sent.Count==2,"opening the chooser checks preset support with one addressed request and reply");
        int verifiedTraffic=net.Sent.Count;for(int i=0;i<10;i++)net.A.Share.CheckRecipients(true);net.Run(2);
        Check(net.Sent.Count==verifiedTraffic&&net.B.Share.Transfer.Pending==null,"repeated chooser refreshes do not flood probes or display preset offers");
        int beforeOffer=net.Sent.Count;
        net.Start(Sample("Network",5000));net.Run(1);Check(net.B.Share.Transfer.Pending!=null&&net.Sent.Count==beforeOffer+1,"only the chosen player receives one metadata offer");
        net.B.Context();net.B.Share.Decide(true);net.Run(0.4f);net.A.Net.Peers[0].m_socket.Queue=5000;
        int before=net.Sent.Count,bytesBefore=net.A.Share.Transfer.OutgoingProgress.Bytes;
        net.Run(3);Check(net.Sent.Count==before,"native socket congestion pauses preset transmission");
        Check(net.A.Share.Transfer.OutgoingProgress.Bytes==bytesBefore&&net.A.Share.Transfer.OutgoingProgress.Phase=="Waiting for connection","production transport holds the progress bar while the native queue is congested");
        net.A.Net.Peers[0].m_socket.Queue=0;net.Run(45);
        Check(!net.A.Share.Transfer.Busy&&net.A.Share.Transfer.Status.Contains("added")&&net.B.Host.Window.Refreshes==1,"production transport completes an accepted transfer and refreshes the receiver list without applying filters");
        Check(net.A.Host.Config.Saved==0&&net.B.Host.Config.Saved==0,"sharing and import never apply filters or change configurator settings");
        Check(net.Sent.All(s=>s.To!=0&&s.Bytes<=4096),"every RPC is individually addressed and bounded");
        foreach(var source in new[]{1L,2L}){
            var sent=net.Sent.Where(s=>s.From==source).OrderBy(s=>s.At).ToList();
            Check(sent.Sum(s=>s.Bytes)<=4224+2048*(sent.Last().At-sent.First().At),"outgoing traffic obeys the 2 KiB/s token budget for player "+source);
        }
        int idle=net.Sent.Count;net.Run(180);Check(net.Sent.Count==idle,"completed transfer returns to zero traffic");
        net.B.Host.BlockPresetOffers.Value=true;net.Start(Sample("Ignored"));net.Run(3);Check(net.B.Share.Transfer.Pending==null&&net.Sent.Count==idle+1,"blocked offers are silent through the production RPC path");
        net.Run(125);Check(!net.A.Share.Transfer.Busy,"blocked recipient does not cause resends or permanent busy state");
        net.A.Context();net.Start(Sample("old world"));net.A.Net.World=456;net.Run(1);Check(!net.A.Share.Transfer.Busy&&net.A.Share.Transfer.World=="456","changing worlds clears old outbound and consent state");
        net.A.Host.Enabled.Value=false;int disabled=net.Sent.Count;net.Run(2);Check(net.A.Share.Transfer==null&&net.Sent.Count==disabled,"disabled mod clears pending transfers without network output");
        Check(new FilterPresetStore(Path.Combine(BepInEx.Paths.ConfigPath,"MapTracker","presets.json")).Read().Single().name=="Network","received preset survives reading the persisted profile document");
        var absent=new Network();absent.B.Rpc.Handlers.Clear();absent.A.Context();absent.A.Share.CheckRecipients();absent.Run(6);absent.A.Context();
        var unavailable=absent.A.Share.Recipients().Single();
        Check(unavailable.State==PresetSharing.Availability.Unavailable&&!unavailable.CanShare&&unavailable.Hint.Contains("not installed")&&!absent.A.Share.Send(2,Sample()),"players without a responding mod are unavailable with an explanatory hint and cannot receive offers");
        int absentTraffic=absent.Sent.Count;absent.Run(180);Check(absentTraffic==1&&absent.Sent.Count==1,"a vanilla client gets only the explicit chooser probe; there are no automatic retries or idle broadcasts");
        var query=DataJson.Read<PresetMessage>(absent.Sent[0].Json);
        var fakeReply=new PresetMessage{world="123",kind="capability",id=Guid.NewGuid().ToString("N"),features=3};
        absent.A.Context();absent.A.Rpc.Handlers["MapTracker.Presets.v1"](2,DataJson.Write(fakeReply));absent.Run(0.1f);absent.A.Context();
        Check(!absent.A.Share.Recipients().Single().CanShare,"an unsolicited capability reply cannot enable a vanilla recipient");
        fakeReply.id=query.id;fakeReply.world="other";absent.A.Rpc.Handlers["MapTracker.Presets.v1"](2,DataJson.Write(fakeReply));absent.Run(0.1f);absent.A.Context();
        Check(!absent.A.Share.Recipients().Single().CanShare,"capability replies from another world are ignored");
        var refusing=new Network();refusing.B.Host.BlockPresetOffers.Value=true;refusing.A.Context();refusing.A.Share.CheckRecipients();refusing.Run(2);refusing.A.Context();
        var declined=refusing.A.Share.Recipients().Single();
        Check(declined.State==PresetSharing.Availability.NotAccepting&&!declined.CanShare&&declined.Hint.Contains("blocked")&&refusing.B.Share.Transfer.Pending==null,
            "installed clients blocking offers get their own unavailable reason without an acceptance popup");
        absent.A.Context();absent.A.Net.Players.RemoveAll(p=>p.m_characterID.UserID==2);absent.A.Share.CheckRecipients();
        Check(absent.A.Share.Recipients().Count==0&&!absent.A.Share.Send(2,Sample()),"disconnected recipients and their cached capabilities are removed");
        Directory.Delete(BepInEx.Paths.ConfigPath,true);
        System.Console.WriteLine($"Passed {passed} preset-sharing checks. Game rendering and live multiplayer are not simulated.");
    }
}
