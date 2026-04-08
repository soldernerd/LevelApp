using System.Text.Json;
using System.Text.Json.Serialization;
using LevelApp.Core.Models;

namespace LevelApp.Core.Serialization;

/// <summary>
/// Reads <see cref="Orientation"/> from either its string name
/// ("North", "South", "East", "West", "NorthEast", …) or its legacy integer
/// representation (North=0, South=1, East=2, West=3).
/// Always writes the string name so that all future saves use the string format.
/// </summary>
public sealed class OrientationConverter : JsonConverter<Orientation>
{
    public override Orientation Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // ── String format (current) ───────────────────────────────────────────
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() switch
            {
                "North"     => Orientation.North,
                "South"     => Orientation.South,
                "East"      => Orientation.East,
                "West"      => Orientation.West,
                "NorthEast" => Orientation.NorthEast,
                "NorthWest" => Orientation.NorthWest,
                "SouthEast" => Orientation.SouthEast,
                "SouthWest" => Orientation.SouthWest,
                var s       => throw new JsonException($"Unknown Orientation value '{s}'.")
            };
        }

        throw new JsonException(
            $"Unexpected token '{reader.TokenType}' while reading Orientation.");
    }

    public override void Write(
        Utf8JsonWriter writer, Orientation value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
