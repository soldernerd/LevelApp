namespace LevelApp.Core.Models;

/// <summary>One primitive closure loop and its signed closure error in mm.</summary>
public record PrimitiveLoop(string[] NodeIds, double ClosureErrorMm);

public class SurfaceResult
{
    /// <summary>
    /// Best-fit surface heights in mm, keyed by node id.
    /// Full Grid: "col{c}_row{r}". Union Jack: "arm{Dir}_seg{k}" or "center".
    /// </summary>
    public Dictionary<string, double> NodeHeights { get; set; } = [];

    /// <summary>Peak-to-valley flatness deviation in mm.</summary>
    public double FlatnessValueMm { get; set; }

    /// <summary>Per-step residuals after the least-squares fit (one entry per step, in step order).</summary>
    public double[] Residuals { get; set; } = [];

    /// <summary>Indices into the step list whose |residual| > k*sigma.</summary>
    public List<int> FlaggedStepIndices { get; set; } = [];

    /// <summary>Outlier detection threshold multiplier (default 2.5).</summary>
    public double SigmaThreshold { get; set; } = 2.5;

    /// <summary>Residual RMS (sqrt of sum-of-squares / DOF) in mm.</summary>
    public double Sigma { get; set; }

    // ── Primitive closure loop statistics ────────────────────────────────────

    public PrimitiveLoop[] PrimitiveLoops   { get; set; } = [];
    public double          ClosureErrorMean   { get; set; }
    public double          ClosureErrorMedian { get; set; }
    public double          ClosureErrorMax    { get; set; }  // absolute value
    public double          ClosureErrorRms    { get; set; }
}
