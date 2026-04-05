namespace LevelApp.Core.Models;

/// <summary>
/// A correction pass that replaces readings for specific flagged steps.
/// Original readings in the InitialRound (or prior CorrectionRound) are never modified.
/// </summary>
public class CorrectionRound
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TriggeredAt { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<ReplacedStep> ReplacedSteps { get; set; } = [];
    public SurfaceResult? Result { get; set; }
}

/// <summary>A single re-measured step within a correction round.</summary>
public class ReplacedStep
{
    public int OriginalStepIndex { get; set; }
    public double Reading { get; set; }
}
