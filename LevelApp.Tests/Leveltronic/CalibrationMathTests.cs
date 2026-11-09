using LevelApp.Instruments.Leveltronic.Calibration;

namespace LevelApp.Tests.Leveltronic;

/// <summary>Port of the phone app's test/calibration/calibration_math_test.dart.</summary>
public sealed class CalibrationMathTests
{
    /// <summary>A capture of <c>u = (zero + tilt·k)·e^{jφ}</c> with a little noise.</summary>
    private static ComplexStats Capture(
        double tilt, double k = 0.05, double zero = 0.001, double phaseDeg = 12,
        double noise = 1e-5, int n = 300, int seed = 1)
    {
        var rng = new Random(seed);
        var s = new ComplexStats();
        double mag = zero + tilt * k;
        double ph = phaseDeg * Math.PI / 180;
        for (int i = 0; i < n; i++)
            s.Add(mag * Math.Cos(ph) + (rng.NextDouble() - 0.5) * noise,
                  mag * Math.Sin(ph) + (rng.NextDouble() - 0.5) * noise);
        return s;
    }

    [Fact]
    public void ComplexStats_MeanAndVariance()
    {
        var s = new ComplexStats();
        foreach (double v in new[] { 1.0, 2.0, 3.0, 4.0 }) s.Add(v, -v);
        Assert.Equal(4, s.N);
        Assert.Equal(2.5, s.MeanRe, 12);
        Assert.Equal(-2.5, s.MeanIm, 12);
        Assert.Equal(5.0 / 3, s.VarRe, 12);
    }

    [Fact]
    public void ComplexStats_ResetClearsEverything()
    {
        var s = new ComplexStats();
        s.Add(3, 4); s.Add(5, 6);
        s.Reset();
        Assert.Equal(0, s.N);
        Assert.Equal(0, s.VarRe);
        s.Add(1, 1);
        Assert.Equal(1, s.MeanRe);
    }

    [Fact]
    public void ComplexDivide_Works()
    {
        var (re, im) = CalibrationMath.ComplexDivide(1, 1, 0, 1)!.Value; // (1+j)/j = 1 - j
        Assert.Equal(1, re, 12);
        Assert.Equal(-1, im, 12);
        Assert.Null(CalibrationMath.ComplexDivide(1, 1, 0, 0));
    }

    [Theory]
    [InlineData(-40.0)]
    [InlineData(-12.5)]
    [InlineData(0.0)]
    [InlineData(3.0)]
    [InlineData(12.0)]
    [InlineData(44.0)]
    public void EstimatePhase_RecoversTheTruth(double truth)
    {
        var e = CalibrationMath.EstimatePhase(Capture(0, phaseDeg: truth, seed: 1), Capture(0.1, phaseDeg: truth, seed: 2))!;
        Assert.Equal(truth, e.AngleDeg, 1);
        Assert.True(e.InRange);
        Assert.True(e.Confident);
        Assert.InRange((double)e.CentiDegrees, truth * 100 - 10, truth * 100 + 10);
    }

    [Fact]
    public void EstimatePhase_TheSignOfTheStepDoesNotChangeThePhase()
    {
        var up = CalibrationMath.EstimatePhase(Capture(0, seed: 1), Capture(0.1, seed: 2))!;
        var down = CalibrationMath.EstimatePhase(Capture(0, seed: 1), Capture(-0.1, seed: 2))!;
        Assert.Equal(up.AngleDeg, down.AngleDeg, 1);
    }

    [Fact]
    public void EstimatePhase_DoesNotDependOnTheStepSize()
    {
        var small = CalibrationMath.EstimatePhase(Capture(0, seed: 1), Capture(0.02, seed: 2))!;
        var large = CalibrationMath.EstimatePhase(Capture(0, seed: 1), Capture(1.0, seed: 2))!;
        Assert.Equal(small.AngleDeg, large.AngleDeg, 0);
        Assert.InRange(Math.Abs(small.AngleDeg - large.AngleDeg), 0, 0.3);
    }

