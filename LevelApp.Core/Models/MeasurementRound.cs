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
    public CalculationParameters? CalculationParameters { get; set; }

    /// <summary>
    /// Returns <paramref name="originalSteps"/> with readings replaced where a
    /// matching <see cref="ReplacedStep"/> exists. All other step fields are copied
    /// verbatim, including NodeId, ToNodeId, and PassId.
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
                Reading         = replacedMap.TryGetValue(s.Index, out double nr) ? nr : s.Reading
            })
            .ToList();
    }
}
