using System;
using Newtonsoft.Json;
using System.Globalization;
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
        internal static T ReadFrom<T>(System.IO.TextReader input)
        {
            using (var reader = new JsonTextReader(input) { CloseInput = false })
            {
                var serializer = JsonSerializer.Create(Settings);
                T value = serializer.Deserialize<T>(reader);
                // Match JsonConvert's rejection of trailing non-comment content.
                while (reader.Read())
                    if (reader.TokenType != JsonToken.Comment)
                        throw new JsonSerializationException("Additional content after JSON document.");
                return value;
            }
        }

        private sealed class PositionConverter : JsonConverter
        {
            private static readonly object InvalidCoordinate = new object();
            public override bool CanConvert(Type type)
            { return type == typeof(Vector3) || Nullable.GetUnderlyingType(type) == typeof(Vector3); }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null) { writer.WriteNull(); return; }
                var point = (Vector3)value;
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(point.x);
                writer.WritePropertyName("y"); writer.WriteValue(point.y);
                writer.WritePropertyName("z"); writer.WriteValue(point.z);
                writer.WriteEndObject();
            }
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return type == typeof(Vector3) ? (object)default(Vector3) : null;
                if (reader.TokenType != JsonToken.StartObject)
                    throw new JsonSerializationException("Expected a position object.");
                object x = null, y = null, z = null;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndObject) return new Vector3(Coordinate(x), Coordinate(y), Coordinate(z));
                    if (reader.TokenType == JsonToken.Comment) continue;
                    if (reader.TokenType != JsonToken.PropertyName)
                        throw new JsonSerializationException("Expected a position property.");
                    string name = (string)reader.Value;
                    do { if (!reader.Read()) throw new JsonSerializationException("Incomplete position."); }
                    while (reader.TokenType == JsonToken.Comment);
                    if (name != "x" && name != "y" && name != "z") { reader.Skip(); continue; }
                    // The old JObject parser kept the last duplicate property before
                    // converting it. Defer conversion without allocating a token tree.
                    object coordinate = reader.TokenType == JsonToken.Null || reader.TokenType == JsonToken.Undefined ? null :
                        reader.TokenType == JsonToken.Integer || reader.TokenType == JsonToken.Float ||
                        reader.TokenType == JsonToken.String || reader.TokenType == JsonToken.Boolean ? reader.Value : InvalidCoordinate;
                    if (ReferenceEquals(coordinate, InvalidCoordinate)) reader.Skip();
                    if (name == "x") x = coordinate;
                    else if (name == "y") y = coordinate;
                    else z = coordinate;
                }
                throw new JsonSerializationException("Incomplete position.");
            }
            private static float Coordinate(object value)
            {
                if (ReferenceEquals(value, InvalidCoordinate)) throw new JsonSerializationException("Invalid position coordinate.");
                return value == null ? 0 : value is System.Numerics.BigInteger integer ? (float)integer :
                    Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
