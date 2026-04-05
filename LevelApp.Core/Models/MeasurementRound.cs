namespace LevelApp.Core.Models;

/// <summary>
/// The initial measurement round for a session: the full ordered step list
/// and the result computed from those readings.
/// </summary>
public class MeasurementRound
{
    public DateTime? CompletedAt { get; set; }
    public List<MeasurementStep> Steps { get; set; } = [];
    public SurfaceResult? Result { get; set; }
}
