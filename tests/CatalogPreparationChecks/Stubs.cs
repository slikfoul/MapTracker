using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MapTracker
{
    // Plain catalog schema only. Native registry operations remain in Catalog
    // and are not claimed to be exercised by these managed scheduling checks.
    public sealed class CatalogEntry
    {
        public string key, prefab, name, category, source;
        public string[] components, products;
        public bool creature, dungeon, renewable, playerPiece, technical;
        public float respawnMinutes;
        [System.NonSerialized] public object Selected, KeepVisible, Icon;
    }
    public sealed class CatalogDocument
    {
        public int schema = 1;
        public string modVersion, gameVersion, generatedUtc;
        public List<CatalogEntry> entries;
        public List<string> warnings;
    }
    public static class DataJson
    {
        internal static void WriteTo(TextWriter output, object value)
        {
            using (var writer = new JsonTextWriter(output) { Formatting = Formatting.Indented, CloseOutput = false })
                new JsonSerializer().Serialize(writer, value);
        }
    }
}
