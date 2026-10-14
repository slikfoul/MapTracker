using MapTracker;
using System.Diagnostics;

void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}
int operations = 0, completions = 0, disposals = 0;
IEnumerable<byte> Archive(int count)
{
    try
    {
        for (int i = 0; i < count; i++) { operations++; yield return 0; }
        completions++;
    }
    finally { disposals++; }
}
IEnumerator<byte> work = Archive(18299).GetEnumerator();
int frames = 0;
while (work != null)
{
    int before = operations;
    bool done = FrameWork.Advance(ref work, 2, clock: () => 0);
    Check(operations - before <= 512 && done == (work == null), "frame " + ++frames + " respects item limit and completion state");
}
Check(operations == 18299 && completions == 1 && disposals == 1 && frames == 36,
    "large archive resumes without repeats and completes/disposes exactly once");
Check(FrameWork.Advance(ref work, 2) && completions == 1, "completed work cannot rerun cleanup");
long clock = 0;
operations = 0;
work = Archive(100).GetEnumerator();
Check(!FrameWork.Advance(ref work, 2, clock: () => clock += Stopwatch.Frequency / 1000) && operations == 2,
    "elapsed-time limit ends a frame before the item cap");
Check(!FrameWork.Advance(ref work, 2, clock: () => clock += Stopwatch.Frequency / 1000) && operations == 4,
    "following frame resumes at the next record with a fresh budget");
work.Dispose(); work = null;
Check(disposals == 2 && completions == 1, "world exit disposes unfinished work without committing completion");
operations = 0; work = Archive(3).GetEnumerator();
FrameWork.Advance(ref work, 2, clock: () => clock += Stopwatch.Frequency);
Check(operations == 1, "single expensive operation yields immediately rather than batching more work");
work.Dispose(); work = null;
Console.WriteLine("Scheduler checks do not measure live Unity frame times.");
