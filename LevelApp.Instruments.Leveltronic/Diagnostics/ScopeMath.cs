using System.Globalization;

namespace LevelApp.Instruments.Leveltronic.Diagnostics;

/// <summary>
/// Pure maths of the oscilloscope screen (port of the phone app's scope_screen.dart).
/// The instrument reports amplitude and phase per channel, not samples, so every trace is
/// rebuilt as <c>peak · sin(ωt + φ)</c> over <see cref="SpanDeg"/>.
/// </summary>
public static class ScopeMath
{
    /// <summary>The ADC's input range, ± this many mV (excitation panel).</summary>
    public const double FullScaleMv = 1200;

    /// <summary>
    /// The selectable full scales of the sensor panel, ± mV. 10 and 5 mV are for the small
    /// signals of the original Wyler sensors.
    /// </summary>
    public static readonly IReadOnlyList<double> ScalesMv = [1200, 600, 200, 100, 50, 20, 10, 5];

    /// <summary>Vertical shift of S2 against S1 as a fraction of the panel's half height.</summary>
    public const double S2Shift = 0.02;

    /// <summary>Horizontal extent of the plot: 1¼ periods.</summary>
    public const double SpanDeg = 450;

    /// <summary>Number of line segments of a trace.</summary>
    public const int Steps = 300;

    public const int GridCols = 10;
    public const int GridRows = 8;

    /// <summary>
    /// The trace as <c>steps + 1</c> values in units of the half panel height
    /// (+1 = top edge, −1 = bottom edge, not clipped).
    /// </summary>
    public static double[] Trace(double peakMv, double phaseDeg, double fullScaleMv,
        int steps = Steps, double spanDeg = SpanDeg)
    {
        var v = new double[steps + 1];
        for (int k = 0; k <= steps; k++)
        {
            double deg = spanDeg * k / steps;
            v[k] = peakMv * Math.Sin((deg + phaseDeg) * Math.PI / 180) / fullScaleMv;
        }
        return v;
    }

    /// <summary>Voltage of horizontal grid line <paramref name="j"/> (1 … rows−1), in mV.</summary>
    public static double GridLineMv(double fullScaleMv, int j, int rows = GridRows) =>
        fullScaleMv * (rows / 2.0 - j) / (rows / 2.0);

    /// <summary>Axis label of a grid voltage: "0", "+600", "−600" (real minus sign).</summary>
    public static string GridLabel(double mv)
    {
        if (mv == 0) return "0";
        return (mv > 0 ? "+" : "−") + Math.Abs(mv).ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary>Legend text of a trace: "S1  12.34 mV p–p" (two decimals below 50 mV peak).</summary>
    public static string LegendText(string name, double peakMv)
    {
        string fmt = peakMv < 50 ? "0.00" : "0.0";
        return $"{name}  {(peakMv * 2).ToString(fmt, CultureInfo.InvariantCulture)} mV p–p";
    }

    /// <summary>"±600 mV full scale".</summary>
    public static string FullScaleText(double fullScaleMv) =>
        $"±{fullScaleMv.ToString("0", CultureInfo.InvariantCulture)} mV full scale";

    /// <summary>"S1/S2 ±600 mV", the entry of the scale menu.</summary>
    public static string ScaleItemText(double fullScaleMv) =>
        $"S1/S2 ±{fullScaleMv.ToString("0", CultureInfo.InvariantCulture)} mV";
}
