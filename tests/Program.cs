using MapTracker;

static ClusterPoint Point(float x, float y, string kind = "portal") => new ClusterPoint { X = x, Y = y, Kind = kind };
static void Check(bool value, string scenario)
{
    if (!value) throw new Exception("FAIL: " + scenario);
    Console.WriteLine("PASS: " + scenario);
}
static void CompareReference(List<ClusterPoint> points, float distance, int minimum)
{
    // Independent breadth-first distance graph checks the optimized spatial index.
    var seen = new bool[points.Count];
    var expected = new List<string>();
    for (int i = 0; i < points.Count; i++)
    {
        if (seen[i]) continue;
        var group = new List<int>();
        var pending = new Queue<int>();
        pending.Enqueue(i); seen[i] = true;
        while (pending.Count > 0)
        {
            int a = pending.Dequeue(); group.Add(a);
            for (int b = 0; b < points.Count; b++)
            {
                float x = points[a].X - points[b].X, y = points[a].Y - points[b].Y;
                if (!seen[b] && points[a].Kind == points[b].Kind && x * x + y * y <= distance * distance)
                { seen[b] = true; pending.Enqueue(b); }
            }
        }
        if (group.Count >= minimum) { group.Sort(); expected.Add(string.Join(",", group)); }
    }
    var actual = ClusterMath.Build(points, distance, minimum).Select(g => { g.Sort(); return string.Join(",", g); }).Order().ToArray();
    if (!expected.Order().SequenceEqual(actual)) throw new Exception("Spatial clustering differs from the distance graph.");
}

var bridge = new List<ClusterPoint> { Point(0, 0), Point(35, 35), Point(17, 17) };
Check(ClusterMath.Build(bridge, 36, 3).Single().Count == 3, "a third marker joins both nearby components");
Check(ClusterMath.Build(new() { Point(-1, 0), Point(1, 0), Point(36, 0) }, 36, 3).Count == 1, "negative coordinates and cell boundaries");
Check(ClusterMath.Build(new() { Point(0, 0), Point(0, 0), Point(0, 0, "ore") }, 36, 3).Count == 0, "different types do not merge");
Check(ClusterMath.Build(new() { Point(0, 0), Point(1, 0) }, 36, 3).Count == 0, "groups below the configured threshold stay separate");
Check(ClusterMath.Build(new() { Point(0, 0), Point(72, 0), Point(144, 0) }, 36, 3).Count == 0, "zooming in separates markers");
var random = new Random(107);
for (int sample = 0; sample < 100; sample++)
{
    var points = Enumerable.Range(0, 80).Select(_ => Point(random.Next(-120, 121), random.Next(-120, 121), random.Next(3).ToString())).ToList();
    CompareReference(points, 36, 3);
}
Check(true, "100 mixed-type maps match the independent reference");
var dense = Enumerable.Range(0, 10000).Select(_ => Point(10, 10)).ToList();
Check(ClusterMath.Build(dense, 36, 3).Single().Count == dense.Count, "10,000 overlapping portal markers form one group");
var chain = Enumerable.Range(0, 30).Select(i => new ClusterPoint { X = i * 20, Y = 0, WorldX = i * 4, WorldZ = 0, Kind = "plant" }).ToList();
var bounded = ClusterMath.BuildBounded(chain, 36, 2, 72, 40);
Check(bounded.Count > 1 && bounded.SelectMany(g => g).Distinct().Count() == chain.Count,
    "long chains split into bounded groups without losing or duplicating any members");
Check(bounded.All(g => g.Max(i => chain[i].X) - g.Min(i => chain[i].X) <= 72), "full screen group diameter stays bounded");
var distant = Enumerable.Range(0, 20).Select(i => new ClusterPoint { X = i, Y = 0, WorldX = i * 10, Kind = "plant" }).ToList();
Check(ClusterMath.BuildBounded(distant, 36, 2, 72, 40).All(g => g.Max(i => distant[i].WorldX) - g.Min(i => distant[i].WorldX) <= 40),
    "world diameter stays bounded at distant map zoom");
Check(ClusterMath.BuildBounded(new() { Point(0, 0, "plant"), Point(0, 0, "ore") }, 36, 2, 72, 40).Count == 0,
    "bounded grouping preserves type separation and the minimum member threshold");
