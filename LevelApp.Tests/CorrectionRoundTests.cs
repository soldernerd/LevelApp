using LevelApp.Core.Geometry.Calculators;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Tests;

/// <summary>
/// Tests for <see cref="MeasurementRound.MergeWithReplacements"/> and the
/// end-to-end correction workflow (merge → recalculate).
/// </summary>
public class CorrectionRoundTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ObjectDefinition Def(int cols = 3, int rows = 3) => new()
    {
        GeometryModuleId = "SurfacePlate",
        Parameters = new Dictionary<string, object>
        {
            ["columnsCount"] = cols, ["rowsCount"] = rows,
            ["widthMm"] = 300.0,    ["heightMm"]  = 300.0
        }
    };

    private static List<MeasurementStep> FlatSteps(ObjectDefinition def)
    {
        var steps = new FullGridStrategy().GenerateSteps(def).ToList();
        foreach (var s in steps) s.Reading = 0.0;
        return steps;
    }

    private static CalculationParameters Params() => new()
    {
        MethodId            = "LeastSquares",
        SigmaThreshold      = 2.5,
        AutoExcludeOutliers = true
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NoReplacements_AllOriginalReadingsPreserved()
    {
        var steps = FlatSteps(Def());
        steps[3].Reading = 1.5;
        var origReadings = steps.Select(s => s.Reading).ToList();

        var merged = MeasurementRound.MergeWithReplacements(steps, []);

        for (int i = 0; i < merged.Count; i++)
            Assert.Equal(origReadings[i], merged[i].Reading);
    }

    [Fact]
    public void SingleReplacement_UpdatesReadingAtCorrectIndex()
    {
        var steps = FlatSteps(Def());
        var replacement = new ReplacedStep { OriginalStepIndex = 2, Reading = 9.99 };

        var merged = MeasurementRound.MergeWithReplacements(steps, [replacement]);

        Assert.Equal(9.99, merged.First(s => s.Index == 2).Reading!.Value, precision: 9);
        // All other steps unchanged (original reading was 0.0)
        Assert.All(merged.Where(s => s.Index != 2),
            s => Assert.Equal(0.0, s.Reading!.Value, precision: 9));
    }

    [Fact]
    public void MultipleReplacements_AllApplied()
    {
        var steps = FlatSteps(Def());
        var replacements = new[]
        {
            new ReplacedStep { OriginalStepIndex = 0, Reading = 1.0 },
            new ReplacedStep { OriginalStepIndex = 4, Reading = 2.0 },
            new ReplacedStep { OriginalStepIndex = 9, Reading = 3.0 }
        };

        var merged = MeasurementRound.MergeWithReplacements(steps, replacements);

        Assert.Equal(1.0, merged.First(s => s.Index == 0).Reading!.Value, precision: 9);
        Assert.Equal(2.0, merged.First(s => s.Index == 4).Reading!.Value, precision: 9);
        Assert.Equal(3.0, merged.First(s => s.Index == 9).Reading!.Value, precision: 9);
    }

    [Fact]
    public void Replacement_PreservesAllOtherStepFields()
    {
        var def    = Def();
        var steps  = new FullGridStrategy().GenerateSteps(def).ToList();
        foreach (var s in steps) s.Reading = 0.0;

        var original = steps[0];
        var merged   = MeasurementRound.MergeWithReplacements(steps,
            [new ReplacedStep { OriginalStepIndex = 0, Reading = 7.7 }]);
        var result   = merged[0];

        Assert.Equal(original.Index,           result.Index);
        Assert.Equal(original.NodeId,          result.NodeId);
        Assert.Equal(original.ToNodeId,        result.ToNodeId);
        Assert.Equal(original.Orientation,     result.Orientation);
        Assert.Equal(original.PassPhase,       result.PassPhase);
        Assert.Equal(original.InstructionText, result.InstructionText);
        Assert.Equal(7.7,                      result.Reading!.Value, precision: 9);
    }

    [Fact]
    public void NonExistentIndex_HasNoEffect()
    {
        var steps = FlatSteps(Def());
        steps[1].Reading = 5.0;

        var merged = MeasurementRound.MergeWithReplacements(steps,
            [new ReplacedStep { OriginalStepIndex = 999, Reading = -99.0 }]);

        Assert.Equal(5.0, merged[1].Reading!.Value, precision: 9);
        Assert.Equal(merged.Count, steps.Count);
    }

    [Fact]
    public void MergedSteps_WithCorrectedReading_ProduceDifferentResult()
    {
        // Initial round: inject a large erroneous reading on step 0.
        var def   = Def();
        var steps = FlatSteps(def);
        steps[0].Reading = 50.0;   // bad reading

        var calc = new LeastSquaresCalculator(new FullGridStrategy());

        var initialResult  = calc.Calculate(steps, def, Params());

        // Correction: replace step 0 with the true zero reading.
        var corrected = MeasurementRound.MergeWithReplacements(steps,
            [new ReplacedStep { OriginalStepIndex = 0, Reading = 0.0 }]);
        var correctedResult = calc.Calculate(corrected, def, Params());

        // Flatness must decrease after the bad reading is corrected.
        Assert.True(correctedResult.FlatnessValueMm < initialResult.FlatnessValueMm,
            $"Expected flatness to decrease after correction. " +
            $"Before: {initialResult.FlatnessValueMm:G4}, After: {correctedResult.FlatnessValueMm:G4}");
        // The corrected result should produce no flagged steps on clean data.
        Assert.Empty(correctedResult.FlaggedStepIndices);
    }
}
