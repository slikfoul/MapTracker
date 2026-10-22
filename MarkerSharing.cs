using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MapTracker
{
    internal sealed class MarkerSharing
    {
        // v3 negotiates independent bidirectional static/creature channels.
        // Older clients interpret these capabilities differently and must update together.
        private const string Hello = "MapTracker.Discoveries.Hello.v3";
        private const string Ack = "MapTracker.Discoveries.Ack.v3";
        private const string Data = "MapTracker.Discoveries.Data.v3";
        private const string Processed = "MapTracker.Discoveries.Processed.v3";
        private readonly Plugin host;
        private ZRoutedRpc rpc;
        private string world;
        private long worldUid;
        private bool active;
        private readonly Dictionary<long, SharingPeer> peers = new Dictionary<long, SharingPeer>();
        private readonly List<long> order = new List<long>();
        private sealed class Control { internal string Method, Value; }
        private readonly Dictionary<long, Control> controls = new Dictionary<long, Control>();
        private readonly SharingInbox inbox = new SharingInbox();
        private SharingBudget budget = NewBudget();
        private int cursor;
        private float nextHello, nextSend, nextSnapshot, nextCreatureSnapshot, nextTargetSend;
        private string advertised;
        private bool advertisedMarkers, advertisedCreatures;
        private string session = Guid.NewGuid().ToString("N");
        private static readonly MarkerRecord[] EmptyDiscoveries = new MarkerRecord[0];
        private IReadOnlyList<MarkerRecord> discoveries = EmptyDiscoveries;
        private List<MarkerRecord> creatures = new List<MarkerRecord>();
        private readonly HashSet<string> currentCreatures = new HashSet<string>();
        private static SharingBudget NewBudget() => new SharingBudget(8192, SharingBudget.PacketBytes + SharingBudget.EnvelopeBytes);
        internal MarkerSharing(Plugin plugin) { host = plugin; }
        private bool Interested => host.Enabled.Value && (host.ShareMarkers.Value || host.ShareCreatures.Value);
        private string Announcement => "3:" + world + ":" + session + ":" + (host.ShareMarkers.Value ? "1" : "0") + ":" + (host.ShareCreatures.Value ? "1" : "0");

        internal void Tick()
        {
            if (!Interested) { if (active) Reset(); return; }
            if (Player.m_localPlayer == null || ZNet.instance == null || ZRoutedRpc.instance == null) { if (active) Reset(); return; }
            long current = ZNet.instance.GetWorldUID();
            if (rpc != ZRoutedRpc.instance)
            {
                Reset(); rpc = ZRoutedRpc.instance;
                rpc.Register<string>(Hello, ReceiveHello); rpc.Register<string>(Ack, ReceiveAck);
                rpc.Register<string>(Data, ReceiveData); rpc.Register<string>(Processed, ReceiveProcessed);
            }
            if (world == null || worldUid != current) { Reset(); worldUid = current; world = current.ToString(); }
            if (!host.Catalog.Ready || !host.Tracking.Ready) return;
            active = true;
            float now = Time.realtimeSinceStartup;
            if (advertised == null || advertisedMarkers != host.ShareMarkers.Value || advertisedCreatures != host.ShareCreatures.Value)
            {
                advertisedMarkers = host.ShareMarkers.Value; advertisedCreatures = host.ShareCreatures.Value;
                advertised = Announcement; nextHello = now;
            }
            ProcessIncoming(now);
            if (now >= nextSnapshot)
            {
                discoveries = host.ShareMarkers.Value ? host.Tracking.OwnDiscoveries() : EmptyDiscoveries;
                nextSnapshot = now + 2;
            }
            if (!host.ShareCreatures.Value) { creatures.Clear(); foreach (var peer in peers.Values) peer.Creatures.Reset(); }
            else if (now >= nextCreatureSnapshot)
            {
                creatures = host.Tracking.OwnCreatures(); nextCreatureSnapshot = now + 0.5f;
                currentCreatures.Clear(); foreach (var creature in creatures) currentCreatures.Add(creature.id);
                foreach (var peer in peers.Values) peer.Creatures.Prune(currentCreatures);
            }
            if (!host.ShareMarkers.Value) foreach (var peer in peers.Values) peer.Target = null;
            // Pause mod traffic rather than appending it behind native game synchronization.
            if (now < nextSend || Congested()) return;
            long controlPeer = 0; Control controlValue = null;
            foreach (var control in controls) { controlPeer = control.Key; controlValue = control.Value; break; }
            if (controlValue != null)
            {
                if (Send(controlPeer, controlValue.Method, controlValue.Value, now, null)) controls.Remove(controlPeer);
                return;
            }
            if (now >= nextHello)
            {
                if (Send(ZRoutedRpc.Everybody, Hello, Announcement, now, null)) nextHello = now + 30;
                return;
            }
            if (order.Count == 0) return;
            for (int i = 0, count = order.Count; i < count && order.Count > 0; i++)
            {
                if (cursor >= order.Count) cursor = 0;
                long id = order[cursor++];
                if (!peers.TryGetValue(id, out var peer)) { order.Remove(id); continue; }
                if (now - peer.Seen > 90) { RemovePeer(id); continue; }
                if (!peer.Budget.Allows(512, now) || !budget.Allows(512, now)) continue;
                if (peer.Pending != null)
                {
                    if (peer.Pending.kind == "creatures" && (!host.ShareCreatures.Value || !peer.AcceptsCreatures) ||
                        (peer.Pending.kind == "discoveries" || peer.Pending.kind == "target") && (!host.ShareMarkers.Value || !peer.AcceptsDiscoveries))
                    {
                        if (peer.Pending.kind == "creatures") peer.Creatures.Reset();
                        else if (peer.Pending.kind == "discoveries") peer.Static.Reset();
                        peer.Pending = null; peer.PendingJson = null; peer.PendingSent = false;
                    }
                    else
                    {
                        if (peer.PendingSent && now - peer.Attempt < 8) continue;
                        if (Send(id, Data, peer.PendingJson, now, peer)) { peer.Attempt = now; peer.PendingSent = true; }
                        return;
                    }
                }
                SharingPacket packet;
                if (peer.Target != null && peer.AcceptsTargets)
                    packet = new SharingPacket { world = world, session = session, sequence = peer.NextSequence, kind = "target", target = peer.Target };
                else
                {
                    bool sendStatic = host.ShareMarkers.Value && peer.AcceptsDiscoveries;
                    bool sendLive = host.ShareCreatures.Value && peer.AcceptsCreatures;
                    if (!sendStatic && !sendLive) continue;
                    bool live = sendLive && (!sendStatic || peer.NextCreatures);
                    peer.NextCreatures = !peer.NextCreatures;
                    var selected = (live ? peer.Creatures : peer.Static).Select(live ? creatures : discoveries, now, live);
                    if (selected.Count == 0) continue;
                    packet = new SharingPacket { world = world, session = session, sequence = peer.NextSequence,
                        markers = selected, kind = live ? "creatures" : "discoveries" };
                    if (live) packet.creatureLeaseSeconds = CreatureSharingLease.Seconds(creatures.Count, peers.Count);
                }
                string json = DataJson.WriteNetwork(packet);
                while (Encoding.UTF8.GetByteCount(json) > SharingBudget.PacketBytes && packet.markers?.Count > 1)
                { packet.markers.RemoveAt(packet.markers.Count - 1); json = DataJson.WriteNetwork(packet); }
                if (Encoding.UTF8.GetByteCount(json) > SharingBudget.PacketBytes) continue;
                // Retain selected records until bandwidth is available. Advancing the
                // selection cursor on an unsuccessful send could starve the same IDs.
                peer.Pending = packet; peer.PendingJson = json; peer.PendingSent = false;
                if (Send(id, Data, json, now, peer)) { peer.Attempt = now; peer.PendingSent = true; }
                peer.NextSequence++;
                if (packet.kind == "target") peer.Target = null;
                return;
            }
        }
        private static bool Congested()
        {
            foreach (var peer in ZNet.instance.GetPeers())
                if (peer.IsReady() && peer.m_socket != null && peer.m_socket.GetSendQueueSize() >= SharingBudget.SocketQueueBytes) return true;
            return false;
        }
        private bool Send(long destination, string method, string json, float now, SharingPeer peer)
        {
            int bytes = Encoding.UTF8.GetByteCount(json) + SharingBudget.EnvelopeBytes;
            if (!budget.Allows(bytes, now) || peer != null && !peer.Budget.Allows(bytes, now)) return false;
            rpc.InvokeRoutedRPC(destination, method, json);
            budget.Spend(bytes); peer?.Budget.Spend(bytes); nextSend = now + 0.1f;
            return true;
        }
        private bool SameWorld(long sender, string announcement)
        {
            if (!Interested || Player.m_localPlayer == null || sender == ZNet.GetUID() || world == null ||
                announcement == null || announcement.Length > 80) return false;
            var parts = announcement.Split(':');
            return parts.Length == 5 && parts[0] == "3" && parts[1] == world && Guid.TryParseExact(parts[2], "N", out _) &&
                (parts[3] == "0" || parts[3] == "1") && (parts[4] == "0" || parts[4] == "1");
        }
        private void Remember(long sender, string announcement)
        {
            string remoteSession = announcement.Split(':')[2];
            if (peers.TryGetValue(sender, out var previous) && previous.Session != remoteSession) RemovePeer(sender);
            if (!peers.TryGetValue(sender, out var peer))
            {
                if (peers.Count >= 32) return;
                peers.Add(sender, peer = new SharingPeer()); order.Add(sender);
                peer.Static.Current = host.Tracking.Contains;
                peer.Static.Retained = host.Tracking.ContainsDiscovery;
                peer.Static.Prepare = host.Tracking.PrepareSharedDiscovery;
            }
            peer.Session = remoteSession;
            bool receivesStatic = announcement[announcement.Length - 3] == '1';
            bool receivesCreatures = announcement[announcement.Length - 1] == '1';
            if (receivesStatic && !peer.AcceptsDiscoveries) peer.Static.Reset();
            if (receivesCreatures && !peer.AcceptsCreatures) peer.Creatures.Reset();
            peer.AcceptsDiscoveries = peer.AcceptsTargets = receivesStatic;
            peer.AcceptsCreatures = receivesCreatures;
            if (!receivesStatic) peer.Target = null;
            // Presence refreshes never invalidate acknowledged deltas.
            peer.Seen = Time.realtimeSinceStartup;
        }
        private void ReceiveHello(long sender, string announcement)
        {
            if (!SameWorld(sender, announcement)) return;
            Remember(sender, announcement);
            if (peers.ContainsKey(sender) && !controls.ContainsKey(sender)) controls[sender] = new Control { Method = Ack, Value = Announcement };
        }
        private void ReceiveAck(long sender, string announcement)
        { if (SameWorld(sender, announcement)) Remember(sender, announcement); }
        internal void PrioritizeDiscovery(MarkerRecord record)
        {
            if (!Interested || !host.ShareMarkers.Value || record == null || record.shared || record.dynamic || !host.Tracking.Contains(record)) return;
            foreach (var peer in peers.Values) if (peer.AcceptsDiscoveries) peer.Static.Prioritize(record);
        }
        internal void ForgetDiscovery(string id)
        { foreach (var peer in peers.Values) peer.Static.Forget(id); }
        internal void SendNavigationTarget(Vector3 position, string name, string type = null, string objectId = null)
        {
            float now = Time.realtimeSinceStartup;
            var target = new NavigationShare { world = world, name = NavigationShare.Caption(name), position = position, type = type, objectId = objectId };
            if (rpc == null || now < nextTargetSend || !NavigationShare.Allowed(target, world, host.Enabled.Value, host.ShareMarkers.Value)) return;
            foreach (var peer in peers.Values) if (now - peer.Seen <= 90 && peer.AcceptsTargets) peer.Target = target;
            host.Tracking.Discoveries.SelectSharedTarget(Minimap.instance, position, target.name, type, objectId);
            nextTargetSend = now + 0.5f;
        }
        private void ReceiveData(long sender, string json)
        {
            if (!Interested || world == null || !peers.TryGetValue(sender, out var peer) || Time.realtimeSinceStartup - peer.Seen > 90) return;
            inbox.Enqueue(sender, json);
        }
        private void ReceiveProcessed(long sender, string value)
        {
            if (!Interested || world == null || !peers.TryGetValue(sender, out var peer)) return;
            string prefix = world + ":" + session + ":" + peer.Session + ":";
            if (value == null || value.Length > 128 || !value.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(value.Substring(prefix.Length), out var sequence)) return;
            if (peer.Confirm(sequence, Time.realtimeSinceStartup)) peer.Seen = Time.realtimeSinceStartup;
        }
        private void ProcessIncoming(float now)
        {
            var item = inbox.Take();
            if (item == null || !peers.TryGetValue(item.Peer, out var peer)) return;
            try
            {
                var packet = DataJson.Read<SharingPacket>(item.Json);
                if (packet == null || packet.schema != 2 || packet.world != world || packet.session != peer.Session || packet.sequence <= 0) return;
                if (packet.sequence > peer.LastReceived)
                {
                    if (packet.kind == "target")
                    {
                        if (host.ShareMarkers.Value)
                        {
                            if (!NavigationShare.Allowed(packet.target, world, host.Enabled.Value, true)) return;
                            var target = packet.target;
                            host.Tracking.Discoveries.SelectSharedTarget(Minimap.instance, target.position, NavigationShare.Caption(target.name), target.type, target.objectId);
                        }
                    }
                    else
                    {
                        if (packet.markers == null || packet.markers.Count > 8 ||
                            (packet.kind != "discoveries" && packet.kind != "creatures")) return;
                        if (packet.kind == "creatures" && (packet.creatureLeaseSeconds < CreatureSharingLease.Minimum ||
                            packet.creatureLeaseSeconds > CreatureSharingLease.Maximum)) return;
                        if (packet.kind == "discoveries" && host.ShareMarkers.Value)
                            host.Tracking.ReceiveDiscoveries(item.Peer, packet.markers);
                        else if (packet.kind == "creatures" && host.ShareCreatures.Value)
                            host.Tracking.ReceiveCreatures(item.Peer, packet.markers, packet.creatureLeaseSeconds);
                    }
                    peer.LastReceived = packet.sequence;
                }
                peer.Seen = now; controls[item.Peer] = new Control { Method = Processed, Value = world + ":" + packet.session + ":" + session + ":" + packet.sequence };
            }
            catch (Exception) { /* Discard malformed input without flooding the game log. */ }
        }
        private void RemovePeer(long id) { peers.Remove(id); order.Remove(id); controls.Remove(id); }
        private void Reset()
        {
            world = null; active = false; peers.Clear(); order.Clear(); controls.Clear(); inbox.Clear();
            discoveries = EmptyDiscoveries; creatures.Clear(); cursor = 0; budget = NewBudget();
            currentCreatures.Clear();
            session = Guid.NewGuid().ToString("N"); advertised = null; nextHello = nextSnapshot = nextCreatureSnapshot = nextSend = nextTargetSend = 0;
        }
    }
}
