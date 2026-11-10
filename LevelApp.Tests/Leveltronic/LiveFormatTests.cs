using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>The live screen's formatting and tare arithmetic.</summary>
public sealed class LiveFormatTests
{
    [Theory]
    [InlineData(0.12345, "+0.12345")]
    [InlineData(-0.12345, "−0.12345")]
    [InlineData(0.0, "+0.00000")]
    [InlineData(12.3, "+12.30000")]
    public void Tilt_FiveDecimals_UsesRealMinus(double v, string expected) =>
        Assert.Equal(expected, LiveFormat.Tilt(v, 5));

    [Fact]
    public void Tilt_DefaultFourDecimals() =>
        Assert.Equal("−0.1235", LiveFormat.Tilt(-0.12346));

    [Fact]
    public void Tilt_MissingOrNonFinite_IsPlaceholder()
    {
        Assert.Equal("––.–––––", LiveFormat.Tilt(null, 5));
        Assert.Equal(LiveFormat.Placeholder(5), LiveFormat.Tilt(double.NaN, 5));
        Assert.Equal(LiveFormat.Placeholder(4), LiveFormat.Tilt(double.PositiveInfinity));
    }

    [Fact]
    public void Signed_WithUnit()
    {
        Assert.Equal("+23.4 °C", LiveFormat.Signed(23.44, 1, "°C"));
        Assert.Equal("−0.50", LiveFormat.Signed(-0.5));
        Assert.Equal("—", LiveFormat.Signed(null));
    }

    [Fact]
    public void Percent_Label()
    {
        Assert.Equal("85%", LiveFormat.Percent(85));
        Assert.Equal("—", LiveFormat.Percent(null));
    }

    [Fact]
    public void ApplyTare_SubtractsOffset()
    {
        Assert.Equal(0.25, LiveFormat.ApplyTare(0.75, 0.5)!.Value, 10);
        Assert.Equal(0.75, LiveFormat.ApplyTare(0.75, null)!.Value, 10);
        Assert.Equal(1.25, LiveFormat.ApplyTare(0.75, -0.5)!.Value, 10);
        Assert.Null(LiveFormat.ApplyTare(null, 0.5));
    }

    [Fact]
    public void StateOf_DimAmberOrNormal()
    {
        Assert.Equal(ReadingState.None, LiveFormat.StateOf(false, true));
        Assert.Equal(ReadingState.Doubtful, LiveFormat.StateOf(true, true));
        Assert.Equal(ReadingState.Ok, LiveFormat.StateOf(true, false));
    }

    [Theory]
    [InlineData(80, BatteryState.Normal, BatteryLevel.Ok)]
    [InlineData(35, BatteryState.Normal, BatteryLevel.Low)]
    [InlineData(15, BatteryState.Normal, BatteryLevel.Critical)]
    [InlineData(80, BatteryState.Low, BatteryLevel.Low)]
    [InlineData(80, BatteryState.Critical, BatteryLevel.Critical)]
    public void BatteryLevelFor_Thresholds(int percent, BatteryState state, BatteryLevel expected) =>
        Assert.Equal(expected, LiveFormat.BatteryLevelFor(percent, state));

    [Fact]
    public void BatteryLevelFor_UnknownPercent() =>
        Assert.Equal(BatteryLevel.Unknown, LiveFormat.BatteryLevelFor(null, BatteryState.Normal));

    [Fact]
    public void Precision_ProgressAndSubtitle()
    {
        Assert.Equal(0, LiveFormat.PrecisionPercent(3, 0));
        Assert.Equal(30, LiveFormat.PrecisionPercent(3, 10), 10);
        Assert.Equal(100, LiveFormat.PrecisionPercent(12, 10));
        Assert.Equal("Looking for a quiet 2 s window  ·  3/10  ·  1.2 s",
            LiveFormat.PrecisionSubtitle(3, 10, 1234));
    }
}
