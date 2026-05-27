using LevelApp.Core.Geometry.Calculators;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

/// <summary>
/// Unit tests for <see cref="SequentialIntegrationCalculator"/>.
/// Exercises the sequential-integration + proportional-closure path independently
/// of the least-squares solver, using FullGridStrategy as the geometry provider.
/// </summary>
public class SequentialIntegrationCalculatorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ObjectDefinition Def(int cols, int rows,
        double widthMm = 300.0, double heightMm = 300.0) => new()
    {
        GeometryModuleId = "SurfacePlate",
        Parameters = new Dictionary<string, object>
        {
            ["columnsCount"] = cols,
            ["rowsCount"]    = rows,
            ["widthMm"]      = widthMm,
            ["heightMm"]     = heightMm
        }
    };

    private static CalculationParameters Params(double sigmaThreshold = 2.5,
        bool autoExclude = true) => new()
    {
        MethodId            = "SequentialIntegration",
        SigmaThreshold      = sigmaThreshold,
        AutoExcludeOutliers = autoExclude
    };

    private static SequentialIntegrationCalculator Calculator() =>
        new(new FullGridStrategy());

    /// <summary>
    /// Generates steps whose readings are derived from <paramref name="trueHeights"/>,
    /// optionally injecting noise on one step.
    /// </summary>
    private static List<MeasurementStep> BuildSteps(
        ObjectDefinition def,
        double[][] trueHeights,
        int noiseOnStepIndex = -1,
        double noiseValue    = 0.0)
    {
        int cols     = Convert.ToInt32(def.Parameters["columnsCount"]);
        int rows     = Convert.ToInt32(def.Parameters["rowsCount"]);
        double stepX = Convert.ToDouble(def.Parameters["widthMm"])  / (cols - 1);
        double stepY = Convert.ToDouble(def.Parameters["heightMm"]) / (rows - 1);

        var steps = new FullGridStrategy().GenerateSteps(def).ToList();
        foreach (var step in steps)
        {
            double stepLen = step.Orientation is Orientation.East or Orientation.West
                ? stepX : stepY;

            (int toRow, int toCol) = step.Orientation switch
            {
                Orientation.East  => (step.GridRow,     step.GridCol + 1),
                Orientation.West  => (step.GridRow,     step.GridCol - 1),
                Orientation.South => (step.GridRow + 1, step.GridCol),
                Orientation.North => (step.GridRow - 1, step.GridCol),
                _ => throw new InvalidOperationException()
            };

            double delta = trueHeights[toRow][toCol] - trueHeights[step.GridRow][step.GridCol];
            step.Reading = delta * 1000.0 / stepLen;   // mm → mm/m
        }

        if (noiseOnStepIndex >= 0)
            steps[noiseOnStepIndex].Reading += noiseValue;

        return steps;
    }

    private static double[][] ZeroHeights(int rows, int cols) =>
        Enumerable.Range(0, rows).Select(_ => new double[cols]).ToArray();

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FlatSurface_ZeroReadings_ProducesZeroFlatnessAndSigma()
    {
        var def   = Def(3, 3);
        var steps = BuildSteps(def, ZeroHeights(3, 3));

        var result = Calculator().Calculate(steps, def, Params());

        Assert.Equal(0.0, result.FlatnessValueMm, precision: 9);
        Assert.Equal(0.0, result.Sigma,           precision: 9);
        Assert.Empty(result.FlaggedStepIndices);
    }

    [Fact]
    public void FlatSurface_AllNodeHeightsAreZero()
    {
        var def   = Def(3, 3);
        var steps = BuildSteps(def, ZeroHeights(3, 3));

        var result = Calculator().Calculate(steps, def, Params());

        Assert.All(result.NodeHeights.Values, h => Assert.Equal(0.0, h, precision: 9));
    }

    [Fact]
    public void NodeCount_MatchesGridSize()
    {
        // 3×3 grid → 9 nodes
        var def   = Def(3, 3);
        var steps = BuildSteps(def, ZeroHeights(3, 3));

        var result = Calculator().Calculate(steps, def, Params());

        Assert.Equal(9, result.NodeHeights.Count);
    }

    [Fact]
    public void UniformSlope_FlatnessIsPositive()
    {
        // h[row][col] = col × 0.1 mm — pure East tilt.
        // The SI approximation will not produce the exact flatness of 0.2 mm,
        // but it must be strictly positive for a non-flat input.
        var def = Def(3, 3, widthMm: 200.0, heightMm: 200.0);
        double[][] trueH = Enumerable.Range(0, 3)
            .Select(r => Enumerable.Range(0, 3).Select(c => c * 0.1).ToArray())
            .ToArray();

        var result = Calculator().Calculate(BuildSteps(def, trueH), def, Params());

        Assert.True(result.FlatnessValueMm > 0,
            $"Expected positive flatness, got {result.FlatnessValueMm}");
    }

    [Fact]
    public void ResidualsLength_MatchesStepCount()
    {
        var def   = Def(3, 3);
        var steps = BuildSteps(def, ZeroHeights(3, 3));

        var result = Calculator().Calculate(steps, def, Params());

        Assert.Equal(steps.Count, result.Residuals.Length);
    }

    [Fact]
    public void LargeNoise_StepIsFlagged()
    {
        var def   = Def(4, 4);
        // Inject 100 mm/m noise on step 0 with a tight sigma threshold
        var steps = BuildSteps(def, ZeroHeights(4, 4),
            noiseOnStepIndex: 0, noiseValue: 100.0);

        var result = Calculator().Calculate(steps, def, Params(sigmaThreshold: 0.5));

        Assert.NotEmpty(result.FlaggedStepIndices);
    }

    [Fact]
    public void AutoExcludeOff_ProducesNoFlaggedSteps()
    {
        var def   = Def(3, 3);
        var steps = BuildSteps(def, ZeroHeights(3, 3),
            noiseOnStepIndex: 2, noiseValue: 5.0);

        var result = Calculator().Calculate(steps, def, Params(autoExclude: false));

        Assert.Empty(result.FlaggedStepIndices);
    }

    [Fact]
    public void EmptySteps_ThrowsArgumentException()
    {
        var def = Def(3, 3);

        Assert.Throws<ArgumentException>(() =>
            Calculator().Calculate([], def, Params()));
    }

    [Fact]
    public void FlatSurface_AgreesWithLeastSquares_OnFlatnessAndNodeCount()
    {
        // On zero-noise data both calculators must agree on flatness (0) and node count.
        var def   = Def(3, 3);
        var steps = BuildSteps(def, ZeroHeights(3, 3));

        var si = new SequentialIntegrationCalculator(new FullGridStrategy())
            .Calculate(steps, def, Params());
        var ls = new LeastSquaresCalculator(new FullGridStrategy())
            .Calculate(steps, def, Params());

        Assert.Equal(ls.FlatnessValueMm,   si.FlatnessValueMm,   precision: 9);
        Assert.Equal(ls.NodeHeights.Count, si.NodeHeights.Count);
    }
}
