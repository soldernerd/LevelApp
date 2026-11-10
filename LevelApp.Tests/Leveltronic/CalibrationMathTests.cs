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

    private static readonly double[] Truths = [-170.0, -90.0, -40.0, -12.5, 0.0, 3.0, 12.0, 44.0, 135.0, 180.0];

    public static IEnumerable<object[]> TruthData() => Truths.Select(t => new object[] { t });

    private static double Off(double a, double truth) => Math.Abs(CalibrationMath.WrapDegrees(a - truth));

    [Theory]
    [MemberData(nameof(TruthData))]
    public void EstimatePhaseFromTilts_RecoversTheTruth_FromAPositiveAndANegativeTilt(double truth)
    {
        var e = CalibrationMath.EstimatePhaseFromTilts(
            positive: Capture(0.1, phaseDeg: truth, seed: 1),
            negative: Capture(-0.1, phaseDeg: truth, seed: 2))!;
        Assert.True(Off(e.AngleDeg, truth) < 0.1);
        Assert.True(e.InRange);
        Assert.True(e.Confident);
        Assert.False(e.SingleSided);
    }

    [Theory]
    [MemberData(nameof(TruthData))]
    public void EstimatePhaseFromTilts_RecoversTheTruth_FromAPositiveTiltAlone(double truth)
    {
        var e = CalibrationMath.EstimatePhaseFromTilts(positive: Capture(0.1, phaseDeg: truth, seed: 1))!;
        Assert.True(Off(e.AngleDeg, truth) < 0.5);
        Assert.True(e.SingleSided);
    }

    [Theory]
    [MemberData(nameof(TruthData))]
    public void EstimatePhaseFromTilts_RecoversTheTruth_FromANegativeTiltAlone(double truth)
    {
        var e = CalibrationMath.EstimatePhaseFromTilts(negative: Capture(-0.1, phaseDeg: truth, seed: 2))!;
        Assert.True(Off(e.AngleDeg, truth) < 0.5);
        Assert.True(e.SingleSided);
    }

    [Fact]
    public void EstimatePhaseFromTilts_TwoTiltsCancelTheZeroOffset_OneDoesNot()
    {
        var both = CalibrationMath.EstimatePhaseFromTilts(
            positive: Capture(0.1, zero: 0.001, phaseDeg: 30, seed: 1),
            negative: Capture(-0.1, zero: 0.001, phaseDeg: 30, seed: 2))!;
        Assert.Equal(30, both.AngleDeg, 1);

        // An offset that is not along the signal (e.g. a crosstalk term).
        static ComplexStats Offset(double tilt, int seed)
        {
            var s = Capture(tilt, zero: 0, phaseDeg: 30, seed: seed);
            var r = new ComplexStats();
            for (int i = 0; i < 300; i++) r.Add(s.MeanRe + 0.002, s.MeanIm - 0.002);
            return r;
        }

        var withOffsetBoth = CalibrationMath.EstimatePhaseFromTilts(positive: Offset(0.1, 1), negative: Offset(-0.1, 2))!;
        var withOffsetOne = CalibrationMath.EstimatePhaseFromTilts(positive: Offset(0.1, 1))!;
        Assert.True(Math.Abs(withOffsetBoth.AngleDeg - 30) < Math.Abs(withOffsetOne.AngleDeg - 30));
        Assert.Equal(30, withOffsetBoth.AngleDeg, 1);
    }

    [Fact]
    public void EstimatePhaseFromTilts_DoesNotDependOnTheTiltSize()
    {
        var small = CalibrationMath.EstimatePhaseFromTilts(Capture(0.02, seed: 1), Capture(-0.02, seed: 2))!;
        var large = CalibrationMath.EstimatePhaseFromTilts(Capture(1.0, seed: 1), Capture(-1.0, seed: 2))!;
        Assert.InRange(Math.Abs(small.AngleDeg - large.AngleDeg), 0, 0.3);
    }

    [Fact]
    public void EstimatePhaseFromTilts_ATiltBuriedInNoiseHasLowConfidenceAndAWideError()
    {
        var e = CalibrationMath.EstimatePhaseFromTilts(
            Capture(0.01, noise: 5e-3, n: 40, seed: 1), Capture(-0.01, noise: 5e-3, n: 40, seed: 2))!;
        Assert.False(e.Confident);
        Assert.True(e.UncertaintyDeg > 1);
    }

    [Fact]
    public void EstimatePhaseFromTilts_UncertaintyShrinksWithABiggerTilt()
    {
        var a = CalibrationMath.EstimatePhaseFromTilts(
            Capture(0.02, noise: 1e-4, seed: 1), Capture(-0.02, noise: 1e-4, seed: 2))!;
        var b = CalibrationMath.EstimatePhaseFromTilts(
            Capture(0.5, noise: 1e-4, seed: 1), Capture(-0.5, noise: 1e-4, seed: 2))!;
        Assert.True(b.UncertaintyDeg < a.UncertaintyDeg);
        Assert.True(b.Snr > a.Snr);
    }

    [Fact]
    public void EstimatePhaseFromTilts_TooFewSamplesNothingOrNoSignalIsNull()
    {
        Assert.Null(CalibrationMath.EstimatePhaseFromTilts());
        Assert.Null(CalibrationMath.EstimatePhaseFromTilts(positive: Capture(0.1, n: 3)));
        Assert.Null(CalibrationMath.EstimatePhaseFromTilts(positive: Capture(0.1), negative: Capture(-0.1, n: 3)));
        var zero = new ComplexStats();
        for (int i = 0; i < 10; i++) zero.Add(0, 0);
        Assert.Null(CalibrationMath.EstimatePhaseFromTilts(positive: zero));
        Assert.Null(CalibrationMath.EstimatePhaseFromTilts(positive: zero, negative: zero));
    }

    [Fact]
    public void PhaseEstimate_StoresTheFullPlusMinus180Degrees()
    {
        var e = CalibrationMath.EstimatePhaseFromTilts(positive: Capture(0.1, phaseDeg: 150, seed: 1))!;
        Assert.True(e.InRange);
        Assert.InRange(e.CentiDegrees, 14990L, 15010L);
        Assert.Equal(18000L, new PhaseEstimate(180, 0, 1, 1).CentiDegrees);
        Assert.Equal(-18000L, new PhaseEstimate(-181, 0, 1, 1).CentiDegrees);
        Assert.False(new PhaseEstimate(181, 0, 1, 1).InRange);
    }

    [Fact]
    public void Rotated_KeepsTheEstimateIn_Minus180To180()
    {
        var e = CalibrationMath.EstimatePhaseFromTilts(positive: Capture(0.1, phaseDeg: 100, seed: 1))!;
        Assert.Equal(-80, e.Rotated(180).AngleDeg, 1);
        Assert.Equal(-80, e.Rotated(-180).AngleDeg, 1);
        Assert.Equal(e.Snr, e.Rotated(180).Snr);
        Assert.Equal(180, CalibrationMath.WrapDegrees(540), 9);
        Assert.Equal(180, CalibrationMath.WrapDegrees(-180), 9);
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
