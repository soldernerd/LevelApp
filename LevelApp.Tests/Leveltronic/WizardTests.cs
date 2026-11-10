using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Calibration;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>
/// Port of the phone app's test/providers/wizards_test.dart: the three calibration
/// wizards, end to end against the simulator's known truth.
/// </summary>
public sealed class FlipCalWizardTests
{
    private static async Task<(SimRig rig, FlipCalWizard w)> NewAsync(bool serviceMode = true)
    {
        var rig = await SimRig.OpenAsync();
        rig.Sim.SetServiceMode(serviceMode);
        var w = new FlipCalWizard(rig.Api);
        await w.RefreshValuesAsync();
        return (rig, w);
    }

    private static Task Until(SimRig rig, Func<bool> c, int seconds = 10) =>
        TestKit.AdvanceUntilAsync(rig.Time, c, TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task WalksBothSteps_AndRecordsBeforeAndAfter()
    {
        var (rig, w) = await NewAsync();
        Assert.NotNull(w.State.Current);
        long zeroBefore = rig.Sim.Value(ValueCatalog.S1Zero);

        w.SelectSensors(SensorMask.Both);
        await w.StartAsync();
        await Until(rig, () => w.State.Stage == FlipStage.Measuring1);
        await Until(rig, () => w.State.Stage == FlipStage.Turn);

        await w.ConfirmTurnedAsync();
        Assert.Equal(FlipStage.Measuring2, w.State.Stage);
        await Until(rig, () => w.State.Stage == FlipStage.Result);

        var run = w.State.LastRun!;
        Assert.Equal(zeroBefore, run.Before.ZeroPpm[0]);
        Assert.Equal(rig.Sim.Value(ValueCatalog.S1Zero), run.After.ZeroPpm[0]);
        Assert.NotEqual(run.Before.ZeroPpm[0], run.After.ZeroPpm[0]);
        Assert.Equal(SensorMask.Both, run.Mask);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ASingleSensorLeavesTheOthersZeroAlone()
    {
        var (rig, w) = await NewAsync();
        long s2 = rig.Sim.Value(ValueCatalog.S2Zero);

        w.SelectSensors(SensorMask.S1);
        await w.StartAsync();
        await Until(rig, () => w.State.Stage == FlipStage.Turn);
        await w.ConfirmTurnedAsync();
        await Until(rig, () => w.State.Stage == FlipStage.Result);

        Assert.Equal(s2, rig.Sim.Value(ValueCatalog.S2Zero));
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task WithoutServiceMode_EndsInAnErrorThatAsksForIt()
    {
        var (rig, w) = await NewAsync(serviceMode: false);

        await w.StartAsync();
        await TestKit.SettleAsync();

        Assert.Equal(FlipStage.Error, w.State.Stage);
        Assert.True(w.State.NeedsService);
        Assert.Contains("Service mode", w.State.Message);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ARepeatRunIsRecordedNextToTheFirst()
    {
        var (rig, w) = await NewAsync();
        for (int run = 0; run < 2; run++)
        {
            await w.StartAsync();
            await Until(rig, () => w.State.Stage == FlipStage.Turn);
            await w.ConfirmTurnedAsync();
            await Until(rig, () => w.State.Stage == FlipStage.Result);
            w.BackToStart();
            await TestKit.SettleAsync();
        }

        Assert.Equal(2, w.State.FinishedRuns.Count);
        // The second run finds (almost) nothing left to correct.
        var second = w.State.FinishedRuns[1];
        Assert.InRange(Math.Abs(second.After.ZeroPpm[0] - second.Before.ZeroPpm[0]), 0, 1);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task Cancel_ReturnsToTheSelectionAndAbortsTheDevice()
    {
        var (rig, w) = await NewAsync();
        await w.StartAsync();
        await TestKit.AdvanceAsync(rig.Time, TimeSpan.FromMilliseconds(500));

        await w.CancelAsync();
        Assert.Equal(FlipStage.Select, w.State.Stage);

        await TestKit.AdvanceAsync(rig.Time, TimeSpan.FromSeconds(3));
        Assert.Equal(FlipStage.Select, w.State.Stage);
        await rig.DisposeAsync();
    }
}

public sealed class PhaseCalWizardTests
{
    private static async Task<(SimRig rig, PhaseCalWizard w)> NewAsync(bool serviceMode = true)
    {
        var rig = await SimRig.OpenAsync();
        rig.Sim.SetServiceMode(serviceMode);
        var w = new PhaseCalWizard(rig.Api, rig.Time);
        await w.RefreshValuesAsync();
        return (rig, w);
    }

    /// <summary>
    /// Runs the wizard up to the review stage, capturing each tilt of <paramref name="mode"/>
    /// with a tilt of +/- <paramref name="step"/>.
    /// </summary>
    private static async Task<PhaseCalState> RunToReviewAsync(
        SimRig rig, PhaseCalWizard w, double step = 0.1, PhaseMode mode = PhaseMode.Both)
    {
        w.SelectMode(mode);
        await w.BeginAsync();
        foreach (var tilt in mode.Tilts())
        {
            Assert.Equal(PhaseStage.Ready, w.State.Stage);
            Assert.Equal(tilt, w.State.Tilt);
            double t = tilt == PhaseTilt.Positive ? step : -step;
            rig.Sim.PhysicalTilt = [t, t];
            await w.CaptureAsync();
            await TestKit.AdvanceUntilAsync(rig.Time, () => w.State.Stage != PhaseStage.Capturing,
                TimeSpan.FromSeconds(12));
        }
        return w.State;
    }

    private static double Off(double a, double truth) => Math.Abs(CalibrationMath.WrapDegrees(a - truth));

    [Fact]
    public async Task FindsTheTruePhaseOfBothSensors_AndWritesIt()
    {
        var (rig, w) = await NewAsync();
        long before1 = rig.Sim.Value(ValueCatalog.S1Phase);

        var s = await RunToReviewAsync(rig, w);
        Assert.Equal(PhaseStage.Review, s.Stage);
        Assert.Equal(rig.Sim.TruePhaseDeg[0], s.PhaseEstimates[0]!.AngleDeg, 1);
        Assert.Equal(rig.Sim.TruePhaseDeg[1], s.PhaseEstimates[1]!.AngleDeg, 1);
        Assert.True(s.Samples > 250);

        await w.ApplyAsync();
        var done = w.State;
        Assert.Equal(PhaseStage.Done, done.Stage);
        Assert.Equal(before1, done.Before![0]);
        Assert.InRange((double)done.After![0], rig.Sim.TruePhaseDeg[0] * 100 - 10, rig.Sim.TruePhaseDeg[0] * 100 + 10);
        Assert.InRange((double)done.After![1], rig.Sim.TruePhaseDeg[1] * 100 - 10, rig.Sim.TruePhaseDeg[1] * 100 + 10);
        Assert.Equal(done.After![0], rig.Sim.Value(ValueCatalog.S1Phase));
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task TheModeDecidesHowManyStepsThereAre()
    {
        var (rig, w) = await NewAsync();
        Assert.Equal(PhaseMode.Both, w.State.Mode);
        Assert.Equal(2, w.State.Steps);
        w.SelectMode(PhaseMode.NegativeOnly);
        Assert.Equal(1, w.State.Steps);

        await w.BeginAsync();
        Assert.Equal(PhaseStage.Ready, w.State.Stage);
        Assert.Equal(PhaseTilt.Negative, w.State.Tilt);
        Assert.Equal(1, w.State.Step);

        // The mode cannot be changed once the measurement has begun.
        w.SelectMode(PhaseMode.Both);
        Assert.Equal(PhaseMode.NegativeOnly, w.State.Mode);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task OnlyTheSelectedSensorIsWritten()
    {
        var (rig, w) = await NewAsync();
        long s2Before = rig.Sim.Value(ValueCatalog.S2Phase);

        w.SelectSensors(SensorMask.S1);
        await RunToReviewAsync(rig, w);
        await w.ApplyAsync();

        Assert.Equal(PhaseStage.Done, w.State.Stage);
        Assert.Equal(s2Before, rig.Sim.Value(ValueCatalog.S2Phase));
        Assert.InRange(rig.Sim.Value(ValueCatalog.S1Phase), 1190, 1210);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ASingleTiltOfEitherSignFindsThePhaseToo()
    {
        var (rig, w) = await NewAsync(serviceMode: false);

        var pos = await RunToReviewAsync(rig, w, step: 0.2, mode: PhaseMode.PositiveOnly);
        Assert.True(Off(pos.PhaseEstimates[0]!.AngleDeg, rig.Sim.TruePhaseDeg[0]) < 0.5);
        Assert.True(pos.PhaseEstimates[0]!.SingleSided);

        w.BackToStart();
        await TestKit.SettleAsync();
        var neg = await RunToReviewAsync(rig, w, step: 0.2, mode: PhaseMode.NegativeOnly);
        Assert.True(Off(neg.PhaseEstimates[0]!.AngleDeg, rig.Sim.TruePhaseDeg[0]) < 0.5);
        Assert.True(Off(neg.PhaseEstimates[1]!.AngleDeg, rig.Sim.TruePhaseDeg[1]) < 0.5);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task APhaseBeyondNinetyDegreesIsFoundAndWritten()
    {
        var (rig, w) = await NewAsync();
        rig.Sim.TruePhaseDeg = [150.0, -120.0];

        var s = await RunToReviewAsync(rig, w);
        Assert.True(Off(s.PhaseEstimates[0]!.AngleDeg, 150) < 0.2);
        Assert.True(Off(s.PhaseEstimates[1]!.AngleDeg, -120) < 0.2);
        await w.ApplyAsync();

        Assert.Equal(PhaseStage.Done, w.State.Stage);
        Assert.InRange(rig.Sim.Value(ValueCatalog.S1Phase), 14980, 15020);
        Assert.InRange(rig.Sim.Value(ValueCatalog.S2Phase), -12020, -11980);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task AnInvertedSensorGetsThePhaseThatReadsTheTiltPositive()
    {
        var (rig, w) = await NewAsync(serviceMode: false);
        rig.Sim.SetValue(ValueCatalog.S1Invert, 1);

        var s = await RunToReviewAsync(rig, w);

        // Positive reads positive only with the raw signal turned around.
        Assert.True(Off(s.PhaseEstimates[0]!.AngleDeg, rig.Sim.TruePhaseDeg[0] - 180) < 0.2);
        Assert.True(Off(s.PhaseEstimates[1]!.AngleDeg, rig.Sim.TruePhaseDeg[1]) < 0.2);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task NoTiltAtAllGivesNoEstimate()
    {
        var (rig, w) = await NewAsync(serviceMode: false);
        var s = await RunToReviewAsync(rig, w, step: 0.0);
        Assert.Null(s.PhaseEstimates[0]); // a zero step has no direction to find
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ApplyingWithoutServiceMode_AsksForIt()
    {
        var (rig, w) = await NewAsync(serviceMode: false);
        await RunToReviewAsync(rig, w);

        await w.ApplyAsync();

        Assert.Equal(PhaseStage.Error, w.State.Stage);
        Assert.True(w.State.NeedsService);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task BothSensorsAreWrittenWhenBothAreInRange()
    {
        var (rig, w) = await NewAsync();
        rig.Sim.TruePhaseDeg = [70.0, 10.5];

        var s = await RunToReviewAsync(rig, w);
        Assert.True(s.PhaseEstimates[0]!.InRange);
        Assert.True(s.PhaseEstimates[1]!.InRange);
        await w.ApplyAsync();

        Assert.Equal(PhaseStage.Done, w.State.Stage);
        Assert.InRange(rig.Sim.Value(ValueCatalog.S1Phase), 6990, 7010);
        Assert.InRange(rig.Sim.Value(ValueCatalog.S2Phase), 1040, 1060);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task TooFewBatches_IsAnError()
    {
        var (rig, w) = await NewAsync();
        rig.Sim.PhasorSilent = true;
        await w.BeginAsync();

        await w.CaptureAsync();
        await TestKit.AdvanceUntilAsync(rig.Time, () => w.State.Stage == PhaseStage.Error, TimeSpan.FromSeconds(12));

        Assert.Contains("too few phasor batches", w.State.Message);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task Redo_GoesBackToTheFirstTilt()
    {
        var (rig, w) = await NewAsync();
        await w.BeginAsync();
        await w.CaptureAsync();
        await TestKit.AdvanceAsync(rig.Time, TimeSpan.FromSeconds(2));
        Assert.Equal(PhaseStage.Capturing, w.State.Stage);

        w.Redo();
        Assert.Equal(PhaseStage.Ready, w.State.Stage);
        Assert.Equal(PhaseTilt.Positive, w.State.Tilt);

        // Capture the positive tilt, then redo from the negative one.
        rig.Sim.PhysicalTilt = [0.1, 0.1];
        await w.CaptureAsync();
        await TestKit.AdvanceUntilAsync(rig.Time, () => w.State.Stage != PhaseStage.Capturing, TimeSpan.FromSeconds(12));
        Assert.Equal(PhaseStage.Ready, w.State.Stage);
        Assert.Equal(PhaseTilt.Negative, w.State.Tilt);
        Assert.Equal(2, w.State.Step);

        w.Redo();
        Assert.Equal(PhaseTilt.Positive, w.State.Tilt);
        await rig.DisposeAsync();
    }
}

public sealed class GainCalWizardTests
{
    private static async Task<(SimRig rig, GainCalWizard w)> NewAsync(
        bool serviceMode = true, bool phaseRight = true, ILeveltronicPreferences? prefs = null)
    {
        var rig = await SimRig.OpenAsync();
        rig.Sim.SetServiceMode(serviceMode);
        if (phaseRight)
        {
            // Phase already right, so only k is off.
            rig.Sim.SetValue(ValueCatalog.S1Phase, 1200);
            rig.Sim.SetValue(ValueCatalog.S2Phase, 1050);
        }
        var w = new GainCalWizard(rig.Api, rig.Time, prefs, "6DA08073");
        await w.RefreshValuesAsync();
        return (rig, w);
    }

    private static Task Until(SimRig rig, Func<bool> c, int seconds = 10) =>
        TestKit.AdvanceUntilAsync(rig.Time, c, TimeSpan.FromSeconds(seconds));

    private static async Task RunToReviewAsync(SimRig rig, GainCalWizard w, double step = 0.1)
    {
        w.SetShimForSensor(0, heightMm: 0.1, baselineMm: 1000);
        w.SetShimForSensor(1, heightMm: 0.1, baselineMm: 1000);
        w.Begin();
        rig.Sim.PhysicalTilt = [0.0, 0.0];
        var b = w.MeasureBaselineAsync();
        await Until(rig, () => b.IsCompleted);
        Assert.Equal(GainStage.Shim, w.State.Stage);

        rig.Sim.PhysicalTilt = [step, step];
        var s = w.MeasureShimAsync();
        await Until(rig, () => s.IsCompleted);
    }

    [Fact]
    public async Task SetsK_SoThatTheKnownStepReadsAsTheKnownTilt()
    {
        var (rig, w) = await NewAsync();

        await RunToReviewAsync(rig, w);
        var s = w.State;
        Assert.Equal(GainStage.Review, s.Stage);
        Assert.Equal(45000, s.GainEstimates[0]!.KOldMicro);
        Assert.InRange(s.GainEstimates[0]!.KNewMicro, 49700, 50300);
        Assert.InRange(s.GainEstimates[1]!.KNewMicro, 47700, 48300);

        await w.ApplyAsync();
        var done = w.State;
        Assert.Equal(GainStage.Done, done.Stage);
        Assert.Equal(45000, done.KBefore![0]);
        Assert.InRange(done.KAfter![0], 49700, 50300);
        Assert.Equal(done.KAfter![0], rig.Sim.Value(ValueCatalog.S1K));
        // Storing does not measure again: the instrument saw only the two precision runs.
        Assert.Equal(2, rig.Sim.PrecisionRuns);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ADifferentShimAndEdgeGiveTheSameK()
    {
        var (rig, w) = await NewAsync();
        w.SetShimForSensor(0, heightMm: 0.05, baselineMm: 250); // 0.2 mm/m
        w.SetShimForSensor(1, heightMm: 0.05, baselineMm: 250);
        Assert.Equal(0.2, w.State.KnownTilts[0], 9);

        w.Begin();
        rig.Sim.PhysicalTilt = [0.0, 0.0];
        var b = w.MeasureBaselineAsync();
        await Until(rig, () => b.IsCompleted);
        rig.Sim.PhysicalTilt = [0.2, 0.2];
        var s = w.MeasureShimAsync();
        await Until(rig, () => s.IsCompleted);

        Assert.InRange(w.State.GainEstimates[0]!.KNewMicro, 49700, 50300);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task OnlyTheSelectedSensorIsWritten()
    {
        var (rig, w) = await NewAsync();
        w.SelectSensors(SensorMask.S2);

        await RunToReviewAsync(rig, w);
        await w.ApplyAsync();

        Assert.Equal(45000, rig.Sim.Value(ValueCatalog.S1K));
        Assert.InRange(rig.Sim.Value(ValueCatalog.S2K), 47700, 48300);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ApplyingWithoutServiceMode_AsksForIt()
    {
        var (rig, w) = await NewAsync(serviceMode: false, phaseRight: false);
        await RunToReviewAsync(rig, w);

        await w.ApplyAsync();

        Assert.Equal(GainStage.Error, w.State.Stage);
        Assert.True(w.State.NeedsService);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task ADisturbedTable_SendsTheUserBackToMeasureAgain()
    {
        var rig = await SimRig.OpenAsync(t => new SimulatedLeveltronicLink(t) { PrecisionNeverClean = true });
        var w = new GainCalWizard(rig.Api, rig.Time);
        w.Begin();

        var b = w.MeasureBaselineAsync();
        await Until(rig, () => b.IsCompleted);

        Assert.Equal(GainStage.Baseline, w.State.Stage);
        Assert.Contains("quiet", w.State.Message);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task NoShimInputMeansNoStart()
    {
        var (rig, w) = await NewAsync(serviceMode: false);
        w.SetShimForSensor(0, heightMm: 0, baselineMm: 1000);
        w.SetShimForSensor(1, heightMm: 0, baselineMm: 1000);

        w.Begin();

        Assert.Equal(GainStage.Setup, w.State.Stage);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task TheShimSetupIsRememberedPerInstrument()
    {
        var prefs = new InMemoryLeveltronicPreferences();
        var (rig, w) = await NewAsync(prefs: prefs);
        w.SetShimForSensor(0, heightMm: 0.25, baselineMm: 500);
        w.SetShimForSensor(1, heightMm: 0.5, baselineMm: 300);

        var again = new GainCalWizard(rig.Api, rig.Time, prefs, "6DA08073");
        var other = new GainCalWizard(rig.Api, rig.Time, prefs, "OTHER000");

        Assert.Equal(0.25, again.State.ShimHeights[0]);
        Assert.Equal(300, again.State.Baselines[1]);
        Assert.Equal(0.1, other.State.ShimHeights[0]); // another instrument starts from the defaults
        await rig.DisposeAsync();
    }
}
