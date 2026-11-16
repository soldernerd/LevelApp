using LevelApp.Core.Geometry.Calculators;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

/// <summary>
/// The walking direction of a strategy (normal / reversed) is only about how the operator moves the
/// instrument: the same plate must give the same heights, the same flatness and a zero closure error.
/// </summary>
public class ReverseDirectionCalculationTests
{
    private static ObjectDefinition FullGridDef(bool reverse)
    {
        var def = new ObjectDefinition
        {
            GeometryModuleId = "SurfacePlate",
            Parameters = new Dictionary<string, object>
            {
                ["columnsCount"] = 5, ["rowsCount"] = 4, ["widthMm"] = 800.0, ["heightMm"] = 600.0
            }
        };
        if (reverse) def.Parameters["reverseDirection"] = true;
        return def;
    }

    private static ObjectDefinition UnionJackDef(bool reverse, UnionJackRings rings)
    {
        var def = new ObjectDefinition
        {
            GeometryModuleId = "SurfacePlate",
            Parameters = new Dictionary<string, object>
            {
                ["segments"] = 3, ["rings"] = rings.ToString(), ["widthMm"] = 900.0, ["heightMm"] = 600.0
            }
        };
        if (reverse) def.Parameters["reverseDirection"] = true;
        return def;
    }

    /// <summary>A smooth, slightly warped plate (mm).</summary>
    private static double Height(double x, double y) =>
        0.004 * x / 1000.0 + 0.002 * y / 1000.0 + 0.003 * Math.Sin(x / 300.0) * Math.Cos(y / 250.0);

    private static List<MeasurementStep> Measure(IMeasurementStrategy strategy, ObjectDefinition def)
    {
        var steps = strategy.GenerateSteps(def).ToList();
        foreach (var s in steps)
        {
            var (x0, y0) = strategy.GetNodePosition(s, def);
            var (x1, y1) = strategy.GetToNodePosition(s, def);
            double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            s.Reading = (Height(x1, y1) - Height(x0, y0)) * 1000.0 / len;   // mm/m
        }
        return steps;
    }

    private static CalculationParameters Params() => new()
    {
        MethodId = "LeastSquares", SigmaThreshold = 2.5, AutoExcludeOutliers = false
    };

    /// <summary>The datum (first node of the walk) differs, so compare the heights about their mean.</summary>
    private static void AssertSameShape(SurfaceResult a, SurfaceResult b)
    {
        double meanA = a.NodeHeights.Values.Average();
        double meanB = b.NodeHeights.Values.Average();
        foreach (var (id, h) in a.NodeHeights)
            Assert.Equal(h - meanA, b.NodeHeights[id] - meanB, 9);
    }

    [Fact]
    public void FullGrid_ReversedWalk_GivesTheSameSurface()
    {
        var strategy = new FullGridStrategy();
        var normalDef = FullGridDef(false);
        var revDef    = FullGridDef(true);

        var normal   = new LeastSquaresCalculator(strategy).Calculate(Measure(strategy, normalDef), normalDef, Params());
        var reversed = new LeastSquaresCalculator(strategy).Calculate(Measure(strategy, revDef),    revDef,    Params());

        Assert.Equal(normal.FlatnessValueMm, reversed.FlatnessValueMm, 9);
        AssertSameShape(normal, reversed);
        Assert.True(reversed.ClosureErrorMax < 1e-9);
    }

    [Theory]
    [InlineData(UnionJackRings.None)]
    [InlineData(UnionJackRings.Circumference)]
    [InlineData(UnionJackRings.Full)]
    public void UnionJack_ReversedWalk_GivesTheSameSurface(UnionJackRings rings)
    {
        var strategy = new UnionJackStrategy();
        var normalDef = UnionJackDef(false, rings);
        var revDef    = UnionJackDef(true, rings);

        var normal   = new LeastSquaresCalculator(strategy).Calculate(Measure(strategy, normalDef), normalDef, Params());
        var reversed = new LeastSquaresCalculator(strategy).Calculate(Measure(strategy, revDef),    revDef,    Params());

        Assert.Equal(normal.FlatnessValueMm, reversed.FlatnessValueMm, 9);
        AssertSameShape(normal, reversed);
        Assert.True(reversed.ClosureErrorMax < 1e-9);
    }
}
