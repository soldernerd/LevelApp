namespace LevelApp.Core.Models;

public class SurfaceResult
{
    /// <summary>
    /// Best-fit surface heights in mm. Indexed [row][col].
    /// Jagged array for straightforward JSON serialisation.
    /// </summary>
    public double[][] HeightMapMm { get; set; } = [];

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
}
