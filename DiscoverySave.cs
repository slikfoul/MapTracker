using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MapTracker
{
    internal static class DiscoverySave
    {
        // Copy plain saved fields on the game thread. The worker never touches
        // Character, GameObject, catalog settings or the mutable marker dictionary.
        internal static MarkerFile Snapshot(string world, string character, IEnumerable<MarkerRecord> records)
        {
            var file = new MarkerFile { world = world, character = character };
            foreach (var r in records)
                if (!r.dynamic) file.markers.Add(new MarkerRecord {
                    id = r.id, type = r.type, position = r.position, collected = r.collected,
                    visited = r.visited, completed = r.completed, pickedTime = r.pickedTime,
                    completionRevision = r.completionRevision, completionAuthor = r.completionAuthor,
                    portalTag = r.portalTag, imported = r.imported, favorite = r.favorite,
                    shared = r.shared, observed = r.observed, sourcePeer = r.sourcePeer,
                    creatureLevel = r.creatureLevel, creatureRelation = r.creatureRelation,
                    dungeonOrigin = r.dungeonOrigin, dungeonAliases = r.dungeonAliases == null ? null : (string[])r.dungeonAliases.Clone()
                });
            return file;
        }
        internal static Task WriteAsync(string path, MarkerFile file) => Task.Run(() => Write(path, file));
        internal static Task WritePreparedAsync(string path, SavedMarkerFile file) => Task.Run(() => Write(path, file));
        private static void Write(string path, object file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            // Stream instead of allocating a whole database-sized UTF-16 string:
            // even a worker allocation can trigger a game-wide GC pause.
            using (var output = new StreamWriter(temp, false, new UTF8Encoding(false), 16384))
            {
                if (file is SavedMarkerFile prepared) prepared.WriteTo(output);
                else DataJson.WriteTo(output, file);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        }
    }
}
