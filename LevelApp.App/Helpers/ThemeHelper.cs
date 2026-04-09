using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LevelApp.App.Helpers;

/// <summary>
/// Resolves named colours from the active theme's resource dictionary.
/// Checks the supplied element's own resources first, then falls back to
/// application-level resources (where ThemeColors.xaml is merged).
/// Falls back to <see cref="Colors.Gray"/> when a key is not found.
/// </summary>
internal static class ThemeHelper
{
    /// <summary>Resolves a named theme colour.</summary>
    internal static Color GetColor(FrameworkElement element, string resourceKey)
    {
        if (element.Resources.TryGetValue(resourceKey, out var res)
            || Application.Current.Resources.TryGetValue(resourceKey, out res))
        {
            return res is SolidColorBrush brush ? brush.Color : Colors.Gray;
        }
        return Colors.Gray;
    }

    /// <summary>
    /// Resolves a named theme brush, preserving any alpha channel baked into
    /// the colour (e.g. semi-transparent loop closure fills).
    /// </summary>
    internal static SolidColorBrush GetBrush(FrameworkElement element, string resourceKey)
    {
        if (element.Resources.TryGetValue(resourceKey, out var res)
            || Application.Current.Resources.TryGetValue(resourceKey, out res))
        {
            return res is SolidColorBrush brush ? brush : new SolidColorBrush(Colors.Gray);
        }
        return new SolidColorBrush(Colors.Gray);
    }

    /// <summary>
    /// Resolves the five plot-ramp anchor colours in a single call.
    /// Call this once before any per-node rendering loop; pass the result to
    /// <see cref="InterpolateRamp"/>.
    /// </summary>
    internal static PlotRamp GetPlotRamp(FrameworkElement element) => new(
        GetColor(element, "PlotLowBrush"),
        GetColor(element, "PlotMidLowBrush"),
        GetColor(element, "PlotMidBrush"),
        GetColor(element, "PlotMidHighBrush"),
        GetColor(element, "PlotHighBrush"));

    /// <summary>
    /// Maps a normalised height <paramref name="t"/> ∈ [0,1] to a colour by
    /// linearly interpolating across the four equal segments of the ramp.
    /// </summary>
    internal static Color InterpolateRamp(PlotRamp ramp, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t switch
        {
            <= 0.25 => Lerp(ramp.Low,     ramp.MidLow,  t / 0.25),
            <= 0.50 => Lerp(ramp.MidLow,  ramp.Mid,     (t - 0.25) / 0.25),
            <= 0.75 => Lerp(ramp.Mid,     ramp.MidHigh, (t - 0.50) / 0.25),
            _       => Lerp(ramp.MidHigh, ramp.High,    (t - 0.75) / 0.25)
        };
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromArgb(255,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
}

/// <summary>
/// Pre-resolved five-stop colour ramp used for height-map node colouring.
/// Obtain an instance with <see cref="ThemeHelper.GetPlotRamp"/> once per render
/// pass, then pass to <see cref="ThemeHelper.InterpolateRamp"/> for each node.
/// </summary>
internal readonly record struct PlotRamp(
    Color Low,
    Color MidLow,
    Color Mid,
    Color MidHigh,
    Color High);
