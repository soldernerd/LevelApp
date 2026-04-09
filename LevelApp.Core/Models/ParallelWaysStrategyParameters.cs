using System.Text.Json;
using System.Text.Json.Serialization;

namespace LevelApp.Core.Models;

public enum DriftCorrectionMethod { FirstStationAnchor, LinearDriftCorrection, LeastSquares }
public enum SolverMode { IndependentThenReconcile, GlobalLeastSquares }

/// <summary>
/// Task list and algorithm settings for a Parallel Ways measurement session.
/// Stored in <see cref="ObjectDefinition.Parameters"/> under the keys
/// "tasks", "driftCorrection", and "solverMode".
/// </summary>
public class ParallelWaysStrategyParameters
{
    public List<ParallelWaysTask> Tasks { get; set; } = [];
    public DriftCorrectionMethod DriftCorrection { get; set; } = DriftCorrectionMethod.LeastSquares;
    public SolverMode SolverMode { get; set; } = SolverMode.GlobalLeastSquares;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Extracts <see cref="ParallelWaysStrategyParameters"/> from an
    /// <see cref="ObjectDefinition.Parameters"/> dictionary.
    /// Handles both live <c>List&lt;ParallelWaysTask&gt;</c> values (set by the ViewModel)
    /// and <c>JsonElement</c> values (produced by <see cref="Serialization.ObjectValueConverter"/>
    /// on deserialisation).
    /// </summary>
    public static ParallelWaysStrategyParameters From(Dictionary<string, object> parameters)
    {
        var result = new ParallelWaysStrategyParameters();

        if (parameters.TryGetValue("tasks", out var tasksObj))
        {
            result.Tasks = tasksObj is JsonElement je
                ? je.Deserialize<List<ParallelWaysTask>>(_jsonOptions) ?? []
                : tasksObj as List<ParallelWaysTask> ?? [];
        }

        if (parameters.TryGetValue("driftCorrection", out var dc))
            result.DriftCorrection = Enum.Parse<DriftCorrectionMethod>(dc.ToString()!);

        if (parameters.TryGetValue("solverMode", out var sm))
            result.SolverMode = Enum.Parse<SolverMode>(sm.ToString()!);

        return result;
    }
}
