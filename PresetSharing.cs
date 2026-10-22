using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace MapTracker
{
    internal sealed class PresetSharing
    {
        internal enum Availability { Checking, Ready, Unavailable, NotAccepting }
        internal sealed class Recipient
        {
            internal long Id;
            internal string Name;
            internal Availability State;
            internal bool CanShare => State == Availability.Ready;
            internal string Hint => State == Availability.Ready ? "Share this preset with the player." :
                State == Availability.Checking ? "Checking whether MapTracker is installed…" :
                State == Availability.NotAccepting ? "This player has blocked incoming preset offers." :
                "MapTracker is not installed, is disabled, or does not support preset sharing.";
        }
        private sealed class Capability
        {
            internal string Id;
            internal float NextCheck, SentAt = -1;
            internal bool Replied, PreviouslyConfirmed;
            internal int Features;
        }
        private const float ProbeTimeout = 5, ProbeInterval = 30;
        private const string Method = "MapTracker.Presets.v1";
        private readonly Plugin host;
        private ZRoutedRpc rpc;
        private PresetTransfer transfer;
        private readonly PresetInbox inbox = new PresetInbox();
        private readonly Dictionary<long, Capability> capabilities = new Dictionary<long, Capability>();
        private readonly Dictionary<long, float> probeReplies = new Dictionary<long, float>();
        private readonly List<long> disconnected = new List<long>();
        private readonly Queue<PresetTransfer.Delivery> capabilityOutbox = new Queue<PresetTransfer.Delivery>();
        private readonly Queue<PresetTransfer.Delivery> capabilityReplies = new Queue<PresetTransfer.Delivery>();
        private readonly FilterPresetStore store = new FilterPresetStore(Path.Combine(Paths.ConfigPath, "MapTracker", "presets.json"));
        private SharingBudget budget = new SharingBudget(2048, SharingBudget.PacketBytes + SharingBudget.EnvelopeBytes);
        private PresetTransfer.Delivery waiting;
        private string waitingJson;
        private float nextSend, nextPresenceCheck;
        internal PresetTransfer Transfer => transfer;
        internal PresetSharing(Plugin plugin) { host = plugin; }
        internal static List<Recipient> Players()
        {
            var result = new List<Recipient>();
            if (ZNet.instance == null || Player.m_localPlayer == null) return result;
            foreach (var player in ZNet.instance.GetPlayerList())
            {
                // Character ZDOs are created by their local client's session. Unlike
                // names/positions, this routes directly even when public position is off.
                long id = player.m_characterID.UserID;
                if (player.m_characterID.IsNone() || id == 0 || id == ZNet.GetUID() || result.Exists(p => p.Id == id)) continue;
                result.Add(new Recipient { Id = id, Name = Caption(player.m_name) });
            }
            result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            return result;
        }
        internal List<Recipient> Recipients()
        {
            var players = Players();
            foreach (var player in players) player.State = CapabilityState(player.Id);
            return players;
        }
        private Availability CapabilityState(long peer)
        {
            if (!capabilities.TryGetValue(peer, out var state)) return Availability.Checking;
            if (state.Replied) return (state.Features & 2) != 0 ? Availability.Ready : Availability.NotAccepting;
            if (state.SentAt >= 0 && Time.realtimeSinceStartup - state.SentAt >= ProbeTimeout) return Availability.Unavailable;
            // Revalidating an already confirmed peer is not evidence that its
            // mod disappeared. Preserve the last answer until reply or timeout.
            return state.PreviouslyConfirmed ? (state.Features & 2) != 0 ? Availability.Ready : Availability.NotAccepting : Availability.Checking;
        }
        // Called by the visible recipient chooser only. No background announcements
        // and no probing merely because discovery/creature sharing is enabled.
        internal void CheckRecipients(bool refreshKnown = false)
        {
            if (transfer == null || !host.Enabled.Value) return;
            float now = Time.realtimeSinceStartup;
            var players = Players();
            foreach (long peer in new List<long>(capabilities.Keys))
                if (!players.Exists(p => p.Id == peer)) capabilities.Remove(peer);
            foreach (long peer in new List<long>(probeReplies.Keys))
                if (Name(peer) == null || now - probeReplies[peer] >= 60) probeReplies.Remove(peer);
            foreach (var player in players)
            {
                if (capabilities.TryGetValue(player.Id, out var previous) && (!refreshKnown || now < previous.NextCheck)) continue;
                if (capabilities.Count >= 64 && previous == null || capabilityOutbox.Count >= 64) break;
                var state = new Capability { Id = Guid.NewGuid().ToString("N"), NextCheck = float.PositiveInfinity,
                    PreviouslyConfirmed = previous?.Replied == true, Features = previous?.Features ?? 0 };
                capabilities[player.Id] = state;
                capabilityOutbox.Enqueue(new PresetTransfer.Delivery { Peer = player.Id,
                    Message = new PresetMessage { world = transfer.World, kind = "probe", id = state.Id } });
            }
        }
        private bool ReceiveCapability(long peer, PresetMessage message, float now)
        {
            if (message?.kind != "probe" && message?.kind != "capability") return false;
            if (message.schema != 1 || message.world != transfer.World || !Guid.TryParseExact(message.id, "N", out _)) return true;
            if (message.kind == "probe")
            {
                if (probeReplies.Count >= 64)
                    foreach (long id in new List<long>(probeReplies.Keys))
                        if (now - probeReplies[id] >= 60) probeReplies.Remove(id);
                if (probeReplies.TryGetValue(peer, out float last) && now - last < ProbeInterval || capabilityReplies.Count >= 64 ||
                    probeReplies.Count >= 64 && !probeReplies.ContainsKey(peer)) return true;
                probeReplies[peer] = now;
                capabilityReplies.Enqueue(new PresetTransfer.Delivery { Peer = peer, Message = new PresetMessage {
                    world = transfer.World, kind = "capability", id = message.id, features = host.BlockPresetOffers.Value ? 1 : 3 } });
            }
            else if (capabilities.TryGetValue(peer, out var state) && state.Id == message.id && state.SentAt >= 0 &&
                now - state.SentAt < ProbeTimeout && (message.features == 1 || message.features == 3))
            {
                state.Replied = true; state.Features = message.features;
                // Count revalidation spacing from the received answer, not the
                // queued request. This also respects older peers' reply cooldown
                // when request/response delivery times differ between checks.
                state.NextCheck = now + ProbeInterval;
            }
            return true;
        }
        private bool CapabilityCanSend(PresetTransfer.Delivery item)
        {
            if (item.Message.kind != "probe") return item.Message.kind == "capability";
            return capabilities.TryGetValue(item.Peer, out var state) && state.Id == item.Message.id && state.SentAt < 0;
        }
        private static string Caption(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Player";
            var value = new StringBuilder();
            foreach (char c in text) { if (!char.IsControl(c)) value.Append(c); if (value.Length == 80) break; }
            return value.ToString();
        }
        private static string Name(long id)
        {
            if (ZNet.instance == null || id == 0 || id == ZNet.GetUID()) return null;
            foreach (var player in ZNet.instance.GetPlayerList())
                if (!player.m_characterID.IsNone() && player.m_characterID.UserID == id) return Caption(player.m_name);
            return null;
        }
        private static bool Online(long id)
        {
            if (ZNet.instance == null || id == 0 || id == ZNet.GetUID()) return false;
            foreach (var player in ZNet.instance.GetPlayerList())
                if (!player.m_characterID.IsNone() && player.m_characterID.UserID == id) return true;
            return false;
        }
        private void CheckPresence(float now)
        {
            if (now < nextPresenceCheck) return;
            nextPresenceCheck = now + 0.5f;
            // Do not retain a positive answer across an observed disconnect,
            // even when the picker was closed at the time.
            foreach (var peer in capabilities) if (!Online(peer.Key)) disconnected.Add(peer.Key);
            foreach (long peer in disconnected) capabilities.Remove(peer);
            disconnected.Clear();
            foreach (var peer in probeReplies) if (!Online(peer.Key)) disconnected.Add(peer.Key);
            foreach (long peer in disconnected) probeReplies.Remove(peer);
            disconnected.Clear();
        }
        internal bool Send(long peer, FilterPreset preset)
        {
            if (transfer == null || !host.Enabled.Value || Name(peer) == null || CapabilityState(peer) != Availability.Ready) return false;
            transfer.Start(peer, preset, Time.realtimeSinceStartup, Name(peer)); return true;
        }
        private void Receive(long sender, string json)
        {
            if (transfer == null || !host.Enabled.Value || Name(sender) == null) return;
            // Do no parsing, decompression, disk writes or replies inside the RPC.
            inbox.Enqueue(sender, json);
        }
        internal void Decide(bool accept)
        {
            if (host.BlockPresetOffers.Value && accept) return;
            transfer?.Decide(accept, Time.realtimeSinceStartup);
        }
        internal void Tick()
        {
            if (!host.Enabled.Value || Player.m_localPlayer == null || ZNet.instance == null || ZRoutedRpc.instance == null)
            { if (transfer != null) Reset(); return; }
            string world = ZNet.instance.GetWorldUID().ToString();
            if (rpc != ZRoutedRpc.instance || transfer == null || transfer.World != world)
            {
                Reset();
                if (rpc != ZRoutedRpc.instance) { rpc = ZRoutedRpc.instance; rpc.Register<string>(Method, Receive); }
                transfer = new PresetTransfer(world, preset => { string name = store.Import(preset); host.Window.Refresh(); return name; });
            }
            float now = Time.realtimeSinceStartup;
            CheckPresence(now);
            transfer.Tick(now, host.BlockPresetOffers.Value, Online);
            var item = inbox.Take();
            if (item != null)
            {
                string name = Name(item.Peer);
                if (name != null)
                    try
                    {
                        var message = DataJson.Read<PresetMessage>(item.Json);
                        if (!ReceiveCapability(item.Peer, message, now)) transfer.Receive(item.Peer, name, message, now, host.BlockPresetOffers.Value);
                    }
                    catch (Newtonsoft.Json.JsonException) { } // Malformed input never alters presets or opens a prompt.
            }
            if (waiting == null && !transfer.HasOutput && capabilityOutbox.Count == 0 && capabilityReplies.Count == 0) return;
            bool congested = Congested(); transfer.SendingPaused(congested);
            if (now < nextSend || congested) return;
            if (waiting == null)
            {
                waiting = capabilityReplies.Count > 0 ? capabilityReplies.Dequeue() : transfer.Take();
                if (waiting == null && capabilityOutbox.Count > 0) waiting = capabilityOutbox.Dequeue();
                waitingJson = waiting == null ? null : DataJson.Write(waiting.Message);
            }
            if (waiting == null) return;
            bool capability = waiting.Message.kind == "probe" || waiting.Message.kind == "capability";
            if (Name(waiting.Peer) == null || (capability ? !CapabilityCanSend(waiting) : !transfer.CanSend(waiting)))
            { waiting = null; waitingJson = null; return; }
            int size = Encoding.UTF8.GetByteCount(waitingJson) + SharingBudget.EnvelopeBytes;
            if (size > SharingBudget.PacketBytes) { waiting = null; waitingJson = null; return; }
            if (!budget.Allows(size, now)) return;
            rpc.InvokeRoutedRPC(waiting.Peer, Method, waitingJson);
            if (waiting.Message.kind == "probe")
            { var state = capabilities[waiting.Peer]; state.SentAt = now; state.NextCheck = now + ProbeInterval; }
            transfer.Sent(waiting);
            budget.Spend(size); nextSend = now + 0.25f; waiting = null; waitingJson = null;
        }
        private static bool Congested()
        {
            foreach (var peer in ZNet.instance.GetPeers())
                if (peer.IsReady() && peer.m_socket != null && peer.m_socket.GetSendQueueSize() >= SharingBudget.SocketQueueBytes) return true;
            return false;
        }
        internal void Reset()
        {
            transfer = null; inbox.Clear(); waiting = null; waitingJson = null; nextSend = nextPresenceCheck = 0;
            disconnected.Clear();
            capabilities.Clear(); probeReplies.Clear(); capabilityOutbox.Clear(); capabilityReplies.Clear();
            budget = new SharingBudget(2048, SharingBudget.PacketBytes + SharingBudget.EnvelopeBytes);
        }
    }
    internal sealed class PresetInbox
    {
        internal sealed class Item { internal long Peer; internal string Json; internal int Bytes; }
        private readonly Queue<Item> messages = new Queue<Item>();
        private int bytes;
        internal bool Enqueue(long peer, string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > SharingBudget.PacketBytes || messages.Count >= 64) return false;
            int size = Encoding.UTF8.GetByteCount(json);
            if (size > SharingBudget.PacketBytes || bytes + size > 131072) return false;
            // Reliable routed messages may arrive together after a long game frame.
            // Unlike discovery deltas, preset chunks are not periodically repeated.
            messages.Enqueue(new Item { Peer = peer, Json = json, Bytes = size }); bytes += size; return true;
        }
        internal Item Take()
        {
            if (messages.Count == 0) return null;
            var result = messages.Dequeue(); bytes -= result.Bytes; return result;
        }
        internal void Clear() { messages.Clear(); bytes = 0; }
    }
}
