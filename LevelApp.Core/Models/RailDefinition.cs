namespace LevelApp.Core.Models;

/// <summary>
/// Defines the geometry of a single rail (way) within a Parallel Ways measurement object.
/// All offsets are zero for the reference rail.
/// </summary>
public class RailDefinition
{
    /// <summary>User-defined label, e.g. "Front", "Rear".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Length of the rail in mm along its travel axis.</summary>
    public double LengthMm { get; set; }

    /// <summary>Offset from the reference rail start along the travel axis (mm).</summary>
    public double AxialOffsetMm { get; set; }

    /// <summary>Perpendicular distance from the reference rail (mm).</summary>
    public double LateralSeparationMm { get; set; }

    /// <summary>Nominal height difference from the reference rail (mm).</summary>
    public double VerticalOffsetMm { get; set; }
}
