using System.Diagnostics;

namespace MapTracker
{
    internal static class ScanTimeBudget
    {
        internal static bool WithinBudget(long start, int milliseconds) =>
            (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency < milliseconds;
    }
}
