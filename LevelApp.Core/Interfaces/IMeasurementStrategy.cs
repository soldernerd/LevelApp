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
}
