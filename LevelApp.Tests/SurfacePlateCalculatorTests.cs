using LevelApp.Core.Geometry.SurfacePlate;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

public class SurfacePlateCalculatorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ObjectDefinition Def(int cols, int rows,
        double widthMm = 200.0, double heightMm = 200.0) => new()
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

    /// <summary>
    /// Builds a MeasurementRound whose step readings are derived from <paramref name="trueHeights"/>.
    /// Optionally adds <paramref name="noiseValue"/> (mm/m) to the reading of one specific step.
    /// </summary>
    private static MeasurementRound BuildRound(
        ObjectDefinition def,
        double[][] trueHeights,
        int noiseOnStepIndex = -1,
        double noiseValue    = 0.0)
    {
        int cols    = Convert.ToInt32(def.Parameters["columnsCount"]);
        int rows    = Convert.ToInt32(def.Parameters["rowsCount"]);
        double stepLenX = Convert.ToDouble(def.Parameters["widthMm"])  / (cols - 1);
        double stepLenY = Convert.ToDouble(def.Parameters["heightMm"]) / (rows - 1);

        var steps = new FullGridStrategy().GenerateSteps(def).ToList();

        foreach (var step in steps)
        {
            double stepLen = step.Orientation is Orientation.East or Orientation.West
                ? stepLenX : stepLenY;

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

        return new MeasurementRound { Steps = steps };
    }

    private static double[][] ZeroHeights(int rows, int cols) =>
        Enumerable.Range(0, rows).Select(_ => new double[cols]).ToArray();

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FlatSurface_ZeroReadings_ProducesZeroHeightsAndFlatness()
    {
        var def   = Def(3, 3);
        var round = BuildRound(def, ZeroHeights(3, 3));

        var result = new SurfacePlateCalculator(def).Calculate(round);

        Assert.Equal(0.0, result.FlatnessValueMm, precision: 9);
        Assert.All(result.HeightMapMm.SelectMany(row => row),
            h => Assert.Equal(0.0, h, precision: 9));
        Assert.Empty(result.FlaggedStepIndices);
    }

    [Fact]
    public void UniformEastSlope_RecoversTrueHeights()
    {
        // h[row][col] = col * 0.1 mm — uniform East slope, no North-South component.
        // stepLen = 200/(3-1) = 100 mm; readings = ±1.0 mm/m in row pass, 0 in col pass.
        int cols = 3, rows = 3;
        var def  = Def(cols, rows, widthMm: 200.0, heightMm: 200.0);
        double[][] trueH = Enumerable.Range(0, rows)
            .Select(r => Enumerable.Range(0, cols).Select(c => c * 0.1).ToArray())
            .ToArray();

        var result = new SurfacePlateCalculator(def).Calculate(BuildRound(def, trueH));

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                Assert.Equal(trueH[r][c], result.HeightMapMm[r][c], precision: 9);

        Assert.Equal(0.2, result.FlatnessValueMm, precision: 9);
        Assert.Empty(result.FlaggedStepIndices);
    }

    [Fact]
    public void DiagonalSlope_RecoversTrueHeights()
    {
        // h[row][col] = col * 0.1 + row * 0.05 mm — slope in both axes.
        // max at (row=2, col=2) = 0.30 mm; reference h[0][0] = 0; flatness = 0.30 mm.
        int cols = 3, rows = 3;
        var def  = Def(cols, rows, widthMm: 200.0, heightMm: 200.0);
        double[][] trueH = Enumerable.Range(0, rows)
            .Select(r => Enumerable.Range(0, cols).Select(c => c * 0.1 + r * 0.05).ToArray())
            .ToArray();

        var result = new SurfacePlateCalculator(def).Calculate(BuildRound(def, trueH));

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                Assert.Equal(trueH[r][c], result.HeightMapMm[r][c], precision: 9);

        Assert.Equal(0.30, result.FlatnessValueMm, precision: 9);
    }

    [Fact]
    public void LargeNoise_StepWithMaxResidualIsFlagged()
    {
        // After injecting large noise on step 0, the step with the biggest absolute
        // residual must be flagged when using a threshold k < √(DOF/M).
        //
        // Proof: r_max ≥ σ·√(DOF/M) = σ·√(9/24) ≈ σ·0.61  (Cauchy-Schwarz)
        // With k = 0.5 < 0.61 this is mathematically guaranteed regardless of
        // how the error is distributed across the network.
        var def   = Def(4, 4);
        var round = BuildRound(def, ZeroHeights(4, 4), noiseOnStepIndex: 0, noiseValue: 50.0);

        var result = new SurfacePlateCalculator(def, sigmaThreshold: 0.5).Calculate(round);

        int worstListIdx = result.Residuals
            .Select((r, i) => (AbsR: Math.Abs(r), i))
            .MaxBy(x => x.AbsR).i;
        int worstStepIndex = round.Steps[worstListIdx].Index;

        Assert.Contains(worstStepIndex, result.FlaggedStepIndices);
    }

    [Fact]
    public void CleanData_ProducesNoFlaggedSteps()
    {
        var def   = Def(4, 4);
        var round = BuildRound(def, ZeroHeights(4, 4));

        var result = new SurfacePlateCalculator(def).Calculate(round);

        Assert.Empty(result.FlaggedStepIndices);
    }

    [Fact]
    public void ResidualsLength_MatchesStepCount()
    {
        var def   = Def(3, 3);
        var round = BuildRound(def, ZeroHeights(3, 3));

        var result = new SurfacePlateCalculator(def).Calculate(round);

        Assert.Equal(round.Steps.Count, result.Residuals.Length);
    }

    [Fact]
    public void FlatnessValue_EqualsHeightMapPeakToValley()
    {
        var def = Def(3, 3);
        double[][] trueH = Enumerable.Range(0, 3)
            .Select(r => Enumerable.Range(0, 3).Select(c => c * 0.07 + r * 0.03).ToArray())
            .ToArray();

        var result = new SurfacePlateCalculator(def).Calculate(BuildRound(def, trueH));

        double expectedFlatness = result.HeightMapMm.SelectMany(r => r).Max()
                                - result.HeightMapMm.SelectMany(r => r).Min();
        Assert.Equal(expectedFlatness, result.FlatnessValueMm, precision: 9);
    }

    [Fact]
    public void SigmaThreshold_ControlsOutlierSensitivity()
    {
        // Mild noise on one step: tight threshold flags it, generous threshold does not.
        var def   = Def(3, 3);
        var round = BuildRound(def, ZeroHeights(3, 3), noiseOnStepIndex: 2, noiseValue: 5.0);

        var resultTight = new SurfacePlateCalculator(def, sigmaThreshold: 0.1).Calculate(round);
        var resultLoose = new SurfacePlateCalculator(def, sigmaThreshold: 100.0).Calculate(round);

        Assert.NotEmpty(resultTight.FlaggedStepIndices);
        Assert.Empty(resultLoose.FlaggedStepIndices);
    }

    [Fact]
    public void MissingReading_ThrowsInvalidOperationException()
    {
        var def   = Def(3, 3);
        var round = BuildRound(def, ZeroHeights(3, 3));
        round.Steps[3].Reading = null;

        Assert.Throws<InvalidOperationException>(() =>
            new SurfacePlateCalculator(def).Calculate(round));
    }

    [Fact]
    public void EmptyRound_ThrowsArgumentException()
    {
        var def   = Def(3, 3);
        var round = new MeasurementRound();   // no steps

        Assert.Throws<ArgumentException>(() =>
            new SurfacePlateCalculator(def).Calculate(round));
    }
}
