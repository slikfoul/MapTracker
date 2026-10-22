#nullable enable
using System.Reflection;
using MapTracker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class PositionReadChecks
{
    private static readonly JsonSerializerSettings ReferenceSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None, MaxDepth = 32,
        Converters = { new ReferenceConverter() }
    };
    private static readonly MethodInfo StreamRead = typeof(DataJson).GetMethod("ReadFrom", BindingFlags.NonPublic | BindingFlags.Static)!
        .MakeGenericMethod(typeof(MarkerFile));
    internal static void Run(Action<bool, string> check)
    {
        foreach (string point in new[] {
            "null", "{}", "{\"x\":-123.25,\"y\":12,\"z\":1e-7}",
            "{\"z\":2,\"x\":\"3.5\",\"y\":null}", "{\"x\":1,\"x\":8,\"z\":false}",
            "{/*before*/\"x\":/*value*/4,\"metadata\":{\"x\":90,\"list\":[1,2]},\"X\":99,\"z\":-2}",
            "{\"x\":\"NaN\",\"y\":\"Infinity\",\"z\":\"-Infinity\"}",
            "{\"x\":18446744073709551616,\"z\":-18446744073709551616}",
            "{\"x\":\"bad\",\"x\":8,\"y\":{},\"y\":4,\"z\":[],\"z\":1}",
            "{\"x\":undefined,\"y\":null}"
        })
        {
            string payload = "{\"world\":\"test\",\"markers\":[{\"id\":\"old\",\"type\":\"portal\",\"position\":" + point + ",\"dungeonOrigin\":" + point + "}]}";
            var old = JsonConvert.DeserializeObject<MarkerFile>(payload, ReferenceSettings)!;
            var current = DataJson.Read<MarkerFile>(payload);
            using var input = new StringReader(payload);
            var streamed = (MarkerFile)StreamRead.Invoke(null, new object[] { input })!;
            check(DataJson.Write(old) == DataJson.Write(current) && DataJson.Write(old) == DataJson.Write(streamed),
                "streaming position reader preserves legacy coordinate/null/duplicate/unknown-field semantics: " + point);
        }
        foreach (string payload in new[] {
            "{\"markers\":[{\"position\":[1,2,3]}]}", "{\"markers\":[{\"position\":{\"x\":{}}}]}",
            "{\"markers\":[{\"position\":{\"x\":\"bad\"}}]}", "{\"markers\":[{\"position\":{\"x\":3}",
            "{} {}", "{} garbage"
        })
        {
            bool rejected = false;
            try { using var input = new StringReader(payload); StreamRead.Invoke(null, new object[] { input }); }
            catch (TargetInvocationException) { rejected = true; }
            check(rejected, "streaming archive reader rejects malformed or trailing content");
        }
        using (var input = new StringReader("{} /*end*/"))
        {
            var file = (MarkerFile)StreamRead.Invoke(null, new object[] { input })!;
            check(file.markers.Count == 0 && input.Peek() == -1, "stream reader accepts trailing comments and leaves caller-owned input open");
        }
        var fixture = new MarkerFile { world = "benchmark", character = "local" };
        for (int i = 0; i < 4096; i++) fixture.markers.Add(new MarkerRecord {
            id = i.ToString(), type = "location:test", position = new Vector3(i + 0.25f, 12.5f, -i),
            dungeonOrigin = new Vector3(i, 12, -i), completionRevision = 987654321012345678L,
            dungeonAliases = new[] { "previous:" + i }, portalTag = "Север" });
        string json = DataJson.Write(fixture);
        // Warm serializers and converters before comparing allocations.
        JsonConvert.DeserializeObject<MarkerFile>(json, ReferenceSettings);
        DataJson.Read<MarkerFile>(json);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var previous = JsonConvert.DeserializeObject<MarkerFile>(json, ReferenceSettings);
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var result = DataJson.Read<MarkerFile>(json);
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        check(DataJson.Write(previous) == DataJson.Write(result), "4096-row streaming-coordinate fixture keeps every saved field");
        check(newBytes < oldBytes * 0.7, "direct coordinate parsing removes at least 30 percent of fixture deserialization allocations");
        System.Console.WriteLine($"READ ALLOCATIONS: old={oldBytes}; direct={newBytes}; rows=4096; managed runtime, not Unity frame timing.");
    }
    private sealed class ReferenceConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(Vector3) || Nullable.GetUnderlyingType(type) == typeof(Vector3);
        public override object? ReadJson(JsonReader reader, Type type, object? existing, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return type == typeof(Vector3) ? (object)default(Vector3) : null;
            var point = JObject.Load(reader);
            return new Vector3((float?)point["x"] ?? 0, (float?)point["y"] ?? 0, (float?)point["z"] ?? 0);
        }
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotSupportedException();
    }
}
