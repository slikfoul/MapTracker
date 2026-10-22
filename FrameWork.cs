using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MapTracker
{
    // Work resumes at its last yield; no restart of a large archive on the next frame.
    internal static class FrameWork
    {
        // Stable merge sort with bounded work between yields. Sorting an entire
        // saved archive inside the first MoveNext defeats Advance's time budget.
        internal static IEnumerable<byte> StableSort<T>(T[] values, int count, Comparison<T> compare)
        {
            if (count < 2) yield break;
            var buffer = new T[count];
            T[] source = values, destination = buffer;
            int operations = 0;
            for (int width = 1; width < count; width = width > count / 2 ? count : width * 2)
            {
                for (int start = 0; start < count; start += width * 2)
                {
                    int middle = Math.Min(start + width, count), end = Math.Min(start + width * 2, count);
                    int left = start, right = middle;
                    for (int output = start; output < end; output++)
                    {
                        destination[output] = right >= end || left < middle && compare(source[left], source[right]) <= 0
                            ? source[left++] : source[right++];
                        if (++operations == 64) { operations = 0; yield return 0; }
                    }
                }
                var temporary = source; source = destination; destination = temporary;
            }
            if (!ReferenceEquals(source, values))
                for (int i = 0; i < count; i++)
                {
                    values[i] = source[i];
                    if (++operations == 64) { operations = 0; yield return 0; }
                }
        }
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
