using System.Globalization;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>How a live value is to be drawn: no reading, a normal one, or a disturbed ("doubtful") one.</summary>
public enum ReadingState { None, Ok, Doubtful }

/// <summary>Urgency of the battery fill colour.</summary>
public enum BatteryLevel { Unknown, Critical, Low, Ok }

/// <summary>
/// Display formatting and the small pure rules of the live screen (port of the phone app's
/// <c>format.dart</c> and <c>tare_provider.dart</c> arithmetic). Signs use a real minus (U+2212).
/// </summary>
public static class LiveFormat
{
    private const char Minus = '−';
    private const char Dash = '–';

    /// <summary>"––.–––––" style placeholder for a value that is not available.</summary>
    public static string Placeholder(int decimals = 5) => $"{Dash}{Dash}.{new string(Dash, decimals)}";

    private static string Sign(double v) => v < 0 ? Minus.ToString() : "+";

    /// <summary>Tilt in mm/m with an explicit sign: <c>+0.12345</c>; a placeholder for null / non-finite.</summary>
    public static string Tilt(double? v, int decimals = 4) =>
        v is null || !double.IsFinite(v.Value)
            ? Placeholder(decimals)
            : Sign(v.Value) + Math.Abs(v.Value).ToString("F" + decimals, CultureInfo.InvariantCulture);

    /// <summary>A signed number with <paramref name="decimals"/> decimals and an optional unit; "—" for null / non-finite.</summary>
    public static string Signed(double? v, int decimals = 2, string unit = "")
    {
        if (v is null || !double.IsFinite(v.Value)) return "—";
        var s = Sign(v.Value) + Math.Abs(v.Value).ToString("F" + decimals, CultureInfo.InvariantCulture);
        return unit.Length == 0 ? s : s + " " + unit;
    }

    /// <summary>Battery label: <c>85%</c>, or "—" when unknown.</summary>
    public static string Percent(int? percent) =>
        percent is null ? "—" : percent.Value.ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>The Diff as shown: the device's Diff minus the Zero Diff offset (null stays null).</summary>
    public static double? ApplyTare(double? diff, double? tare) => diff is null ? null : diff.Value - (tare ?? 0);

    /// <summary>How a value is drawn: dim without a reading, amber when disturbed.</summary>
    public static ReadingState StateOf(bool hasValue, bool doubtful) =>
        !hasValue ? ReadingState.None : doubtful ? ReadingState.Doubtful : ReadingState.Ok;

    /// <summary>The fill urgency of the battery glyph.</summary>
    public static BatteryLevel BatteryLevelFor(int? percent, BatteryState state)
    {
        if (state == BatteryState.Critical) return BatteryLevel.Critical;
        if (state == BatteryState.Low) return BatteryLevel.Low;
        if (percent is null) return BatteryLevel.Unknown;
        if (percent <= 15) return BatteryLevel.Critical;
        if (percent <= 35) return BatteryLevel.Low;
        return BatteryLevel.Ok;
    }

    /// <summary>Precision run progress 0..100 (0 when the target is not known yet).</summary>
    public static double PrecisionPercent(int count, int target) =>
        target <= 0 ? 0 : Math.Clamp(count * 100.0 / target, 0, 100);

    /// <summary>"Looking for a quiet 2 s window  ·  3/10  ·  1.2 s".</summary>
    public static string PrecisionSubtitle(int count, int target, long elapsedMs) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Looking for a quiet 2 s window  ·  {count}/{target}  ·  {elapsedMs / 1000.0:F1} s");
}
