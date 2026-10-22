using System;
using System.Collections.Generic;

namespace MapTracker
{
    internal static class CatalogPreparation
    {
        // The caller captures registry references first. Name lookup and duplicate
        // filtering then resume across frames without keeping a live-list enumerator.
        internal static IEnumerable<byte> CollectDistinct<T>(IEnumerable<T> source, ICollection<T> destination,
            Predicate<T> include, Func<T, string> key)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in source)
            {
                if (include(value) && seen.Add(key(value))) destination.Add(value);
                yield return 0;
            }
        }

        internal static IEnumerable<byte> Collect<T>(IEnumerable<T> source, ICollection<T> destination, Predicate<T> include)
        {
            foreach (var value in source)
            {
                if (include(value)) destination.Add(value);
                yield return 0;
            }
        }
    }
}
