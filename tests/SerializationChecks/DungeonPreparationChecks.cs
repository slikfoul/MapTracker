using MapTracker;
using System.Reflection;
using UnityEngine;

internal static class DungeonPreparationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(MarkerFile).Assembly.GetType("MapTracker.DungeonDiscoveries");
        var entryField = typeof(MarkerRecord).GetField("entry", fields);
        var liveField = typeof(MarkerRecord).GetField("live", fields);
        var dynamicField = typeof(MarkerRecord).GetField("dynamic", fields);
        var eligible = type.GetMethod("Eligible", methods);
        var legacy = type.GetMethod("Legacy", methods);
        var same = type.GetMethod("SameEntrance", methods);
        var merge = type.GetMethod("Merge", methods);
        var prepare = type.GetMethod("RemoveDuplicatesIncrementally", methods);
        var dungeon = new CatalogEntry { key = "location:audit-dungeon", category = "Dungeons", dungeon = true };
        Dictionary<string, MarkerRecord> Create(int count, bool unique)
        {
            var result = new Dictionary<string, MarkerRecord>();
            for (int i = 0; i < count; i++)
            {
                bool imported = !unique && i % 3 == 0;
                int group = unique ? i : i / 20;
                var position = new Vector3(group * 20, 0, 0);
                var record = new MarkerRecord { id = "dungeon:" + (count - i).ToString("D4"),
                    type = imported && i % 2 == 0 ? "legacy:unknown" : dungeon.key,
                    position = position, imported = imported, visited = i % 5 == 0, completed = i % 7 == 0,
                    favorite = i % 11 == 0, completionRevision = i % 7 == 0 ? 1 : 0 };
                if (!unique && i % 4 != 0) record.dungeonOrigin = position;
                entryField.SetValue(record, dungeon);
                liveField.SetValue(record, !unique && i % 6 == 0);
                result.Add(record.id, record);
            }
            return result;
        }
        void PreviousBehavior(Dictionary<string, MarkerRecord> records)
        {
            var ordered = records.Values.Where(r => (bool)eligible.Invoke(null, new object[] { r }))
                .OrderByDescending(r => (bool)liveField.GetValue(r)).ThenByDescending(r => r.dungeonOrigin.HasValue)
                .ThenBy(r => (bool)legacy.Invoke(null, new object[] { r })).ThenBy(r => r.imported)
                .ThenBy(r => r.id, StringComparer.Ordinal).ToArray();
            var retained = new List<MarkerRecord>();
            foreach (var record in ordered)
            {
                var survivor = retained.FirstOrDefault(r => (bool)same.Invoke(null,
                    new object[] { r, record.type, record.position, (bool)legacy.Invoke(null, new object[] { record }), record.dungeonOrigin }));
                if (survivor == null) retained.Add(record);
                else { merge.Invoke(null, new object[] { survivor, record }); records.Remove(record.id); }
            }
        }
        IEnumerable<byte> Prepare(Dictionary<string, MarkerRecord> records, Action<MarkerRecord> removed = null)
            => (IEnumerable<byte>)prepare.Invoke(null, new object[] { records, removed ?? (_ => { }), (Action)(() => { }) });
        string Saved(Dictionary<string, MarkerRecord> records)
            => DataJson.Write(records.Values.OrderBy(r => r.id, StringComparer.Ordinal).ToArray());
        var original = Create(400, false);
        var incremental = Create(400, false);
        PreviousBehavior(original);
        int steps = 0;
        foreach (var step in Prepare(incremental)) steps++;
        check(Saved(original) == Saved(incremental),
            "budgeted dungeon dedup preserves original live/origin/legacy/imported/id priority and merged progress/aliases");
        check(steps > 800, "dungeon preparation yields during candidate capture and stable sorting");

        var separated = Create(260, true);
        int separatedSteps = Prepare(separated).Count();
        check(separated.Count == 260 && separatedSteps > 260 * 3 + 400,
            "retained dungeon comparisons yield repeatedly even when no entrances can merge");

        var changing = Create(260, true);
        var cursor = Prepare(changing).GetEnumerator();
        for (int i = 0; i < 260; i++)
            if (!cursor.MoveNext()) throw new Exception("dungeon candidate preparation completed before its snapshot was processed");
        var removedRecord = changing.Values.First();
        changing.Remove(removedRecord.id);
        var newRecord = new MarkerRecord { id = "late-live", type = dungeon.key, position = new Vector3(100000, 0, 0) };
        entryField.SetValue(newRecord, dungeon); liveField.SetValue(newRecord, true);
        changing.Add(newRecord.id, newRecord);
        while (cursor.MoveNext()) { }
        cursor.Dispose();
        check(changing.Count == 260 && changing.ContainsKey(newRecord.id) && !changing.ContainsKey(removedRecord.id),
            "dungeon preparation tolerates live additions and removed snapshot records between frames");

        var excluded = Create(3, true);
        excluded.Values.First().shared = true;
        dynamicField.SetValue(excluded.Values.Skip(1).First(), true);
        foreach (var step in Prepare(excluded)) { }
        check(excluded.Count == 3, "dungeon scheduling preserves shared/dynamic eligibility exclusions");

        var rebindRecords = Create(1, true);
        string oldId = rebindRecords.Keys.Single(), retired = null;
        bool callbackBeforeIdentityChange = false;
        var beforeRebind = rebindRecords.Values.Single();
        var rebind = type.GetMethod("RebindTracked", methods);
        var rebound = (MarkerRecord)rebind.Invoke(null, new object[] { rebindRecords, "replacement-id", dungeon,
            beforeRebind.position, (Action<MarkerRecord>)(_ => { }), (Action<string>)(old => {
                retired = old;
                callbackBeforeIdentityChange = !rebindRecords.ContainsKey(old) && beforeRebind.id == old;
            }) });
        check(ReferenceEquals(rebound, beforeRebind) && rebound.id == "replacement-id" && retired == oldId && callbackBeforeIdentityChange,
            "dungeon rebinding retires the previous network identity before changing the surviving marker ID");
    }
}
