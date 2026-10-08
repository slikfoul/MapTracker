using MapTracker;
static void Check(bool condition, string name)
{ if (!condition) throw new Exception(name); System.Console.WriteLine("PASS: " + name); }
static bool[] Choose(params LabelBox[] boxes) => LabelLayout.Choose(boxes, 3);
Check(Choose().Length == 0, "an empty minimap has no labels");
Check(Choose(new(0, 0, 80, 14), new(100, 0, 80, 14)).All(v => v), "separated creature names remain visible");
Check(Choose(new(0, 0, 80, 14), new(40, 3, 80, 14)).SequenceEqual(new[] { true, false }), "nearest-first priority hides a colliding name");
Check(Choose(new(0, 0, 80, 14), new(82, 0, 80, 14)).SequenceEqual(new[] { true, false }), "names retain a small readability gap");
Check(Choose(new(0, 0, 80, 14), new(40, 3, 80, 14), new(100, 0, 80, 14)).SequenceEqual(new[] { true, false, true }), "a hidden name does not suppress another name that fits");
Check(Choose(new(0, 0, 80, 14), new(0, 20, 80, 14)).All(v => v), "different rows can share horizontal space");
Check(LabelLayout.Choose(Enumerable.Repeat(new LabelBox(0, 0, 80, 14), 1000).ToArray(), 3).Count(v => v) == 1, "a dense crowd keeps one name instead of overlapping text");
Check(Choose(new(0, 0, 80, 14), new(100, 0, 80, 14), new(200, 0, 80, 14), new(300, 0, 80, 14), new(400, 0, 80, 14), new(500, 0, 80, 14)).All(v => v), "all names are allowed when space permits, without a five-name cap");
var scattered = LabelLayout.Place(Enumerable.Repeat(new LabelBox(100, 100, 70, 14), 20).ToArray(), 3);
Check(scattered.Length == 20 && scattered[0].X == 100 && scattered[0].Y == 100, "nearest label remains anchored and every overlapping label is retained");
Check(LabelLayout.Choose(scattered, 3).All(v => v), "twenty coincident labels can be displaced without intersection");
var separateLabels = new[] { new LabelBox(0, 0, 70, 14), new LabelBox(100, 50, 70, 14) };
var separatePlaced = LabelLayout.Place(separateLabels, 3);
Check(separatePlaced[0].X == 0 && separatePlaced[0].Y == 0 && separatePlaced[1].X == 100 && separatePlaced[1].Y == 50,
    "labels that already fit keep their original positions");
Check(MapLabelZoom.Visible(0.01f, 0.01f, 0.5f) && MapLabelZoom.Visible(0.024f, 0.01f, 0.5f) &&
    !MapLabelZoom.Visible(0.026f, 0.01f, 0.5f) && !MapLabelZoom.Visible(0.1f, 0.01f, 0.5f),
    "large-map names disappear shortly after close zoom rather than waiting for the distant native threshold");
Check(!MapLabelZoom.Visible(0.021f, 0.01f, 0.02f), "an earlier native cutoff remains respected");
Check(CreatureDisplay.ShowHead(false, 30, 0.1f, 0.01f, 0.5f) && CreatureDisplay.ShowHead(false, 300, 0.1f, 0.01f, 0.5f),
    "minimap creatures retain identifying heads beyond thirty meters");
Check(CreatureDisplay.ShowNearbyName(30) && !CreatureDisplay.ShowNearbyName(30.1f),
    "only nearby minimap names use the thirty-meter boundary");
Check(!CreatureDisplay.ShowNearbyName(43, 30) && CreatureDisplay.ShowNearbyName(43, 60) &&
    CreatureDisplay.ShowNearbyName(60, 60) && !CreatureDisplay.ShowNearbyName(60.1f, 60),
    "the reported forty-three-meter creature gets a label at the configured sixty-meter radius, respecting its boundary");
