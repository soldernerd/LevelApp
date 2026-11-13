using LevelApp.Instruments.Leveltronic.Diagnostics;

namespace LevelApp.Tests.Leveltronic;

public sealed class ScopeAndDiagnosticsMathTests
{
    [Fact]
    public void Trace_HasStepsPlusOnePoints_AndStartsAtPhase()
    {
        var t = ScopeMath.Trace(peakMv: 600, phaseDeg: 90, fullScaleMv: 1200);
        Assert.Equal(ScopeMath.Steps + 1, t.Length);
        Assert.Equal(0.5, t[0], 9);                 // 600 * sin(90 deg) / 1200
        // 450 deg later the phase is 540 deg = 180 deg: zero crossing.
        Assert.Equal(0.0, t[^1], 9);
    }

    [Fact]
    public void Trace_ScalesWithFullScale()
    {
        var a = ScopeMath.Trace(10, 30, 100);
        var b = ScopeMath.Trace(10, 30, 50);
        for (int i = 0; i < a.Length; i++) Assert.Equal(a[i] * 2, b[i], 9);
    }

    [Fact]
    public void Trace_PeaksAtPeakOverFullScale()
    {
        var t = ScopeMath.Trace(300, 0, 1200, steps: 450, spanDeg: 450);
        Assert.Equal(0.25, t.Max(), 6);
        Assert.Equal(-0.25, t.Min(), 6);
    }

    [Fact]
    public void GridLines_AndLabels()
    {
        Assert.Equal(600, ScopeMath.GridLineMv(1200, 2), 9);
        Assert.Equal(0, ScopeMath.GridLineMv(1200, 4), 9);
        Assert.Equal(-900, ScopeMath.GridLineMv(1200, 7), 9);
        Assert.Equal("0", ScopeMath.GridLabel(0));
        Assert.Equal("+600", ScopeMath.GridLabel(600));
        Assert.Equal("−5", ScopeMath.GridLabel(-5));
    }

    [Fact]
    public void LegendText_UsesTwoDecimalsBelow50mVPeak()
    {
        Assert.Equal("S1  12.34 mV p–p", ScopeMath.LegendText("S1", 6.17));
        Assert.Equal("A  1000.0 mV p–p", ScopeMath.LegendText("A", 500));
    }

    [Fact]
    public void ScaleTexts()
    {
        Assert.Equal(8, ScopeMath.ScalesMv.Count);
        Assert.Equal(5, ScopeMath.ScalesMv[^1]);
        Assert.Equal(10, ScopeMath.ScalesMv[^2]);
        Assert.Equal("S1/S2 ±200 mV", ScopeMath.ScaleItemText(200));
        Assert.Equal("±1200 mV full scale", ScopeMath.FullScaleText(1200));
    }

    [Theory]
    [InlineData(-10, 350)]
    [InlineData(370, 10)]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    public void Wrap360_FoldsIntoRange(double input, double expected) =>
        Assert.Equal(expected, DiagnosticsMath.Wrap360(input), 9);

    [Fact]
    public void CalibratedPhase_SubtractsStoredPhase()
    {
        Assert.Equal(80.0, DiagnosticsMath.CalibratedPhase(90, 1000)!.Value, 9);   // stored 10.00 deg
        Assert.Equal(350.0, DiagnosticsMath.CalibratedPhase(5, 1500)!.Value, 9);   // wraps
        Assert.Null(DiagnosticsMath.CalibratedPhase(90, null));
    }

    [Fact]
    public void SensorIndex_OnlyForS1S2()
    {
        Assert.Equal(0, DiagnosticsMath.SensorIndex("S1"));
        Assert.Equal(1, DiagnosticsMath.SensorIndex("S2"));
        Assert.Null(DiagnosticsMath.SensorIndex("A"));
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("12.346 mV", DiagnosticsMath.FormatMv(12.3456));
        Assert.Equal("123.46 mV", DiagnosticsMath.FormatMv(123.456));
        Assert.Equal("+90.0", DiagnosticsMath.FormatSigned(90, 1));
        Assert.Equal("−0.5", DiagnosticsMath.FormatSigned(-0.5, 1));
        Assert.Equal("—", DiagnosticsMath.FormatSigned(double.NaN));
        Assert.Equal("+0.1234", DiagnosticsMath.FormatTilt(0.12344));
        Assert.Equal("––.––––", DiagnosticsMath.FormatTilt(double.PositiveInfinity));
        Assert.Equal("PGA ×16", DiagnosticsMath.FormatPga(16));
        Assert.Equal("", DiagnosticsMath.FormatPga(null));
        Assert.Equal("ADC ch3", DiagnosticsMath.FormatAdc(3));
        Assert.Equal(0.5, DiagnosticsMath.RmsFraction(1, 2), 9);
        Assert.Equal(0, DiagnosticsMath.RmsFraction(1, 0), 9);
    }
}
