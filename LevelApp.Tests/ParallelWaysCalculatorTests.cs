using LevelApp.Core.Geometry.ParallelWays;
using LevelApp.Core.Geometry.ParallelWays.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

public class ParallelWaysCalculatorTests
{
    private readonly ParallelWaysCalculator _sut = new();
    private readonly ParallelWaysStrategy   _strategy = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ObjectDefinition Def(
        List<RailDefinition>   rails,
        List<ParallelWaysTask> tasks,
        int                    refIdx     = 0,
        SolverMode             solverMode = SolverMode.GlobalLeastSquares,
        DriftCorrectionMethod  drift      = DriftCorrectionMethod.LeastSquares)
    => new()
    {
        GeometryModuleId = "ParallelWays",
        Parameters = new Dictionary<string, object>
        {
            ["orientation"]        = WaysOrientation.Horizontal.ToString(),
            ["referenceRailIndex"] = refIdx,
            ["rails"]              = rails,
            ["tasks"]              = tasks,
            ["driftCorrection"]    = drift.ToString(),
            ["solverMode"]         = solverMode.ToString()
        }
    };

    private static RailDefinition Rail(string label, double length,
        double lateral = 0, double vertical = 0)
    => new()
    {
        Label               = label,
        LengthMm            = length,
        LateralSeparationMm = lateral,
        VerticalOffsetMm    = vertical
    };

    private static ParallelWaysTask AlongRail(int rail, double stepDist,
        PassDirection pass = PassDirection.SinglePass)
    => new()
    {
        TaskType       = TaskType.AlongRail,
        RailIndexA     = rail,
        StepDistanceMm = stepDist,
        PassDirection  = pass
    };

    private static CalculationParameters CalcParams(
        double sigma = 3.0, bool autoExclude = false)
    => new()
    {
        MethodId            = "LeastSquares",
        SigmaThreshold      = sigma,
        AutoExcludeOutliers = autoExclude
    };

    /// <summary>
    /// Sets readings on a step list such that the height profile of rail <paramref name="rail"/>
    /// follows <paramref name="heights"/> (height[s] = height at station s).
    /// Reading = (h[to] - h[from]) / stepLenMm × 1000 (in mm/m).
    /// </summary>
    private static void SetReadingsFromHeights(
        IReadOnlyList<MeasurementStep> steps,
        int                            rail,
        double[]                       heights,
        double                         stepDist)
    {
        foreach (var step in steps)
        {
            var (rFrom, sFrom) = ParallelWaysStrategy.ParseNodeId(step.NodeId);
            var (rTo,   sTo)   = ParallelWaysStrategy.ParseNodeId(step.ToNodeId);

            if (rFrom == rail && rTo == rail)
            {
                double hFrom = sFrom < heights.Length ? heights[sFrom] : 0;
                double hTo   = sTo   < heights.Length ? heights[sTo]   : 0;
                step.Reading = (hTo - hFrom) / stepDist * 1000.0;  // mm/m → reading
            }
        }

        // Set any steps not assigned above to 0
        foreach (var step in steps.Where(s => !s.Reading.HasValue))
            step.Reading = 0.0;
    }

    // ── Flat rails ────────────────────────────────────────────────────────────

