using System.Text.Json;
using System.Text.Json.Serialization;

namespace LevelApp.Core.Serialization;

/// <summary>
/// Converts <c>Dictionary&lt;string, object&gt;</c> values so that JSON numbers,
/// strings, and booleans round-trip as proper .NET primitives rather than
/// <see cref="JsonElement"/> instances.
/// </summary>
public sealed class ObjectValueConverter : JsonConverter<object>
{
    public override object? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.True   => true,
            JsonTokenType.False  => false,
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.Null   => null!,
            JsonTokenType.Number =>
                reader.TryGetInt32(out int i)  ? (object)i :
                reader.TryGetInt64(out long l)  ? (object)l :
                                                  (object)reader.GetDouble(),
            _                    => JsonSerializer.Deserialize<JsonElement>(ref reader, options)
        };

    public override void Write(
        Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