var boundedWatch = System.Diagnostics.Stopwatch.StartNew();
Check(ClusterMath.BuildBounded(dense, 36, 3, 72, 40).Single().Count == dense.Count, "bounded clustering handles 10,000 coincident markers");
Console.WriteLine("Bounded clustering 10,000 markers: " + boundedWatch.Elapsed.TotalMilliseconds.ToString("0.0") + " ms (headless check).");
static ClusterPoint Icon(float x, float y, float halfSize = 10, string kind = "creature", float worldX = 0) =>
    new ClusterPoint { X = x, Y = y, HalfWidth = halfSize, HalfHeight = halfSize, Kind = kind, WorldX = worldX };
var distantIcons = new List<ClusterPoint> { Icon(0, 0, worldX: 0), Icon(100, 0, worldX: 10), Icon(200, 0, worldX: 20) };
Check(ClusterMath.BuildOverlapping(distantIcons, 3, 2, 300).Count == 0,
    "a three-hundred-meter span never groups visibly separated icons");
var zoomedOutIcons = new List<ClusterPoint> { Icon(0, 0, worldX: 0), Icon(10, 0, worldX: 10), Icon(20, 0, worldX: 20) };
Check(ClusterMath.BuildOverlapping(zoomedOutIcons, 3, 2, 300).Single().Count == 3,
    "zooming out joins overlapping screen icons without changing the world-span setting");
var zoomedInIcons = new List<ClusterPoint> { Icon(0, 0, worldX: 0), Icon(40, 0, worldX: 10), Icon(80, 0, worldX: 20) };
Check(ClusterMath.BuildOverlapping(zoomedInIcons, 3, 2, 300).Count == 0,
    "zooming in automatically reveals each icon as soon as there is room");
var iconBridge = new List<ClusterPoint> { Icon(0, 0), Icon(30, 0), Icon(15, 0) };
Check(ClusterMath.BuildOverlapping(iconBridge, 3, 3, 300).Single().Count == 3,
    "a bridge icon merges compatible crowded groups while retaining every member");
Check(ClusterMath.BuildOverlapping(new() { Icon(0, 0, 4), Icon(15, 0, 12) }, 1, 2, 300).Single().Count == 2,
    "grouping uses both icons' displayed sizes rather than a fixed pixel radius");
Check(ClusterMath.BuildOverlapping(new() { Icon(0, 0, kind: "plant"), Icon(0, 0, kind: "ore") }, 3, 2, 300).Count == 0,
    "screen overlap does not mix unrelated object types");
Check(ClusterMath.BuildOverlapping(new() { Icon(0, 0, worldX: 0), Icon(0, 0, worldX: 60) }, 3, 2, 40).Count == 0,
    "even coincident screen icons retain the configured maximum world span");
var iconChain = Enumerable.Range(0, 30).Select(i => Icon(i * 10, 0, worldX: i)).ToList();
var iconParts = ClusterMath.BuildOverlapping(iconChain, 3, 2, 300);
Check(iconParts.Count > 1 && iconParts.SelectMany(g => g).Distinct().Count() == iconChain.Count &&
    iconParts.All(g => g.Max(i => iconChain[i].X) - g.Min(i => iconChain[i].X) <= 46),
    "crowded icon chains split into compact visible groups without moving, dropping or duplicating members");
var scaledIcons = zoomedOutIcons.Select(p => new ClusterPoint { X = p.X * 2, Y = p.Y * 2,
    HalfWidth = p.HalfWidth * 2, HalfHeight = p.HalfHeight * 2, Kind = p.Kind, WorldX = p.WorldX, WorldZ = p.WorldZ }).ToList();
Check(ClusterMath.BuildOverlapping(scaledIcons, 6, 2, 300).Single().Count == 3,
    "UI scaling changes icon geometry and padding together without changing group membership");
var denseIcons = Enumerable.Range(0, 10000).Select(_ => Icon(10, 10)).ToList();
var iconWatch = System.Diagnostics.Stopwatch.StartNew();
Check(ClusterMath.BuildOverlapping(denseIcons, 3, 3, 300).Single().Count == 10000,
    "ten thousand coincident screen icons form one group through the spatial index");
