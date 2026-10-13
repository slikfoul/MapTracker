using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTracker
{
    internal sealed class MarkerSharing
    {
        private const string Hello = "MapTracker.Discoveries.Hello.v1";
        private const string Ack = "MapTracker.Discoveries.Ack.v1";
        private const string Data = "MapTracker.Discoveries.Data.v1";
        private const string Creatures = "MapTracker.Discoveries.Creatures.v1";
        private const string TargetHello = "MapTracker.Navigation.Hello.v1";
        private const string TargetAck = "MapTracker.Navigation.Ack.v1";
        private const string TargetData = "MapTracker.Navigation.Target.v1";
        private readonly Plugin host;
        private ZRoutedRpc rpc;
        private string world;
        private readonly Dictionary<long, float> peers = new Dictionary<long, float>();
        private readonly Dictionary<long, float> targetPeers = new Dictionary<long, float>();
        private readonly Dictionary<long, float> lastTargetReceived = new Dictionary<long, float>();
        private float nextTargetSend;
        private List<MarkerRecord> outgoing;
        private int cursor;
        private float nextHello, nextSnapshot, nextBatch;
        private float nextCreatures;
        private readonly CreatureNetworkDelta creatureDelta = new CreatureNetworkDelta();
        internal MarkerSharing(Plugin plugin) { host = plugin; }

        internal void Tick()
        {
            if (Player.m_localPlayer == null || ZNet.instance == null || ZRoutedRpc.instance == null || !host.Catalog.Ready) return;
            string current = ZNet.instance.GetWorldUID().ToString();
            if (rpc != ZRoutedRpc.instance)
            {
                rpc = ZRoutedRpc.instance;
                rpc.Register<string>(Hello, ReceiveHello);
                rpc.Register<string>(Ack, ReceiveAck);
                rpc.Register<string>(Data, ReceiveData);
                rpc.Register<string>(Creatures, ReceiveCreatures);
                rpc.Register<string>(TargetHello, ReceiveTargetHello);
                rpc.Register<string>(TargetAck, ReceiveTargetAck);
                rpc.Register<string>(TargetData, ReceiveTarget);
                world = null;
            }
            if (world != current)
            {
                world = current; peers.Clear(); targetPeers.Clear(); lastTargetReceived.Clear(); creatureDelta.Reset(); nextTargetSend = 0; outgoing = null;
                nextHello = nextSnapshot = nextBatch = nextCreatures = 0;
            }
            float now = Time.realtimeSinceStartup;
            if (now >= nextHello)
            {
                // Only compatibility announcements are broadcast; marker data goes to confirmed mod users.
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, Hello, "1:" + world);
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, TargetHello, "1:" + world);
                nextHello = now + 15;
            }
            if (!host.Enabled.Value || !host.ShareMarkers.Value) { outgoing = null; creatureDelta.Reset(); return; }
            if (peers.Count == 0) return;
            if (!host.ShareCreatures.Value) creatureDelta.Reset();
            if (host.ShareCreatures.Value && now >= nextCreatures)
            {
                var creatures = host.Tracking.OwnCreatures();
                foreach (var peer in peers)
                {
                    if (now - peer.Value > 60) continue;
                    var changed = creatureDelta.Select(peer.Key, creatures, now);
                    for (int start = 0; start < changed.Count; start += 64)
                    {
                        var creatureBatch = new MarkerFile { world = world };
                        for (int i = start; i < Math.Min(start + 64, changed.Count); i++) creatureBatch.markers.Add(changed[i]);
                        rpc.InvokeRoutedRPC(peer.Key, Creatures, DataJson.Write(creatureBatch));
                    }
                }
                nextCreatures = now + 0.5f;
            }
            if (outgoing == null && now >= nextSnapshot)
            {
                outgoing = host.Tracking.OwnDiscoveries(); cursor = 0; nextSnapshot = now + 30;
            }
            if (outgoing == null || now < nextBatch) return;
            var batch = new MarkerFile { world = world, character = Player.m_localPlayer.GetPlayerID().ToString() };
            int end = Math.Min(cursor + 64, outgoing.Count);
            for (; cursor < end; cursor++) batch.markers.Add(outgoing[cursor]);
            if (batch.markers.Count > 0)
            {
                string json = DataJson.Write(batch);
                if (json.IndexOf("\"markers\"", StringComparison.Ordinal) < 0) throw new InvalidOperationException("Sharing serializer omitted markers.");
                var expired = new List<long>();
                foreach (var peer in peers)
                {
                    if (now - peer.Value > 60) expired.Add(peer.Key);
                    else rpc.InvokeRoutedRPC(peer.Key, Data, json);
                }
                foreach (long peer in expired) { peers.Remove(peer); creatureDelta.RemovePeer(peer); }
            }
            nextBatch = now + 0.25f;
            if (cursor >= outgoing.Count) outgoing = null;
        }
        private bool SameWorld(long sender, string announcement)
        {
            return Player.m_localPlayer != null && sender != ZNet.GetUID() && world != null && announcement == "1:" + world;
        }
        private void ReceiveHello(long sender, string announcement)
        {
            if (!SameWorld(sender, announcement)) return;
            peers[sender] = Time.realtimeSinceStartup;
            rpc.InvokeRoutedRPC(sender, Ack, announcement);
            nextSnapshot = 0;
        }
        private void ReceiveAck(long sender, string announcement)
        {
            if (!SameWorld(sender, announcement)) return;
            peers[sender] = Time.realtimeSinceStartup; nextSnapshot = 0;
        }
        private void ReceiveTargetHello(long sender, string announcement)
        {
            if (!SameWorld(sender, announcement)) return;
            targetPeers[sender] = Time.realtimeSinceStartup;
            rpc.InvokeRoutedRPC(sender, TargetAck, announcement);
        }
        private void ReceiveTargetAck(long sender, string announcement)
        {
            if (SameWorld(sender, announcement)) targetPeers[sender] = Time.realtimeSinceStartup;
        }
        internal void SendNavigationTarget(Vector3 position, string name, string type = null, string objectId = null)
        {
            var target = new NavigationShare { world = world, name = NavigationShare.Caption(name), position = position, type = type, objectId = objectId };
            float now = Time.realtimeSinceStartup;
            if (rpc == null || now < nextTargetSend || !NavigationShare.Allowed(target, world, host.Enabled.Value, host.ShareMarkers.Value)) return;
            string json = DataJson.Write(target);
            foreach (var peer in targetPeers)
                if (now - peer.Value <= 60) rpc.InvokeRoutedRPC(peer.Key, TargetData, json);
            host.Tracking.Discoveries.SelectSharedTarget(Minimap.instance, target.position, target.name, target.type, target.objectId);
            nextTargetSend = now + 0.5f;
        }
        private void ReceiveTarget(long sender, string json)
        {
            float now = Time.realtimeSinceStartup;
            if (!host.Enabled.Value || !host.ShareMarkers.Value || world == null || !targetPeers.TryGetValue(sender, out var seen) ||
                now - seen > 60 || string.IsNullOrEmpty(json) || json.Length > 4096 ||
                (lastTargetReceived.TryGetValue(sender, out var previous) && now - previous < 0.5f)) return;
            try
            {
                var target = DataJson.Read<NavigationShare>(json);
                if (!NavigationShare.Allowed(target, world, host.Enabled.Value, host.ShareMarkers.Value)) return;
                lastTargetReceived[sender] = now;
                host.Tracking.Discoveries.SelectSharedTarget(Minimap.instance, target.position, NavigationShare.Caption(target.name), target.type, target.objectId);
            }
            catch (Exception e) { host.Report("Shared navigation target ignored", e); }
        }
        private void ReceiveData(long sender, string json)
        {
            if (!host.Enabled.Value || world == null || !peers.ContainsKey(sender) || string.IsNullOrEmpty(json) || json.Length > 262144) return;
            try
            {
                var file = DataJson.Read<MarkerFile>(json);
                if (file == null || file.schema != 1 || file.world != world || file.markers == null || file.markers.Count > 64) return;
                host.Tracking.ReceiveDiscoveries(sender, file.markers);
            }
            catch (Exception e) { host.Report("Shared discovery batch ignored", e); }
        }
        private void ReceiveCreatures(long sender, string json)
        {
            if (!host.Enabled.Value || world == null || !peers.ContainsKey(sender) || string.IsNullOrEmpty(json) || json.Length > 262144) return;
            try
            {
                var file = DataJson.Read<MarkerFile>(json);
                if (file == null || file.schema != 1 || file.world != world || file.markers == null || file.markers.Count > 64) return;
                host.Tracking.ReceiveCreatures(sender, file.markers);
            }
            catch (Exception e) { host.Report("Shared creature batch ignored", e); }
        }
    }
}
