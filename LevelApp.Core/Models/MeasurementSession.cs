using System.Text.Json.Serialization;

namespace LevelApp.Core.Models;

public class MeasurementSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = string.Empty;
    public DateTime TakenAt { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string InstrumentId { get; set; } = string.Empty;
    public string StrategyId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public MeasurementRound InitialRound { get; set; } = new();
    public List<CorrectionRound> Corrections { get; set; } = [];

    /// <summary>
    /// The geometry parameters this measurement was taken with (grid columns / rows, Union Jack
    /// segments / rings, Parallel Ways rails ...), laid over the project's object definition. A project
    /// can hold measurements with different strategies and sizes, so each measurement that differs
    /// from the project's own definition carries its parameters here. Null = the project's definition.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? StrategyParameters { get; set; }

    /// <summary>
    /// True while the measurement has steps but no result yet: it was started and not finished
    /// (or all readings are in but the calculation never ran). Such a session can be resumed.
    /// </summary>
    public bool IsUnfinished() =>
        InitialRound.Steps.Count > 0
        && InitialRound.Result is null
        && InitialRound.ParallelWaysResult is null;

    /// <summary>How many steps of the initial round already have a reading.</summary>
    public int ReadingCount() => InitialRound.Steps.Count(s => s.Reading.HasValue);
}
