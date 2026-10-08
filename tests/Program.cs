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