    [Fact]
    public void EstimatePhase_SeventyDegreesIsInRange_ThatTheFirmwareNowAccepts()
    {
        var e = CalibrationMath.EstimatePhase(Capture(0, phaseDeg: 70, seed: 1), Capture(0.1, phaseDeg: 70, seed: 2))!;
        Assert.Equal(70, e.AngleDeg, 1);
        Assert.True(e.InRange);
        Assert.InRange(e.CentiDegrees, 6990L, 7010L);
    }

    [Fact]
    public void EstimatePhase_AStepBuriedInNoiseHasLowConfidenceAndAWideError()
    {
        var e = CalibrationMath.EstimatePhase(
            Capture(0, noise: 5e-3, n: 40, seed: 1), Capture(0.01, noise: 5e-3, n: 40, seed: 2))!;
        Assert.False(e.Confident);
        Assert.True(e.UncertaintyDeg > 1);
    }

    [Fact]
    public void EstimatePhase_UncertaintyShrinksWithABiggerStep()
    {
        var a = CalibrationMath.EstimatePhase(Capture(0, noise: 1e-4, seed: 1), Capture(0.02, noise: 1e-4, seed: 2))!;
        var b = CalibrationMath.EstimatePhase(Capture(0, noise: 1e-4, seed: 1), Capture(0.5, noise: 1e-4, seed: 2))!;
        Assert.True(b.UncertaintyDeg < a.UncertaintyDeg);
        Assert.True(b.Snr > a.Snr);
    }

    [Fact]
    public void EstimatePhase_TooFewSamplesOrNoStepIsNull()
    {
        Assert.Null(CalibrationMath.EstimatePhase(new ComplexStats(), Capture(0.1)));
        Assert.Null(CalibrationMath.EstimatePhase(Capture(0.1, n: 3), Capture(0.1, n: 3)));
        var same = new ComplexStats();
        for (int i = 0; i < 11; i++) same.Add(1, 1);
        Assert.Null(CalibrationMath.EstimatePhase(same, same));
    }

    [Fact]
    public void EstimatePhase_FoldsIntoMinus90To90()
    {
        // The step points at 192°: same line as 12°.
        var flat = new ComplexStats();
        var tilted = new ComplexStats();
        for (int i = 0; i < 20; i++)
        {
            flat.Add(0, 0);
            tilted.Add(Math.Cos(192 * Math.PI / 180), Math.Sin(192 * Math.PI / 180));
        }
        Assert.Equal(12, CalibrationMath.EstimatePhase(flat, tilted)!.AngleDeg, 6);
    }

    [Fact]
    public void EstimateGain_ScalesWithTheMeasuredOverTheTrueStep()
    {
        // Reads 0.0900 for a true 0.1000 step: the old k is 10 % too large.
        var g = CalibrationMath.EstimateGain(50000, 0.01, 0.10, 0.1)!;
        Assert.Equal(0.09, g.MeasuredChange, 12);
        Assert.Equal(45000, g.KNewMicro);
        Assert.Equal(-10, g.ChangePercent, 9);
        Assert.True(g.Positive);
        Assert.True(g.InRange);
    }

    [Fact]
    public void EstimateGain_ANegativeStepStillGivesKFromTheMagnitude_Flagged()
    {
        var g = CalibrationMath.EstimateGain(50000, 0.0, -0.1, 0.1)!;
        Assert.Equal(50000, g.KNewMicro);
        Assert.False(g.Positive);
    }

    [Fact]
    public void EstimateGain_NoChangeOrNoKnownTiltIsNull()
    {
        Assert.Null(CalibrationMath.EstimateGain(50000, 0.1, 0.1, 0.1));
        Assert.Null(CalibrationMath.EstimateGain(50000, 0, 0.1, 0));
    }

    [Fact]
    public void EstimateGain_FlagsAKOutsideTheInstrumentRange()
    {
        var g = CalibrationMath.EstimateGain(900000, 0, 0.5, 0.1)!;
        Assert.True(g.KNewMicro > 1_000_000);
        Assert.False(g.InRange);
    }

    [Fact]
    public void ShimTilt_HundredthsOverAMetre()
    {
        Assert.Equal(0.1, CalibrationMath.ShimTilt(0.1, 1000), 12);
        Assert.Equal(0.2, CalibrationMath.ShimTilt(0.1, 500), 12);
        Assert.Equal(0, CalibrationMath.ShimTilt(0.1, 0));
    }
}
