using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.SurfacePlate.Strategies;

/// <summary>
/// The "reverse direction" option shared by the surface-plate strategies.
///
/// The normal walk travels East (rows), South (columns) and South-East / South-West (diagonals), so the
/// instrument is never turned by 180° between two steps — essential for a differential measurement. The
/// reversed walk is the exact point mirror of it: every step starts where the mirrored step would end and
/// faces the opposite way, the order of the steps is the same. The calculation is not affected.
/// </summary>
public static class WalkDirection
{
    /// <summary>Name of the optional boolean strategy parameter (absent = false).</summary>
    public const string ParameterName = "reverseDirection";

    /// <summary>Reads the flag from the strategy parameters; absent or unreadable means "normal direction".</summary>
    public static bool IsReversed(ObjectDefinition definition)
    {
        if (!definition.Parameters.TryGetValue(ParameterName, out var value) || value is null)
            return false;
        return value switch
        {
            bool b   => b,
            string s => bool.TryParse(s, out bool parsed) && parsed,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.True } => true,
            _        => false
        };
    }

    /// <summary>The orientation pointing the other way.</summary>
    public static Orientation Opposite(Orientation o) => o switch
    {
        Orientation.North     => Orientation.South,
        Orientation.South     => Orientation.North,
        Orientation.East      => Orientation.West,
        Orientation.West      => Orientation.East,
        Orientation.NorthEast => Orientation.SouthWest,
        Orientation.SouthWest => Orientation.NorthEast,
        Orientation.NorthWest => Orientation.SouthEast,
        Orientation.SouthEast => Orientation.NorthWest,
        _                     => o
    };
}
