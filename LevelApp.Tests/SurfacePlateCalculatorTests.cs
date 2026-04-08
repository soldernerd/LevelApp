using LevelApp.Core.Geometry.Calculators;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

public class LeastSquaresCalculatorTests
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

    private static CalculationParameters Params(double sigmaThreshold = 2.5, bool autoExclude = true) => new()
    {
        MethodId            = "LeastSquares",
        SigmaThreshold      = sigmaThreshold,
        AutoExcludeOutliers = autoExclude
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

    private static double NodeHeight(SurfaceResult result, int col, int row)
        => result.NodeHeights[$"col{col}_row{row}"];

    private static LeastSquaresCalculator Calculator() =>
        new(new FullGridStrategy());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FlatSurface_ZeroReadings_ProducesZeroHeightsAndFlatness()
    {
        var def   = Def(3, 3);
        var round = BuildRound(def, ZeroHeights(3, 3));

        var result = Calculator().Calculate(round.Steps, def, Params());

        Assert.Equal(0.0, result.FlatnessValueMm, precision: 9);
        Assert.All(result.NodeHeights.Values, h => Assert.Equal(0.0, h, precision: 9));
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

        var result = Calculator().Calculate(BuildRound(def, trueH).Steps, def, Params());

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                Assert.Equal(trueH[r][c], NodeHeight(result, c, r), precision: 9);

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

        var result = Calculator().Calculate(BuildRound(def, trueH).Steps, def, Params());

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                Assert.Equal(trueH[r][c], NodeHeight(result, c, r), precision: 9);

        Assert.Equal(0.30, result.FlatnessValueMm, precision: 9);
    }

    [Fact]
    public void LargeNoise_StepWithMaxResidualIsFlagged()
    {
        // After injecting large noise on step 0, the step with the biggest absolute
        // residual must be flagged when using a threshold k < √(DOF/M).
        var def   = Def(4, 4);
        var round = BuildRound(def, ZeroHeights(4, 4), noiseOnStepIndex: 0, noiseValue: 50.0);

        var result = new LeastSquaresCalculator(new FullGridStrategy())
            .Calculate(round.Steps, def, Params(sigmaThreshold: 0.5));

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

        var result = Calculator().Calculate(round.Steps, def, Params());

        Assert.Empty(result.FlaggedStepIndices);
    }

    [Fact]
    public void ResidualsLength_MatchesStepCount()
    {
        var def   = Def(3, 3);
        var round = BuildRound(def, ZeroHeights(3, 3));

        var result = Calculator().Calculate(round.Steps, def, Params());

        Assert.Equal(round.Steps.Count, result.Residuals.Length);
    }

    [Fact]
    public void FlatnessValue_EqualsNodeHeightPeakToValley()
    {
        var def = Def(3, 3);
        double[][] trueH = Enumerable.Range(0, 3)
            .Select(r => Enumerable.Range(0, 3).Select(c => c * 0.07 + r * 0.03).ToArray())
            .ToArray();

        var result = Calculator().Calculate(BuildRound(def, trueH).Steps, def, Params());

        double expectedFlatness = result.NodeHeights.Values.Max() - result.NodeHeights.Values.Min();
        Assert.Equal(expectedFlatness, result.FlatnessValueMm, precision: 9);
    }

    [Fact]
    public void SigmaThreshold_ControlsOutlierSensitivity()
    {
        // Mild noise on one step: tight threshold flags it, generous threshold does not.
        var def   = Def(3, 3);
        var round = BuildRound(def, ZeroHeights(3, 3), noiseOnStepIndex: 2, noiseValue: 5.0);

        var resultTight = new LeastSquaresCalculator(new FullGridStrategy())
            .Calculate(round.Steps, def, Params(sigmaThreshold: 0.1));
        var resultLoose = new LeastSquaresCalculator(new FullGridStrategy())
            .Calculate(round.Steps, def, Params(sigmaThreshold: 100.0));

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
            Calculator().Calculate(round.Steps, def, Params()));
    }

    [Fact]
    public void EmptySteps_ThrowsArgumentException()
    {
        var def = Def(3, 3);

        Assert.Throws<ArgumentException>(() =>
            Calculator().Calculate([], def, Params()));
    }

    [Fact]
    public void AutoExcludeOff_ProducesNoFlaggedSteps()
    {
        var def   = Def(3, 3);
        var round = BuildRound(def, ZeroHeights(3, 3), noiseOnStepIndex: 2, noiseValue: 5.0);

        var result = Calculator().Calculate(round.Steps, def, Params(autoExclude: false));

        Assert.Empty(result.FlaggedStepIndices);
    }
}
