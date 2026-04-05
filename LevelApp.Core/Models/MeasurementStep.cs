namespace LevelApp.Core.Models;

public enum Orientation { North, South, East, West }

public class MeasurementStep
{
    public int Index { get; set; }
    public int GridCol { get; set; }
    public int GridRow { get; set; }
    public Orientation Orientation { get; set; }
    public string InstructionText { get; set; } = string.Empty;

    /// <summary>Null until the operator records a reading (mm/m).</summary>
    public double? Reading { get; set; }
}
