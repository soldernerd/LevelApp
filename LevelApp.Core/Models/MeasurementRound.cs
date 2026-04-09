namespace LevelApp.Core.Models;

/// <summary>
/// The initial measurement round for a session: the full ordered step list
/// and the result computed from those readings.
/// </summary>
public class MeasurementRound
{
    public DateTime? CompletedAt { get; set; }
    public List<MeasurementStep> Steps { get; set; } = [];

    /// <summary>Surface Plate result (null for Parallel Ways sessions).</summary>
    public SurfaceResult? Result { get; set; }

    /// <summary>Parallel Ways result (null for Surface Plate sessions).</summary>
    public ParallelWaysResult? ParallelWaysResult { get; set; }

    public CalculationParameters? CalculationParameters { get; set; }

    /// <summary>
    /// Returns <paramref name="originalSteps"/> with readings replaced where a
    /// matching <see cref="ReplacedStep"/> exists. All other step fields are copied
    /// verbatim, including NodeId, ToNodeId, PassId, and PassPhase.
    /// </summary>
    public static List<MeasurementStep> MergeWithReplacements(
        IReadOnlyList<MeasurementStep> originalSteps,
        IEnumerable<ReplacedStep> replacements)
    {
        var replacedMap = new Dictionary<int, double>();
        foreach (var r in replacements)
            replacedMap[r.OriginalStepIndex] = r.Reading;

        return originalSteps
            .Select(s => new MeasurementStep
            {
                Index           = s.Index,
                GridCol         = s.GridCol,
                GridRow         = s.GridRow,
                Orientation     = s.Orientation,
                InstructionText = s.InstructionText,
                NodeId          = s.NodeId,
                ToNodeId        = s.ToNodeId,
                PassId          = s.PassId,
                PassPhase       = s.PassPhase,
                Reading         = replacedMap.TryGetValue(s.Index, out double nr) ? nr : s.Reading
            })
            .ToList();
    }
}
