namespace LevelApp.Core.Models;

public enum TaskType { AlongRail, Bridge }
public enum PassDirection { SinglePass, ForwardAndReturn }

/// <summary>
/// A measurement task within a Parallel Ways strategy.
/// The strategy produces an ordered list of tasks; each task produces a
/// contiguous block of <see cref="MeasurementStep"/>s.
/// </summary>
public class ParallelWaysTask
{
    public TaskType TaskType { get; set; }

    /// <summary>For AlongRail: the rail index. For Bridge: the first (from) rail index.</summary>
    public int RailIndexA { get; set; }

    /// <summary>For Bridge only: the second (to) rail index.</summary>
    public int RailIndexB { get; set; }

    public PassDirection PassDirection { get; set; } = PassDirection.SinglePass;

    /// <summary>Station spacing along the rail in mm.</summary>
    public double StepDistanceMm { get; set; }

    /// <summary>
    /// Number of intervals (steps) for this task.
    /// <c>StepCount = floor(railLength / StepDistanceMm)</c>.
    /// </summary>
    public int GetStepCount(double railLengthMm)
        => (int)Math.Floor(railLengthMm / StepDistanceMm);

    /// <summary>Number of stations = StepCount + 1.</summary>
    public int GetStationCount(double railLengthMm)
        => GetStepCount(railLengthMm) + 1;
}