Check(CreatureDisplay.ShowHead(true, 1000, 0.02f, 0.01f, 0.5f) && !CreatureDisplay.ShowHead(true, 1, 0.1f, 0.01f, 0.5f),
    "large-map heads follow zoom rather than distance from the player");
Check(!CreatureDisplay.ShowHead(false, 1, 0.01f, 0.01f, 0.5f, false) &&
    !CreatureDisplay.ShowHead(true, 1, 0.01f, 0.01f, 0.5f, false),
    "dot mode overrides close-range and close-zoom icons on both maps");

var random = new Random(7351);
for (int trial = 0; trial < 60; trial++)
{
    float gap = trial % 6;
    var inputs = Enumerable.Range(0, 100).Select(_ => new LabelBox(
        random.Next(-400, 400), random.Next(-250, 250), random.Next(5, 260), random.Next(10, 40))).ToArray();
    var expected = LayoutReference.Place(inputs, gap);
    var actual = LabelLayout.Place(inputs, gap);
    Check(actual.Zip(expected).All(pair => pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y),
        "spatial placement matches the original ordering and overlaps, randomized map " + trial);
}
static void Benchmark(string name, LabelBox[] input)
{
    LayoutReference.Place(input.Take(20).ToArray(), 3);
    var placement = new LabelLayout.Placement();
    placement.Place(input, 3);
    long oldStart = GC.GetAllocatedBytesForCurrentThread();
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var expected = LayoutReference.Place(input, 3);
    watch.Stop(); double oldMs = watch.Elapsed.TotalMilliseconds;
    long oldBytes = GC.GetAllocatedBytesForCurrentThread() - oldStart;
    watch.Restart(); var cold = new LabelLayout.Placement().Place(input, 3); watch.Stop();
    double coldMs = watch.Elapsed.TotalMilliseconds;
    long newStart = GC.GetAllocatedBytesForCurrentThread();
    watch.Restart(); var actual = placement.Place(input, 3); watch.Stop();
    long newBytes = GC.GetAllocatedBytesForCurrentThread() - newStart;
    Check(actual.Zip(expected).All(pair => pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y), name + " retains exact positions");
    Check(cold.Zip(expected).All(pair => pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y), name + " cold spatial calculation retains exact positions");
    System.Console.WriteLine($"BENCH {name}: legacy {oldMs:F2}ms/{oldBytes}B; cold spatial {coldMs:F2}ms; cached {watch.Elapsed.TotalMilliseconds:F2}ms/{newBytes}B");
}
Benchmark("3000 separated labels", Enumerable.Range(0, 3000).Select(i => new LabelBox(i % 100 * 150, i / 100 * 70, 80, 16)).ToArray());
Benchmark("400 coincident labels", Enumerable.Repeat(new LabelBox(100, 100, 70, 14), 400).ToArray());
var reusedLayout = new LabelLayout.Placement();
var cachedInputs = Enumerable.Repeat(new LabelBox(-128, -64, 70, 14), 40).ToArray();
var originalPositions = reusedLayout.Place(cachedInputs, 3).ToArray();
var translatedInputs = cachedInputs.Select(b => new LabelBox(b.X + 30, b.Y - 20, b.Width, b.Height)).ToArray();
Check(reusedLayout.Place(translatedInputs, 3).Zip(originalPositions).All(p => p.First.X == p.Second.X + 30 && p.First.Y == p.Second.Y - 20),
    "panning cached labels preserves all relative placements");
translatedInputs[5].X += 50;
var movedExpected = LayoutReference.Place(translatedInputs, 3);
Check(reusedLayout.Place(translatedInputs, 3).Zip(movedExpected).All(p => p.First.X == p.Second.X && p.First.Y == p.Second.Y),
    "individually moved markers invalidate cached collision placement");
var scaledExpected = LayoutReference.Place(translatedInputs, 5);
Check(reusedLayout.Place(translatedInputs, 5).Zip(scaledExpected).All(p => p.First.X == p.Second.X && p.First.Y == p.Second.Y),
    "changing the readability gap invalidates cached placement");
