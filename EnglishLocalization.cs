using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MapTracker
{
    internal static class EnglishLocalization
    {
        private static readonly Dictionary<string, string> words = new Dictionary<string, string>();
        private static int previousFiles = -1;
        private static readonly FieldInfo Settings = AccessTools.Field(typeof(Localization), "m_localizationSettings");
        private static readonly MethodInfo Parse = AccessTools.Method(typeof(Localization), "DoQuoteLineSplit");
        internal static string Translate(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '$') return value ?? "";
            var native = Localization.instance;
            if (native.GetSelectedLanguage() == "English") return native.Localize(value);
            object settings = Settings.GetValue(null);
            if (settings == null) return value;
            object files = AccessTools.Field(settings.GetType(), "Localizations")?.GetValue(settings) ??
                AccessTools.Property(settings.GetType(), "Localizations")?.GetValue(settings, null);
            if (!(files is ICollection collection)) return value;
            if (previousFiles != collection.Count)
            {
                words.Clear();
                foreach (var item in collection)
                {
                    if (!(item is TextAsset asset) || asset == null) continue;
                    using (var reader = new StringReader(asset.text))
                    {
                        string header = reader.ReadLine(); if (header == null) continue;
                        int column = Array.FindIndex(header.Split(','), s => s.Trim().Trim('"') == "English");
                        if (column < 0) continue;
                        var rows = (List<List<string>>)Parse.Invoke(native, new object[] { reader });
                        foreach (var row in rows)
                            if (row.Count > column && !string.IsNullOrEmpty(row[0]) && !row[0].StartsWith("//", StringComparison.Ordinal))
                                words[row[0]] = string.IsNullOrWhiteSpace(row[column]) && row.Count > 1 ? row[1] : row[column].Trim();
                    }
                }
                previousFiles = collection.Count;
            }
            return words.TryGetValue(value.Substring(1), out var translated) ? translated : value;
        }
    }
}
