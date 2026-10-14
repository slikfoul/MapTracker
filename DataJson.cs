using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapTracker
{
    // Unity's native serializer omits collections of types from runtime-loaded
    // plugin assemblies. Use the JSON library already bundled with this game.
    public static class DataJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 32,
            Converters = { new PositionConverter() }
        };
        public static string Write(object value, bool pretty = false)
        { return JsonConvert.SerializeObject(value, pretty ? Formatting.Indented : Formatting.None, Settings); }
        internal static void WriteTo(System.IO.TextWriter output, object value)
        {
            using (var writer = new JsonTextWriter(output) { Formatting = Formatting.Indented, CloseOutput = false })
                JsonSerializer.Create(Settings).Serialize(writer, value);
        }
        private static readonly JsonSerializerSettings NetworkSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None, MaxDepth = 32,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Converters = { new PositionConverter() }
        };
        internal static string WriteNetwork(object value) => JsonConvert.SerializeObject(value, Formatting.None, NetworkSettings);
        public static T Read<T>(string json)
        { return JsonConvert.DeserializeObject<T>(json, Settings); }

        private sealed class PositionConverter : JsonConverter
        {
            public override bool CanConvert(Type type) { return type == typeof(Vector3); }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var point = (Vector3)value;
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(point.x);
                writer.WritePropertyName("y"); writer.WriteValue(point.y);
                writer.WritePropertyName("z"); writer.WriteValue(point.z);
                writer.WriteEndObject();
            }
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                var point = JObject.Load(reader);
                return new Vector3((float?)point["x"] ?? 0, (float?)point["y"] ?? 0, (float?)point["z"] ?? 0);
            }
        }
    }
}