Console.WriteLine("Overlap grouping 10,000 icons: " + iconWatch.Elapsed.TotalMilliseconds.ToString("0.0") + " ms (headless check).");
var smallIconChain = Enumerable.Range(0, 30).Select(i => Icon(i * 5, 0, 3, worldX: i)).ToList();
smallIconChain.Add(Icon(500, 500, 32, kind: "unrelated-large-icon"));
Check(ClusterMath.BuildOverlapping(smallIconChain, 3, 2, 300).All(g =>
    g.Max(i => smallIconChain[i].X) - g.Min(i => smallIconChain[i].X) <= 18),
    "an unrelated large icon cannot inflate the screen footprint of tiny-marker groups");
for (int trial = 0; trial < 50; trial++)
{
    var mapIcons = Enumerable.Range(0, 100).Select(i => Icon(random.Next(-100, 101), random.Next(-100, 101), random.Next(3, 17),
        (i % 3).ToString(), random.Next(-30, 31))).ToList();
    var groups = ClusterMath.BuildOverlapping(mapIcons, 3, 2, 36);
    bool sound = groups.SelectMany(g => g).Distinct().Count() == groups.Sum(g => g.Count);
    foreach (var group in groups)
    {
        var connected = new HashSet<int> { group[0] }; var pending = new Queue<int>(); pending.Enqueue(group[0]);
        while (pending.Count > 0)
        {
            var a = mapIcons[pending.Dequeue()];
            foreach (int id in group)
            {
                var b = mapIcons[id];
                if (!connected.Contains(id) && Math.Abs(a.X - b.X) <= a.HalfWidth + b.HalfWidth + 3 &&
                    Math.Abs(a.Y - b.Y) <= a.HalfHeight + b.HalfHeight + 3)
                { connected.Add(id); pending.Enqueue(id); }
            }
        }
        sound &= connected.Count == group.Count && group.Select(i => mapIcons[i].Kind).Distinct().Count() == 1 &&
            group.Max(i => mapIcons[i].WorldX) - group.Min(i => mapIcons[i].WorldX) <= 36;
    }
    Check(sound, "mixed-size screen groups preserve connectivity, identities, types and world limits " + trial);
}
var threeLeeches = new List<CreatureNamePoint> {
    new() { WorldX = 0, WorldZ = 0, ScreenX = 100, ScreenY = 100, Kind = "leech", Count = 1 },
    new() { WorldX = 5, WorldZ = 0, ScreenX = 200, ScreenY = 100, Kind = "leech", Count = 1 },
    new() { WorldX = 10, WorldZ = 0, ScreenX = 300, ScreenY = 100, Kind = "leech", Count = 1 }
};
Check(CreatureNameGroupMath.Build(threeLeeches, 15).Single().Count == 3,
    "nearby same-species names combine independently of screen icon overlap");
CreatureNameGroupMath.Centre(threeLeeches, out float nameX, out float nameY, out int nameCount);
Check(nameX == 200 && nameY == 100 && nameCount == 3,
    "three creature names are centered between their original markers with the correct count");
var originalIcons = threeLeeches.Select(p => p.ScreenX).ToArray();
threeLeeches[0] = new CreatureNamePoint { WorldX = 0, ScreenX = 100, ScreenY = 100, Kind = "leech", Count = 3 };
CreatureNameGroupMath.Centre(threeLeeches, out nameX, out nameY, out nameCount);
Check(nameCount == 5 && nameX == 160 && threeLeeches.Select(p => p.ScreenX).SequenceEqual(originalIcons),
    "centered names count already-grouped icons without changing any marker positions");
var separateSpecies = new List<CreatureNamePoint> { threeLeeches[0], new() { WorldX = 1, Kind = "skeleton", Count = 1 } };
Check(CreatureNameGroupMath.Build(separateSpecies, 15).Count == 0 && CreatureNameGroupMath.Build(threeLeeches, 2).Count == 0,
    "name grouping preserves species separation and its independent world radius");
var widerWorld = new List<ClusterPoint> { Icon(0, 0, worldX: 0), Icon(0, 0, worldX: 600) };
Check(ClusterMath.BuildOverlapping(widerWorld, 3, 2, 800).Single().Count == 2 && ClusterMath.BuildOverlapping(widerWorld, 3, 2, 400).Count == 0,
    "the expanded eight-hundred-meter group limit is effective while the new default still limits world spans");
