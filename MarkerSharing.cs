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
        private readonly Plugin host;
        private ZRoutedRpc rpc;
        private string world;
        private readonly Dictionary<long, float> peers = new Dictionary<long, float>();
        private List<MarkerRecord> outgoing;
        private int cursor;
        private float nextHello, nextSnapshot, nextBatch;
        private float nextCreatures;
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
                world = null;
            }
            if (world != current)
            {
                world = current; peers.Clear(); outgoing = null;
                nextHello = nextSnapshot = nextBatch = nextCreatures = 0;
            }
            float now = Time.realtimeSinceStartup;
            if (now >= nextHello)
            {
                // Only compatibility announcements are broadcast; marker data goes to confirmed mod users.
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, Hello, "1:" + world);
                nextHello = now + 15;
            }
            if (!host.Enabled.Value || !host.ShareMarkers.Value) { outgoing = null; return; }
            if (peers.Count == 0) return;
            if (now >= nextCreatures)
            {
                var creatures = host.Tracking.OwnCreatures();
                for (int start = 0; start < creatures.Count; start += 64)
                {
                    var creatureBatch = new MarkerFile { world = world };
                    for (int i = start; i < Math.Min(start + 64, creatures.Count); i++) creatureBatch.markers.Add(creatures[i]);
                    string json = DataJson.Write(creatureBatch);
                    foreach (var peer in peers)
                        if (now - peer.Value <= 60) rpc.InvokeRoutedRPC(peer.Key, Creatures, json);
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
                foreach (long peer in expired) peers.Remove(peer);
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
