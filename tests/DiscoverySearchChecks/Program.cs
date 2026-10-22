using MapTracker;

static void Check(bool value, string message)
{ if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
static List<MarkerRecord> Reference(IEnumerable<MarkerRecord> records, string search, bool favorites, float x, float z)
{
    string term = (search ?? "").Trim();
    return records.Where(r => !r.dynamic && !r.shared && r.entry != null && !r.entry.creature && (!favorites || r.favorite) &&
        (term.Length == 0 || r.name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
        (r.type ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
        (r.portalTag ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
        .OrderByDescending(r => r.favorite).ThenBy(r =>
        { float dx = r.position.x - x, dz = r.position.z - z; return (float)Math.Sqrt(dx * dx + dz * dz); })
        .ThenBy(r => r.id, StringComparer.Ordinal).ToList();
}
static void Finish(DiscoverySearch query)
{
    int frames = 0;
    while (query.Pending)
    { query.Advance(maximumItems: 7, clock: () => 0); if (++frames > 100000) throw new Exception("query failed to finish"); }
}
var random = new Random(7301);
var records = Enumerable.Range(0, 5021).Select(i => new MarkerRecord
{
    id = i % 17 == 0 ? "same" : i.ToString(), type = i % 3 == 0 ? "mine" : "portal", portalTag = i % 7 == 0 ? "HOME" : null,
    name = i % 4 == 0 ? "Infested Mine" : "Portal", favorite = i % 11 == 0, shared = i % 31 == 0, dynamic = i % 53 == 0,
    entry = i % 61 == 0 ? null : new CatalogEntry { creature = i % 43 == 0 },
    position = new Position { x = random.Next(-100, 101), z = random.Next(-100, 101) }
}).ToList();
var current = new HashSet<MarkerRecord>(records);
foreach (string term in new[] { "", " HOME ", "mINe", "portal", "missing" })
    foreach (bool favorites in new[] { false, true })
    {
        var query = new DiscoverySearch();
        query.Begin(records, term, favorites, 13, -17, r => r.name, current.Contains);
        Finish(query);
        Check(query.Results.SequenceEqual(Reference(records, term, favorites, 13, -17)),
            "budgeted query exactly matches original filtering and ordering: " + term + "/" + favorites);
    }
var stable = Enumerable.Range(0, 103).Select(i => new MarkerRecord
{ id = "identical", type = "same", name = "same", entry = new CatalogEntry(), position = new Position { x = 10, z = 20 } }).ToList();
var tied = new DiscoverySearch(); tied.Begin(stable, "", false, 0, 0, r => r.name, _ => true); Finish(tied);
Check(tied.Results.SequenceEqual(stable), "equal frozen sort keys preserve original encounter order");
int names = 0;
var paced = new DiscoverySearch(); paced.Begin(records, "mine", false, 0, 0, r => { names++; return r.name; }, current.Contains);
paced.Advance(maximumItems: 1, clock: () => 0);
Check(names == 0 && paced.Pending, "first step only captures references without matching every archived record");
for (int i = 0; i < 20; i++)
{
    int before = names; paced.Advance(maximumItems: 7, clock: () => 0);
    Check(names - before <= 7, "matching respects per-frame item budget " + i);
}
paced.Cancel(); Check(!paced.Pending && paced.Results.Count == 0, "cancellation cannot publish a partial query");

var changes = stable.ToDictionary(r => Guid.NewGuid(), r => r);
var live = new HashSet<MarkerRecord>(stable);
var mutation = new DiscoverySearch(); mutation.Begin(changes.Values, "", false, 0, 0, r => r.name, live.Contains);
mutation.Advance(maximumItems: 1, clock: () => 0);
var removed = stable[12]; changes.Remove(changes.First(p => p.Value == removed).Key); live.Remove(removed);
var added = new MarkerRecord { id = "new", name = "new", type = "new", entry = new CatalogEntry() };
changes.Add(Guid.NewGuid(), added); live.Add(added);
Finish(mutation);
Check(!mutation.Results.Contains(removed) && !mutation.Results.Contains(added) && mutation.Results.Count == stable.Count - 1,
    "dictionary additions and removals between frames preserve the captured request and exclude removed records");
mutation.Begin(changes.Values, "", false, 0, 0, r => r.name, live.Contains); Finish(mutation);
Check(mutation.Results.Contains(added), "next refresh includes new discoveries");

var replacement = new DiscoverySearch(); replacement.Begin(records, "portal", false, 0, 0, r => r.name, current.Contains); Finish(replacement);
var previous = replacement.Results;
replacement.Begin(records, "mine", false, 0, 0, r => r.name, current.Contains);
replacement.Advance(maximumItems: 10, clock: () => 0);
Check(ReferenceEquals(previous, replacement.Results), "last completed list remains visible throughout preparation");
replacement.Begin(records, "home", true, 0, 0, r => r.name, current.Contains); Finish(replacement);
Check(replacement.Results.SequenceEqual(Reference(records, "home", true, 0, 0)), "a superseded request cannot replace a newer query result");
replacement.Begin(records, "mine", false, 0, 0, r => r.name, current.Contains); replacement.Reset();
Check(!replacement.Pending && replacement.Results.Count == 0, "world reset cancels work and clears previous-world results");

var frozen = new DiscoverySearch(); frozen.Begin(stable, "", false, 0, 0, r => r.name, _ => true);
// One snapshot yield, one per row, then the sorting-input yield: all keys are now frozen.
frozen.Advance(maximumItems: stable.Count + 2, clock: () => 0);
stable[70].favorite = true; stable[70].position = new Position(); stable[70].id = "changed";
Finish(frozen);
Check(frozen.Results.SequenceEqual(stable), "mid-sort favorite, position and id changes cannot corrupt frozen ordering");
frozen.Begin(stable, "", false, 0, 0, r => r.name, _ => true); Finish(frozen);
Check(frozen.Results[0] == stable[70], "following refresh observes changed favorite and sort keys");

var eligibleRows = Enumerable.Range(0, 6).Select(i => new MarkerRecord
{ id = i.ToString(), type = "portal", name = "Portal", favorite = true, entry = new CatalogEntry() }).ToList();
var eligibleQuery = new DiscoverySearch(); eligibleQuery.Begin(eligibleRows, "", true, 0, 0, r => r.name, _ => true);
eligibleQuery.Advance(maximumItems: eligibleRows.Count + 2, clock: () => 0);
eligibleRows[0].entry = null;
eligibleRows[1].dynamic = true;
eligibleRows[2].shared = true;
eligibleRows[3].entry.creature = true;
eligibleRows[4].favorite = false;
Finish(eligibleQuery);
Check(eligibleQuery.Results.SequenceEqual(new[] { eligibleRows[5] }),
    "records losing entry, static/local/noncreature status or required favorite after capture cannot enter final results");
eligibleRows[5].entry = null;
Check(!DiscoverySearch.Eligible(eligibleRows[5], true) && !DiscoverySearch.Eligible(null, false),
    "published rows losing eligibility can be rejected safely during rendering and clicks");
Console.WriteLine("Query checks validate managed semantics and operation budgets, not live Unity frame times.");
