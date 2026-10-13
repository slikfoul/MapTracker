using MapTracker;
static void Check(bool condition, string name)
{ if (!condition) throw new Exception(name); System.Console.WriteLine("PASS: " + name); }
static bool[] Choose(params LabelBox[] boxes) => LabelLayout.Choose(boxes, 3);
var tooltipArea = new LabelBox(0, 0, 1200, 800);
var portalCloud = Enumerable.Range(0, 22).Select(i => new LabelBox(650 + i % 6 * 24, 300 + i / 6 * 25, 24, 24)).ToArray();
bool NoCover(LabelBox panel, IEnumerable<LabelBox> boxes) => boxes.All(b => panel.X + panel.Width <= b.X || panel.X >= b.X + b.Width || panel.Y + panel.Height <= b.Y || panel.Y >= b.Y + b.Height);
Check(GroupTooltipLayout.TryPlace(portalCloud, tooltipArea, 320, 348, 700, 350, 20, out var portalPopup) &&
    portalPopup.X + portalPopup.Width < portalCloud.Min(p => p.X) && NoCover(portalPopup, portalCloud),
    "the 22-portal group opens a panel to the left of every expanded member without covering icons");
var edgeCloud = portalCloud.Select(p => new LabelBox(p.X - 620, p.Y, p.Width, p.Height)).ToArray();
Check(GroupTooltipLayout.TryPlace(edgeCloud, tooltipArea, 320, 348, 80, 350, 20, out var edgePopup) &&
    LabelLayout.Inside(edgePopup, tooltipArea) && NoCover(edgePopup, edgeCloud),
    "near the left map edge the panel finds a free visible area instead of clamping over the group");
var withLabels = portalCloud.Concat(new[] { new LabelBox(600, 278, 180, 20) }).ToArray();
Check(GroupTooltipLayout.TryPlace(withLabels, tooltipArea, 320, 348, 700, 350, 20, out var labelledPopup) && NoCover(labelledPopup, withLabels),
    "group popup placement also protects visible member names");
Check(!GroupTooltipLayout.TryPlace(new[] { tooltipArea }, tooltipArea, 320, 348, 600, 400, 20, out _),
    "when the entire viewport is occupied no popup is placed over protected objects");
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
var protectedInput = new List<LabelBox> { new LabelBox(90, 90, 40, 40), new LabelBox(90, 90, 100, 20), new LabelBox(100, 100, 120, 20) };
var protectedResult = new LabelLayout.Placement().Place(protectedInput, 3);
bool Intersects(LabelBox a, LabelBox b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
Check(!Intersects(protectedResult[0], protectedResult[1]) && !Intersects(protectedResult[0], protectedResult[2]),
    "the reserved character marker area stays clear of nearby names");
var anchoredSelection = new LabelLayout.Selection();
var anchoredInput = new[] { new LabelBox(90, 90, 40, 40), new LabelBox(90, 90, 100, 20), new LabelBox(200, 100, 120, 20) };
var anchoredOriginal = anchoredInput.ToArray();
Check(anchoredSelection.Choose(anchoredInput, 3).SequenceEqual(new[] { true, false, true }) && anchoredInput.SequenceEqual(anchoredOriginal),
    "crowded names are suppressed at their own markers without moving text or covering the character arrow");
Check(anchoredSelection.Choose(anchoredInput.Select(b => new LabelBox(b.X + 20, b.Y - 40, b.Width, b.Height)).ToArray(), 3).SequenceEqual(new[] { true, false, true }),
    "panning retains cached anchored name choices");
anchoredInput[1].X = 450;
Check(anchoredSelection.Choose(anchoredInput, 3).All(v => v), "moving a crowded marker into free space reveals its anchored name");
anchoredInput[1].X = 250; anchoredInput[2].Width = 300;
Check(anchoredSelection.Choose(anchoredInput, 3).SequenceEqual(new[] { true, true, false }),
    "larger fonts invalidate anchored choices and hide collisions rather than pushing names away");
Check(LabelLayout.Inside(new LabelBox(10, 10, 40, 20), new LabelBox(0, 0, 100, 100)) &&
    !LabelLayout.Inside(new LabelBox(80, 10, 40, 20), new LabelBox(0, 0, 100, 100)),
    "names that cross the map edge remain available on hover rather than spilling outside it");
for (int trial = 0; trial < 60; trial++)
{
    var inputs = Enumerable.Range(0, 120).Select(_ => new LabelBox(random.Next(-300, 300), random.Next(-250, 250), random.Next(10, 180), random.Next(12, 40))).ToArray();
    bool[] expected = new bool[inputs.Length];
    for (int i = 0; i < inputs.Length; i++)
    {
        expected[i] = true;
        for (int j = 0; j < i; j++)
            if (expected[j] && inputs[i].X < inputs[j].X + inputs[j].Width + 3 && inputs[i].X + inputs[i].Width + 3 > inputs[j].X &&
                inputs[i].Y < inputs[j].Y + inputs[j].Height + 3 && inputs[i].Y + inputs[i].Height + 3 > inputs[j].Y)
            { expected[i] = false; break; }
    }
    Check(anchoredSelection.Choose(inputs, 3).SequenceEqual(expected), "spatial anchored selection matches independent collision checks " + trial);
}
