using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    // Immutable saved fields only. Reuse unchanged rows between saves; never send
    // a mutable MarkerRecord (or any Unity object) to the writer thread.
    internal sealed class SavedMarkerSnapshot
    {
        public string id, type;
        public Vector3 position;
        public bool collected, visited, completed;
        public long completionRevision, completionAuthor;
        public long pickedTime;
        public string portalTag;
        public bool imported, favorite, shared, observed;
        public long sourcePeer;
        public int creatureLevel, creatureRelation;
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public Vector3? dungeonOrigin;
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string[] dungeonAliases;

        internal static SavedMarkerSnapshot Capture(MarkerRecord r)
        {
            var cached = r.savedSnapshot;
            if (cached != null && cached.Matches(r)) return cached;
            return r.savedSnapshot = new SavedMarkerSnapshot {
                id = r.id, type = r.type, position = r.position,
                collected = r.collected, visited = r.visited, completed = r.completed,
                completionRevision = r.completionRevision, completionAuthor = r.completionAuthor,
                pickedTime = r.pickedTime, portalTag = r.portalTag, imported = r.imported,
                favorite = r.favorite, shared = r.shared, observed = r.observed,
                sourcePeer = r.sourcePeer, creatureLevel = r.creatureLevel,
                creatureRelation = r.creatureRelation, dungeonOrigin = r.dungeonOrigin,
                dungeonAliases = r.dungeonAliases == null ? null : (string[])r.dungeonAliases.Clone()
            };
        }
        private bool Matches(MarkerRecord r) =>
            id == r.id && type == r.type && position.Equals(r.position) &&
            collected == r.collected && visited == r.visited && completed == r.completed &&
            completionRevision == r.completionRevision && completionAuthor == r.completionAuthor &&
            pickedTime == r.pickedTime && portalTag == r.portalTag && imported == r.imported &&
            favorite == r.favorite && shared == r.shared && observed == r.observed &&
            sourcePeer == r.sourcePeer && creatureLevel == r.creatureLevel &&
            creatureRelation == r.creatureRelation && Nullable.Equals(dungeonOrigin, r.dungeonOrigin) &&
            SameAliases(dungeonAliases, r.dungeonAliases);
        private static bool SameAliases(string[] a, string[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    internal sealed class SavedMarkerFile
    {
        public int schema = 1;
        public string world, character;
        public IEnumerable<SavedMarkerSnapshot> markers;

        // The generic reflection serializer boxes every primitive saved field.
        // A large archive therefore allocates again on every worker write, even
        // when all immutable rows were reused. Typed writes keep the same JSON.
        internal void WriteTo(System.IO.TextWriter output)
        {
            using (var writer = new Newtonsoft.Json.JsonTextWriter(output) {
                Formatting = Newtonsoft.Json.Formatting.Indented, CloseOutput = false })
            {
                writer.WriteStartObject();
                writer.WritePropertyName("schema"); writer.WriteValue(schema);
                writer.WritePropertyName("world"); writer.WriteValue(world);
                writer.WritePropertyName("character"); writer.WriteValue(character);
                writer.WritePropertyName("markers"); writer.WriteStartArray();
                foreach (var r in markers)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("id"); writer.WriteValue(r.id);
                    writer.WritePropertyName("type"); writer.WriteValue(r.type);
                    writer.WritePropertyName("position"); WritePosition(writer, r.position);
                    writer.WritePropertyName("collected"); writer.WriteValue(r.collected);
                    writer.WritePropertyName("visited"); writer.WriteValue(r.visited);
                    writer.WritePropertyName("completed"); writer.WriteValue(r.completed);
                    writer.WritePropertyName("completionRevision"); writer.WriteValue(r.completionRevision);
                    writer.WritePropertyName("completionAuthor"); writer.WriteValue(r.completionAuthor);
                    writer.WritePropertyName("pickedTime"); writer.WriteValue(r.pickedTime);
                    writer.WritePropertyName("portalTag"); writer.WriteValue(r.portalTag);
                    writer.WritePropertyName("imported"); writer.WriteValue(r.imported);
                    writer.WritePropertyName("favorite"); writer.WriteValue(r.favorite);
                    writer.WritePropertyName("shared"); writer.WriteValue(r.shared);
                    writer.WritePropertyName("observed"); writer.WriteValue(r.observed);
                    writer.WritePropertyName("sourcePeer"); writer.WriteValue(r.sourcePeer);
                    writer.WritePropertyName("creatureLevel"); writer.WriteValue(r.creatureLevel);
                    writer.WritePropertyName("creatureRelation"); writer.WriteValue(r.creatureRelation);
                    if (r.dungeonOrigin.HasValue)
                    { writer.WritePropertyName("dungeonOrigin"); WritePosition(writer, r.dungeonOrigin.Value); }
                    if (r.dungeonAliases != null)
                    {
                        writer.WritePropertyName("dungeonAliases"); writer.WriteStartArray();
                        foreach (string alias in r.dungeonAliases) writer.WriteValue(alias);
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
        }
        private static void WritePosition(Newtonsoft.Json.JsonTextWriter writer, Vector3 point)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(point.x);
            writer.WritePropertyName("y"); writer.WriteValue(point.y);
            writer.WritePropertyName("z"); writer.WriteValue(point.z);
            writer.WriteEndObject();
        }
    }

    internal static class SavedMarkerPreparation
    {
        // Only the reference snapshot is copied at once. Row copying, cache
        // validation and allocation are bounded by the existing frame scheduler.
        internal static IEnumerable<byte> Prepare(string world, string character,
            IEnumerable<MarkerRecord> records, Action<SavedMarkerFile> completed)
        {
            var source = records.ToArray();
            var blocks = new List<SavedMarkerSnapshot[]>();
            SavedMarkerSnapshot[] block = null; int count = 0;
            foreach (var record in source)
            {
                if (!record.dynamic)
                {
                    if (block == null || count == block.Length)
                    { block = new SavedMarkerSnapshot[1024]; blocks.Add(block); count = 0; }
                    block[count++] = SavedMarkerSnapshot.Capture(record);
                }
                yield return 0;
            }
            completed(new SavedMarkerFile { world = world, character = character, markers = Rows(blocks) });
        }
        private static IEnumerable<SavedMarkerSnapshot> Rows(List<SavedMarkerSnapshot[]> blocks)
        {
            foreach (var block in blocks)
                foreach (var row in block)
                    if (row != null) yield return row;
        }
    }
}
