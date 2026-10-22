using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MapTracker
{
    // Export receives detached metadata only, never ConfigEntry, Sprite, prefab,
    // localization or other mutable Unity/runtime references.
    internal sealed class CatalogPersistence
    {
        private Task tail = Task.CompletedTask;
        private readonly ConcurrentQueue<Exception> errors = new ConcurrentQueue<Exception>();
        internal static CatalogDocument Snapshot(IEnumerable<CatalogEntry> entries, IEnumerable<string> warnings,
            string version, string gameVersion)
        {
            CatalogDocument result = null;
            foreach (var step in Prepare(entries, warnings, version, gameVersion, snapshot => result = snapshot)) { }
            return result;
        }
        internal static IEnumerable<byte> Prepare(IEnumerable<CatalogEntry> entries, IEnumerable<string> warnings,
            string version, string gameVersion, Action<CatalogDocument> completed)
        {
            var snapshot = new CatalogDocument { modVersion = version, gameVersion = gameVersion,
                generatedUtc = DateTime.UtcNow.ToString("o"), entries = new List<CatalogEntry>(),
                warnings = new List<string>(warnings) };
            return Capture(entries, snapshot, completed);
        }
        private static IEnumerable<byte> Capture(IEnumerable<CatalogEntry> entries, CatalogDocument snapshot,
            Action<CatalogDocument> completed)
        {
            foreach (var e in entries)
            {
                snapshot.entries.Add(new CatalogEntry {
                key = e.key, prefab = e.prefab, name = e.name, category = e.category, source = e.source,
                components = e.components == null ? null : (string[])e.components.Clone(),
                products = e.products == null ? null : (string[])e.products.Clone(),
                creature = e.creature, dungeon = e.dungeon, renewable = e.renewable,
                playerPiece = e.playerPiece, technical = e.technical, respawnMinutes = e.respawnMinutes
                });
                yield return 0;
            }
            completed(snapshot);
        }
        internal Task Enqueue(string path, CatalogDocument snapshot)
        {
            // Serialize exports in request order, including an export requested
            // during a world change. A failed cache export cannot poison the queue.
            return tail = tail.ContinueWith(_ => {
                try { Write(path, snapshot); }
                catch (Exception error) { errors.Enqueue(error); }
            }, TaskScheduler.Default);
        }
        internal Exception TakeError() => errors.TryDequeue(out var error) ? error : null;
        private static void Write(string path, CatalogDocument snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var output = new StreamWriter(temporary, false, new UTF8Encoding(false), 16384))
                DataJson.WriteTo(output, snapshot);
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }
}
