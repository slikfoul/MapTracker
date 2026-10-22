using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapTracker
{
    internal sealed class FilterChoice
    {
        internal List<CatalogEntry> Members;
        internal string Label;
        // The generic portal alias shares the wood portal's name but can have
        // different artwork. Use the wood portal's own icon for their filter row.
        internal Sprite Icon => Members.OrderByDescending(e => e.prefab == "portal_wood")
            .Select(e => e.Icon).FirstOrDefault(icon => icon != null);
        internal bool Selected => Members.Any(e => e.Selected.Value);
        internal bool CanKeep => Members.All(e => !e.creature && e.category != "Dropped items");
        internal bool KeepVisible => Members.Any(e => e.KeepVisible?.Value ?? false);
        internal bool Contains(string key) => Members.Any(e => e.key == key);
        internal static List<FilterChoice> Build(IEnumerable<CatalogEntry> source, Func<CatalogEntry, string> label)
        {
            return source.Where(e => !e.technical && !CatalogContent.IsServiceDefinition(e)).GroupBy(e => e.category + "\n" + label(e))
                .Select(g => new FilterChoice { Members = g.ToList(), Label = label(g.First()) }).OrderBy(c => c.Label).ToList();
        }
    }
}
