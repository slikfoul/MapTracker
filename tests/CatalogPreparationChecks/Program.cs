using MapTracker;
using Newtonsoft.Json;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
var entries = Enumerable.Range(0, 6000).Select(i => new CatalogEntry {
    key = "prefab:" + (i % 3217), prefab = "name" + (i % 3217), name = "label " + i,
    components = new[] { "component" + i }, products = new[] { "product" + i }
}).ToList();
entries.Insert(3, null);
entries.Add(new CatalogEntry { prefab = "Name1", name = "case-sensitive" });
entries.Add(new CatalogEntry { prefab = null, name = "null-key-first" });
entries.Add(new CatalogEntry { prefab = null, name = "null-key-later" });
var source = entries.ToArray();
var expected = source.Where(e => e != null).GroupBy(e => e.prefab).Select(g => g.First()).ToArray();
var found = new List<CatalogEntry>();
int reads = 0;
IEnumerator<byte> work = CatalogPreparation.CollectDistinct(source, found, e => e != null,
    e => { reads++; return e.prefab; }).GetEnumerator();
int frames = 0;
while (work != null)
{
    int before = reads;
    FrameWork.Advance(ref work, 2, maximumItems: 64, clock: () => 0);
    if (reads - before > 64) throw new Exception("unbounded registry preparation");
    frames++;
}
Check(frames > 1 && found.SequenceEqual(expected),
    "budgeted registry dedup preserves LINQ first occurrence, order, case sensitivity, null entries and null names");
var liveRegistry = entries.Take(5).ToList();
var detachedRegistry = liveRegistry.ToArray();
var observed = new List<CatalogEntry>();
work = CatalogPreparation.Collect(detachedRegistry, observed, e => e != null).GetEnumerator();
FrameWork.Advance(ref work, 2, maximumItems: 1, clock: () => 0);
liveRegistry.Clear();
while (work != null) FrameWork.Advance(ref work, 2, maximumItems: 1, clock: () => 0);
Check(observed.SequenceEqual(detachedRegistry.Where(e => e != null)),
    "live registry changes between frames cannot invalidate the captured registry sequence");
found.Clear();
work = CatalogPreparation.CollectDistinct(source, found, e => e != null, e => e.prefab).GetEnumerator();
FrameWork.Advance(ref work, 2, maximumItems: 1, clock: () => 0);
work.Dispose(); work = null;
Check(found.Count == 1, "cancelled startup preparation does not complete the remaining registry");

var exportSource = expected.Where(e => e != null).ToArray();
exportSource[0].Selected = exportSource[0].Icon = new object();
var warnings = new List<string> { "captured warning" };
CatalogDocument exported = null;
var preparing = CatalogPersistence.Prepare(exportSource, warnings, "test", "game", result => exported = result).GetEnumerator();
warnings[0] = "changed live warning";
Check(exported == null, "export completion is deferred before preparation starts");
Check(preparing.MoveNext() && exported == null, "first export step does not copy the whole catalog");
string firstOriginalName = exportSource[0].name;
string firstOriginalProduct = exportSource[0].products[0];
exportSource[0].name = "changed after first copy";
exportSource[0].products[0] = "changed product after first copy";
while (preparing.MoveNext()) { }
Check(exported.entries.Count == exportSource.Length && exported.entries[0].name == firstOriginalName &&
    exported.entries[0].products[0] == firstOriginalProduct && exported.entries[0].Selected == null &&
    exported.entries[0].Icon == null && exported.warnings.SequenceEqual(new[] { "captured warning" }),
    "incremental exports detach runtime references, warning lists and arrays before worker serialization");
CatalogDocument cancelled = null;
preparing = CatalogPersistence.Prepare(exportSource, warnings, "test", "game", result => cancelled = result).GetEnumerator();
preparing.MoveNext(); preparing.Dispose();
Check(cancelled == null, "cancelled export preparation cannot publish a partial catalog");
var snapshot = CatalogPersistence.Snapshot(exportSource, warnings, "test", "game");
CatalogDocument incremental = null;
foreach (var step in CatalogPersistence.Prepare(exportSource, warnings, "test", "game", result => incremental = result)) { }
incremental.generatedUtc = snapshot.generatedUtc;
Check(JsonConvert.SerializeObject(snapshot) == JsonConvert.SerializeObject(incremental),
    "full and incremental catalog copies have identical persisted schema and values");
string folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "CatalogChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var writer = new CatalogPersistence();
    string path = Path.Combine(folder, "catalog.json");
    var earlierWrite = writer.Enqueue(path, snapshot);
    incremental.modVersion = "later";
    writer.Enqueue(path, incremental).GetAwaiter().GetResult();
    var error = writer.TakeError();
    if (error != null) throw error;
    Check(JsonConvert.DeserializeObject<CatalogDocument>(File.ReadAllText(path)).modVersion == "later" &&
        JsonConvert.DeserializeObject<CatalogDocument>(File.ReadAllText(path + ".bak")).modVersion == "test",
        "prepared exports serialize in request order and keep the previous complete cache as backup");
}
finally { Directory.Delete(folder, true); }
Console.WriteLine($"{passed} catalog checks passed; this is not a Unity frame-time measurement.");