    [Fact]
    public void AllZeroReadings_StraightnessIsZero()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRail(0, 200), AlongRail(1, 200)]);

        var steps = _strategy.GenerateSteps(def).ToList();
        foreach (var s in steps) s.Reading = 0.0;

        var result = _sut.Calculate(steps, def, CalcParams());

        Assert.Equal(2, result.RailProfiles.Count);
        Assert.All(result.RailProfiles, p =>
            Assert.Equal(0.0, p.StraightnessValueMm, precision: 9));
    }

    [Fact]
    public void AllZeroReadings_ParallelismIsZero()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRail(0, 200), AlongRail(1, 200)]);

        var steps = _strategy.GenerateSteps(def).ToList();
        foreach (var s in steps) s.Reading = 0.0;

        var result = _sut.Calculate(steps, def, CalcParams());

        Assert.Single(result.ParallelismProfiles);
        Assert.Equal(0.0, result.ParallelismProfiles[0].ParallelismValueMm, precision: 9);
    }

    // ── Tilt (slope) ──────────────────────────────────────────────────────────

    [Fact]
    public void ConstantSlope_StraightnessIsZero_GlobalLeastSquares()
    {
        // Rail 0: constant slope 1 µm/m = 0.001 mm/m
        // With 200 mm step: reading = 0.001 mm/m
        const double slope = 1.0;   // µm/m
        const double dist  = 200.0; // mm
        int nIntervals = 5;         // 1000/200

        // Heights at each station: h[s] = slope * s * dist / 1e6  (in mm)
        double[] heights = Enumerable.Range(0, nIntervals + 1)
            .Select(s => slope * s * dist / 1e6)
            .ToArray();

        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRail(0, dist), AlongRail(1, dist)]);

        var steps = _strategy.GenerateSteps(def).ToList();
        SetReadingsFromHeights(steps, 0, heights, dist);
        SetReadingsFromHeights(steps, 1, new double[nIntervals + 1], dist); // rail 1 flat

        var result = _sut.Calculate(steps, def, CalcParams());

        // A perfectly linear profile has zero straightness after best-fit line removal
        var rail0Profile = result.RailProfiles.First(p => p.RailIndex == 0);
        Assert.Equal(0.0, rail0Profile.StraightnessValueMm, precision: 6);
    }

    // ── Curved profile ────────────────────────────────────────────────────────

    [Fact]
    public void CurvedProfile_StraightnessMatchesExpected()
    {
        // Rail 0: heights follow a parabola h[s] = s*(5-s) * 1e-6 mm
        // Peak at station 2.5 (between sta2 and sta3), value ~6.25e-6 mm
        // Best-fit line removal leaves a residual.
        // We just verify that straightness > 0 and the correct profile length.

        const double dist = 200.0;
        int nSta = 6; // 1000/200 + 1

        double[] heights = Enumerable.Range(0, nSta)
            .Select(s => s * (5.0 - s) * 1e-6)  // mm
            .ToArray();

        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRail(0, dist), AlongRail(1, dist)]);

        var steps = _strategy.GenerateSteps(def).ToList();
        SetReadingsFromHeights(steps, 0, heights, dist);
        SetReadingsFromHeights(steps, 1, new double[nSta], dist);

        var result = _sut.Calculate(steps, def, CalcParams());

        var profile = result.RailProfiles.First(p => p.RailIndex == 0);
        Assert.Equal(nSta, profile.HeightProfileMm.Length);
        Assert.True(profile.StraightnessValueMm > 0,
            $"Expected positive straightness, got {profile.StraightnessValueMm}");
    }

    // ── Parallelism ───────────────────────────────────────────────────────────

    [Fact]
    public void ParallelRailsWithOffset_ParallelismIsZero()
    {
        // Rail 0 and Rail 1 have identical height profiles (parallel)
        // → Parallelism should be zero.

        const double dist = 200.0;
        int nSta = 6;

        double[] heights = Enumerable.Range(0, nSta)
            .Select(s => s * 0.001)  // constant slope, same on both rails
            .ToArray();

        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRail(0, dist), AlongRail(1, dist)]);

        var steps = _strategy.GenerateSteps(def).ToList();
        SetReadingsFromHeights(steps, 0, heights, dist);
        SetReadingsFromHeights(steps, 1, heights, dist);  // same as rail 0

        var result = _sut.Calculate(steps, def, CalcParams());

        Assert.Single(result.ParallelismProfiles);
        // Parallelism = max deviation - min deviation of (h_B - h_A)
        // Since heights are identical, all deviations are 0 → parallelism = 0
        Assert.Equal(0.0, result.ParallelismProfiles[0].ParallelismValueMm, precision: 6);
    }

    // ── Independent mode ──────────────────────────────────────────────────────

    [Fact]
    public void AllZeroReadings_IndependentMode_StraightnessIsZero()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRail(0, 200), AlongRail(1, 200)],
            solverMode: SolverMode.IndependentThenReconcile);

        var steps = _strategy.GenerateSteps(def).ToList();
        foreach (var s in steps) s.Reading = 0.0;

        var result = _sut.Calculate(steps, def, CalcParams());

        Assert.All(result.RailProfiles, p =>
            Assert.Equal(0.0, p.StraightnessValueMm, precision: 9));
    }

    // ── Station positions ─────────────────────────────────────────────────────

    [Fact]
    public void StationPositions_MatchExpected()
    {
        const double dist = 200.0;

        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRail(0, dist), AlongRail(1, dist)]);

        var steps = _strategy.GenerateSteps(def).ToList();
        foreach (var s in steps) s.Reading = 0.0;

        var result = _sut.Calculate(steps, def, CalcParams());

        var profile = result.RailProfiles.First(p => p.RailIndex == 0);

        // Expect 6 stations at 0, 200, 400, 600, 800, 1000 mm
        Assert.Equal(6, profile.StationPositionsMm.Length);
        for (int i = 0; i < 6; i++)
            Assert.Equal(i * dist, profile.StationPositionsMm[i], precision: 6);
    }

    // ── RailProfile count ─────────────────────────────────────────────────────

    [Fact]
    public void TwoRails_ProducesTwoProfiles()
    {
        var def = Def(
            [Rail("A", 1000), Rail("B", 1000, lateral: 400)],
            [AlongRail(0, 200), AlongRail(1, 200)]);

        var steps = _strategy.GenerateSteps(def).ToList();
        foreach (var s in steps) s.Reading = 0.0;

        var result = _sut.Calculate(steps, def, CalcParams());

        Assert.Equal(2, result.RailProfiles.Count);
        Assert.Contains(result.RailProfiles, p => p.RailIndex == 0);
        Assert.Contains(result.RailProfiles, p => p.RailIndex == 1);
    }
}
