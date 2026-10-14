using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MapTracker
{
    // Work resumes at its last yield; no restart of a large archive on the next frame.
    internal static class FrameWork
    {
        internal static bool Advance(ref IEnumerator<byte> work, int milliseconds, int maximumItems = 512,
            Func<long> clock = null)
        {
            if (work == null) return true;
            long start = clock == null ? Stopwatch.GetTimestamp() : clock();
            for (int count = 0; count < maximumItems; count++)
            {
                if (!work.MoveNext()) { work.Dispose(); work = null; return true; }
                long now = clock == null ? Stopwatch.GetTimestamp() : clock();
                if ((now - start) * 1000.0 / Stopwatch.Frequency >= milliseconds) break;
            }
            return false;
        }
    }
}
