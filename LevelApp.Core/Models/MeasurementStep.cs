using System.Text.Json.Serialization;
using LevelApp.Core.Serialization;

namespace LevelApp.Core.Models;

[JsonConverter(typeof(OrientationConverter))]
public enum Orientation
{
    North, South, East, West,
    NorthEast, NorthWest, SouthEast, SouthWest
}

public class MeasurementStep
{
    public int Index { get; set; }
    public int GridCol { get; set; }
    public int GridRow { get; set; }
    public Orientation Orientation { get; set; }
    public string InstructionText { get; set; } = string.Empty;

    /// <summary>Null until the operator records a reading (mm/m).</summary>
    public double? Reading { get; set; }

    /// <summary>
    /// Identity of the from-node. Full Grid: "col{c}_row{r}".
    /// Union Jack: "arm{Dir}_seg{k}" or "center".
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Identity of the to-node. Same naming convention as NodeId.</summary>
    public string ToNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Pass grouping — set by the strategy; not serialized.
    /// Consecutive steps with the same PassId belong to the same measurement pass.
    /// </summary>
    [JsonIgnore]
    public int PassId { get; set; }
}
