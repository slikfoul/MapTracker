using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MapTracker
{
    internal sealed class PresetMessage
    {
        public int schema = 1, bytes, parts, part, features;
        public string world, id, kind, name, hash, data;
    }
    internal static class PresetCodec
    {
        internal const int MaxRaw = 524288, MaxPacked = 65536, Chunk = 1500, MaxTypes = 8192;
        private sealed class Snapshot { public string name; public Dictionary<string, int> types; }
        internal static bool ValidName(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 80 && !text.Any(char.IsControl);
        internal static string Digest(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static byte[] Encode(FilterPreset preset)
        {
            if (preset == null || !ValidName(preset.name) || preset.types == null || preset.types.Count > MaxTypes ||
                preset.types.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 256 || p.Value == null || p.Key.Any(char.IsControl)))
                throw new InvalidDataException("This preset cannot be shared.");
            var snapshot = new Snapshot { name = preset.name.Trim(), types = preset.types.ToDictionary(p => p.Key,
                p => (p.Value.selected ? 1 : 0) | (p.Value.keep ? 2 : 0) | (p.Value.notify.HasValue ? 4 : 0) | (p.Value.notify == true ? 8 : 0)) };
            byte[] raw = Encoding.UTF8.GetBytes(DataJson.Write(snapshot));
            if (raw.Length > MaxRaw) throw new InvalidDataException("Preset is too large to share.");
            using (var result = new MemoryStream())
            {
                using (var zip = new GZipStream(result, CompressionMode.Compress, true)) zip.Write(raw, 0, raw.Length);
                if (result.Length > MaxPacked) throw new InvalidDataException("Preset is too large to share.");
                return result.ToArray();
            }
        }
        internal static FilterPreset Decode(byte[] packed)
        {
            if (packed == null || packed.Length == 0 || packed.Length > MaxPacked) throw new InvalidDataException("Invalid preset size.");
            using (var input = new MemoryStream(packed))
            using (var zip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096]; int read;
                while ((read = zip.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (output.Length + read > MaxRaw) throw new InvalidDataException("Preset expands beyond its limit.");
                    output.Write(buffer, 0, read);
                }
                var doc = DataJson.Read<Snapshot>(new UTF8Encoding(false, true).GetString(output.ToArray()));
                if (doc == null || !ValidName(doc.name) || doc.types == null || doc.types.Count > MaxTypes ||
                    doc.types.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 256 || p.Key.Any(char.IsControl) || p.Value < 0 ||
                        p.Value > 15 || (p.Value & 8) != 0 && (p.Value & 4) == 0)) throw new InvalidDataException("Invalid preset contents.");
                return new FilterPreset { name = doc.name.Trim(), types = doc.types.ToDictionary(p => p.Key,
                    p => new FilterPresetType { selected = (p.Value & 1) != 0, keep = (p.Value & 2) != 0,
                        notify = (p.Value & 4) != 0 ? (bool?)((p.Value & 8) != 0) : null }) };
            }
        }
    }
    // No periodic discovery/handshake. All work belongs to one explicit, addressed offer.
    internal sealed class PresetTransfer
    {
        internal sealed class Progress
        {
            internal string Name, Player, Phase;
            internal int Bytes, Total;
            internal bool Active = true, Saved;
            internal float VisibleUntil = float.PositiveInfinity;
            internal int Percent => Saved ? 100 : Total <= 0 ? 0 : Math.Min(99, (int)((long)Bytes * 100 / Total));
            internal void Finish(string phase, float now, bool saved = false)
            { Active = false; Saved = saved; Phase = phase; VisibleUntil = now + (saved ? 5 : 8); }
        }
        internal sealed class Delivery { internal long Peer; internal PresetMessage Message; }
        internal sealed class Incoming
        {
            internal long Peer;
            internal string Sender;
            internal PresetMessage Offer;
            internal float Expires;
            internal bool Accepted;
            internal byte[][] Chunks;
            internal int Count;
        }
        private sealed class Outgoing
        {
            internal long Peer;
            internal byte[] Bytes;
            internal PresetMessage Offer;
            internal float Expires;
            internal int Next;
            internal bool Offered, Accepted;
            internal bool[] Sent;
        }
        internal readonly string World;
        internal Incoming Pending { get; private set; }
        internal Progress OutgoingProgress { get; private set; }
        internal Progress IncomingProgress { get; private set; }
        internal string Status = "", Notice;
        internal bool Busy => outgoing != null;
        internal bool HasOutput => controls.Count > 0 || outgoing != null && (!outgoing.Offered || outgoing.Accepted && outgoing.Next < outgoing.Offer.parts);
        private Outgoing outgoing;
        private readonly Queue<Delivery> controls = new Queue<Delivery>();
        private readonly Dictionary<long, float> lastOffer = new Dictionary<long, float>();
        private float nextPrune;
        private readonly Func<FilterPreset, string> save;
        internal PresetTransfer(string world, Func<FilterPreset, string> savePreset) { World = world; save = savePreset; }
        internal void Start(long peer, FilterPreset preset, float now, string playerName = "Player")
        {
            if (Busy || peer == 0) throw new InvalidOperationException("Wait for the current preset offer to finish.");
            byte[] bytes = PresetCodec.Encode(preset);
            outgoing = new Outgoing { Peer = peer, Bytes = bytes, Expires = now + 120,
                Offer = new PresetMessage { world = World, kind = "offer", id = Guid.NewGuid().ToString("N"), name = preset.name.Trim(),
                    bytes = bytes.Length, parts = (bytes.Length + PresetCodec.Chunk - 1) / PresetCodec.Chunk, hash = PresetCodec.Digest(bytes) } };
            outgoing.Sent = new bool[outgoing.Offer.parts];
            OutgoingProgress = new Progress { Name = preset.name.Trim(), Player = playerName, Total = bytes.Length, Phase = "Waiting for acceptance" };
            Status = "Waiting for the player to accept…";
        }
        private void Reply(long peer, string id, string kind)
        {
            if (controls.Count < 8) controls.Enqueue(new Delivery { Peer = peer,
                Message = new PresetMessage { world = World, id = id, kind = kind } });
        }
        internal void Decide(bool accept, float now)
        {
            if (Pending == null || Pending.Accepted) return;
            if (accept)
            {
                Pending.Accepted = true; Pending.Chunks = new byte[Pending.Offer.parts][]; Pending.Expires = now + 120;
                IncomingProgress = new Progress { Name = Pending.Offer.name, Player = Pending.Sender, Total = Pending.Offer.bytes, Phase = "Receiving preset" };
                Reply(Pending.Peer, Pending.Offer.id, "accept");
            }
            else { Reply(Pending.Peer, Pending.Offer.id, "decline"); Pending = null; }
        }
        internal void Receive(long peer, string senderName, PresetMessage message, float now, bool blocked)
        {
            if (message == null || message.schema != 1 || message.world != World || peer == 0 ||
                !Guid.TryParseExact(message.id, "N", out _)) return;
            if (message.kind == "offer")
            {
                if (blocked || Pending != null || !PresetCodec.ValidName(message.name) || message.bytes <= 0 || message.bytes > PresetCodec.MaxPacked ||
                    message.parts != (message.bytes + PresetCodec.Chunk - 1) / PresetCodec.Chunk || message.hash == null ||
                    message.hash.Length != 64 || message.hash.Any(c => !Uri.IsHexDigit(c))) return;
                if (lastOffer.TryGetValue(peer, out float previous) && now - previous < 30) return;
                if (lastOffer.Count >= 64 && !lastOffer.ContainsKey(peer)) return;
                lastOffer[peer] = now;
                Pending = new Incoming { Peer = peer, Sender = senderName, Offer = message, Expires = now + 120 }; Notice = null;
            }
            else if (outgoing != null && peer == outgoing.Peer && message.id == outgoing.Offer.id)
            {
                if (message.kind == "accept" && !outgoing.Accepted)
                { outgoing.Accepted = true; outgoing.Expires = now + 120; Status = "Sending preset…"; OutgoingProgress.Phase = "Sending preset"; }
                else if (message.kind == "decline" || message.kind == "saved" || message.kind == "failed")
                {
                    Status = message.kind == "saved" ? "Preset added to the player's list." : message.kind == "decline" ? "Preset offer declined." : "Preset could not be saved by the player.";
                    OutgoingProgress.Finish(message.kind == "saved" ? "Preset saved by player" : message.kind == "decline" ? "Offer declined" : "Could not save preset", now, message.kind == "saved");
                    outgoing = null;
                }
            }
            // The same pair of players may share in both directions concurrently.
            if (message.kind == "chunk" && !blocked && Pending != null && Pending.Accepted && Pending.Peer == peer && Pending.Offer.id == message.id)
            {
                if (message.part < 0 || message.part >= Pending.Chunks.Length || message.data == null || message.data.Length > 2000 ||
                    Pending.Chunks[message.part] != null) return;
                try
                {
                    byte[] chunk = Convert.FromBase64String(message.data);
                    int expected = Math.Min(PresetCodec.Chunk, Pending.Offer.bytes - message.part * PresetCodec.Chunk);
                    if (chunk.Length != expected) return;
                    Pending.Chunks[message.part] = chunk; Pending.Count++;
                    IncomingProgress.Bytes += chunk.Length;
                    if (Pending.Count != Pending.Chunks.Length) return;
                    var bytes = new byte[Pending.Offer.bytes];
                    for (int i = 0; i < Pending.Chunks.Length; i++) Array.Copy(Pending.Chunks[i], 0, bytes, i * PresetCodec.Chunk, Pending.Chunks[i].Length);
                    if (!string.Equals(PresetCodec.Digest(bytes), Pending.Offer.hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Preset checksum mismatch.");
                    var preset = PresetCodec.Decode(bytes);
                    if (preset.name != Pending.Offer.name) throw new InvalidDataException("Preset name changed.");
                    string name = save(preset);
                    IncomingProgress.Finish("Preset added to your list", now, true);
                    Reply(peer, message.id, "saved"); Notice = "Preset “" + name + "” added. Apply it from Presets when ready."; Pending = null;
                }
                catch (Exception error)
                {
                    if (!(error is InvalidDataException || error is IOException || error is ArgumentException || error is FormatException || error is Newtonsoft.Json.JsonException || error is UnauthorizedAccessException)) throw;
                    Reply(peer, message.id, "failed"); Pending = null; Notice = "Could not save the shared preset. Existing presets were kept.";
                    IncomingProgress.Finish("Could not save preset", now);
                }
            }
        }
        internal void Tick(float now, bool blocked, Func<long, bool> online)
        {
            if (Pending != null && (blocked || now >= Pending.Expires || !online(Pending.Peer)))
            {
                if (Pending.Accepted)
                {
                    if (blocked) IncomingProgress = null;
                    else IncomingProgress.Finish("Transfer interrupted or timed out", now);
                }
                long peer = Pending.Peer;
                Pending = null;
                // A changed block preference silently cancels pending acceptance as well.
                var keep = controls.Where(c => c.Peer != peer || c.Message.kind != "accept").ToArray();
                controls.Clear(); foreach (var item in keep) controls.Enqueue(item);
            }
            if (outgoing != null && (now >= outgoing.Expires || !online(outgoing.Peer)))
            {
                outgoing = null; Status = "Preset offer expired or the player disconnected.";
                OutgoingProgress.Finish("Player disconnected or offer expired", now);
            }
            if (OutgoingProgress != null && now >= OutgoingProgress.VisibleUntil) OutgoingProgress = null;
            if (IncomingProgress != null && now >= IncomingProgress.VisibleUntil) IncomingProgress = null;
            if (lastOffer.Count > 0 && now >= nextPrune)
            {
                foreach (var key in lastOffer.Where(p => now - p.Value >= 120).Select(p => p.Key).ToArray()) lastOffer.Remove(key);
                nextPrune = now + 5;
            }
        }
        internal bool CanSend(Delivery item) => item.Message.kind == "offer" || item.Message.kind == "chunk" ?
            outgoing != null && outgoing.Peer == item.Peer && outgoing.Offer.id == item.Message.id :
            item.Message.kind != "accept" || Pending != null && Pending.Accepted && Pending.Peer == item.Peer && Pending.Offer.id == item.Message.id;
        internal void Sent(Delivery item)
        {
            // Taking a queued chunk is not delivery: the socket/bandwidth gate may
            // still postpone it. Advance only after InvokeRoutedRPC succeeds.
            if (item?.Message.kind != "chunk" || outgoing == null || item.Peer != outgoing.Peer || item.Message.id != outgoing.Offer.id ||
                item.Message.part < 0 || item.Message.part >= outgoing.Sent.Length || outgoing.Sent[item.Message.part]) return;
            outgoing.Sent[item.Message.part] = true;
            OutgoingProgress.Bytes += Math.Min(PresetCodec.Chunk, outgoing.Bytes.Length - item.Message.part * PresetCodec.Chunk);
            if (OutgoingProgress.Bytes == OutgoingProgress.Total) OutgoingProgress.Phase = "Waiting for save confirmation";
        }
        internal void SendingPaused(bool paused)
        {
            if (outgoing == null || !outgoing.Accepted || OutgoingProgress.Bytes == OutgoingProgress.Total) return;
            OutgoingProgress.Phase = paused ? "Waiting for connection" : "Sending preset";
        }
        internal Delivery Take()
        {
            if (controls.Count > 0) return controls.Dequeue();
            if (outgoing == null) return null;
            if (!outgoing.Offered) { outgoing.Offered = true; return new Delivery { Peer = outgoing.Peer, Message = outgoing.Offer }; }
            if (!outgoing.Accepted || outgoing.Next >= outgoing.Offer.parts) return null;
            int part = outgoing.Next++, length = Math.Min(PresetCodec.Chunk, outgoing.Bytes.Length - part * PresetCodec.Chunk);
            return new Delivery { Peer = outgoing.Peer, Message = new PresetMessage { world = World, id = outgoing.Offer.id,
                kind = "chunk", part = part, data = Convert.ToBase64String(outgoing.Bytes, part * PresetCodec.Chunk, length) } };
        }
    }
}
