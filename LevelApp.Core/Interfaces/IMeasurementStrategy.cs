using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// Generates the ordered sequence of guided measurement steps for a given object definition.
/// A strategy only produces the step list — it knows nothing about calculation.
/// </summary>
public interface IMeasurementStrategy
{
    string StrategyId { get; }
    string DisplayName { get; }

    IReadOnlyList<MeasurementStep> GenerateSteps(ObjectDefinition definition);

    /// <summary>
    /// Physical position of the from-node of <paramref name="step"/> in mm,
    /// origin at plate bottom-left (0, 0).
    /// </summary>
    (double X, double Y) GetNodePosition(MeasurementStep step, ObjectDefinition definition);

    /// <summary>Physical position of the to-node of <paramref name="step"/> in mm.</summary>
    (double X, double Y) GetToNodePosition(MeasurementStep step, ObjectDefinition definition);

    /// <summary>
    /// Returns ordered node-ID sequences for every primitive closure loop.
    /// Each inner list defines one loop; consecutive IDs are connected by a single
    /// measured step (the last ID wraps back to the first).
    /// </summary>
    IReadOnlyList<IReadOnlyList<string>> GetPrimitiveLoopNodeIds(ObjectDefinition definition);
}
