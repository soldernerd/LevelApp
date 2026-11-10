using System.Globalization;

namespace LevelApp.Instruments.Leveltronic.Diagnostics;

/// <summary>Pure maths and formatting of the diagnostics screen (port of diagnostics_screen.dart / format.dart).</summary>
public static class DiagnosticsMath
{
    /// <summary>Folds an angle into [0°, 360°), the range the measured phases use.</summary>
    public static double Wrap360(double deg) => ((deg % 360) + 360) % 360;

    /// <summary>
    /// Measured phase minus the stored signal phase (in 0.01°), folded into [0°, 360°): follows
    /// A (90°) for a positive tilt, B (270°) for a negative one. Null when the stored phase is unknown.
    /// </summary>
    public static double? CalibratedPhase(double measuredDeg, long? storedCentiDeg) =>
        storedCentiDeg is { } s ? Wrap360(measuredDeg - s / 100.0) : null;

    /// <summary>Index into the S1/S2 arrays (0 = S1, 1 = S2) of a signal name, or null for A and B.</summary>
    public static int? SensorIndex(string name) => name switch { "S1" => 0, "S2" => 1, _ => null };

    /// <summary>Voltage with finer resolution: three decimals below 100 mV, else two.</summary>
    public static string FormatMv(double v) =>
        v.ToString(Math.Abs(v) < 100 ? "0.000" : "0.00", CultureInfo.InvariantCulture) + " mV";

    /// <summary>Signed number with an explicit sign and a real minus (U+2212); "—" if not finite.</summary>
    public static string FormatSigned(double v, int decimals = 2)
    {
        if (!double.IsFinite(v)) return "—";
        return (v < 0 ? "−" : "+") + Math.Abs(v).ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    /// <summary>Tilt in mm/m, four decimals and explicit sign: "+0.1234".</summary>
    public static string FormatTilt(double v) =>
        double.IsFinite(v) ? FormatSigned(v, 4) : "––.––––";

    /// <summary>"PGA ×16", or empty where the firmware cannot tell.</summary>
    public static string FormatPga(long? pga) => pga is { } p ? $"PGA ×{p}" : "";

    /// <summary>"ADC ch2".</summary>
    public static string FormatAdc(int channel) => $"ADC ch{channel}";

    /// <summary>The RMS bar fill: rms / max, clamped to 0…1 (0 when max ≤ 0).</summary>
    public static double RmsFraction(double rms, double maxRms) =>
        maxRms <= 0 ? 0 : Math.Clamp(rms / maxRms, 0, 1);
}
