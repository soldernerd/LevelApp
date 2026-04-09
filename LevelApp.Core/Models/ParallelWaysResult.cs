namespace LevelApp.Core.Models;

/// <summary>
/// Straightness profile for a single rail after best-fit line removal.
/// </summary>
public class RailProfile
{
    public int RailIndex { get; set; }

    /// <summary>Deviation from best-fit line in mm, one entry per station.</summary>
    public double[] HeightProfileMm { get; set; } = [];

    /// <summary>Absolute axial position of each station in mm.</summary>
    public double[] StationPositionsMm { get; set; } = [];

    /// <summary>Peak-to-valley of <see cref="HeightProfileMm"/> in mm.</summary>
    public double StraightnessValueMm { get; set; }
}

/// <summary>
/// Height-difference profile between two rails at common stations.
/// </summary>
public class ParallelismProfile
{
    public int RailIndexA { get; set; }
    public int RailIndexB { get; set; }

    /// <summary>Height difference h_B[s] − h_A[s] in mm at each station.</summary>
    public double[] DeviationMm { get; set; } = [];

    /// <summary>Absolute axial position of each station in mm.</summary>
    public double[] StationPositionsMm { get; set; } = [];

    /// <summary>Peak-to-valley of <see cref="DeviationMm"/> in mm.</summary>
    public double ParallelismValueMm { get; set; }
}

/// <summary>
/// Result produced by <see cref="LevelApp.Core.Geometry.ParallelWays.ParallelWaysCalculator"/>.
/// </summary>
public class ParallelWaysResult
{
    public List<RailProfile>        RailProfiles        { get; set; } = [];
    public List<ParallelismProfile> ParallelismProfiles { get; set; } = [];

    /// <summary>Per-step residuals (one per step, in step order).</summary>
    public double[] Residuals { get; set; } = [];

    /// <summary>Indices of steps whose |residual| > sigma_threshold × σ.</summary>
    public int[] FlaggedStepIndices { get; set; } = [];

    public double SigmaThreshold { get; set; } = 2.5;

    /// <summary>RMS of all residuals (no DOF correction).</summary>
    public double ResidualRms { get; set; }
}
