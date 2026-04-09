using LevelApp.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace LevelApp.App.DisplayModules.ParallelWaysDisplay;

/// <summary>
/// Renders a 2D schematic of Parallel Ways measurement results on a <see cref="Canvas"/>.
///
/// Each rail is drawn as a horizontal (or vertical) line.  Station dots are coloured
/// blue → green → red by their deviation from the best-fit line (straightness profile).
/// Along-rail measurement segments are drawn in accent blue.
/// Bridge measurement segments (if any) are drawn in orange.
///
/// Consistent with <see cref="SurfacePlot3D.SurfacePlot3DDisplay"/>: blue = low,
/// red = high deviation.
/// </summary>
public sealed class ParallelWaysDisplay
{
    public string DisplayId   => "parallel-ways-plot";
    public string DisplayName => "Parallel Ways Plot";

    private const double NodeRadius    = 6.0;
    private const double MarginX       = 60.0;
    private const double MarginY       = 40.0;
    private const double RailSpacingPx = 80.0;
    private const double TargetWidth   = 520.0;

    public object Render(
        ParallelWaysResult            result,
        ParallelWaysParameters        pwp,
        ParallelWaysStrategyParameters strat,
        IReadOnlyList<MeasurementStep> steps)
    {
        var canvas = new Canvas();
        if (result.RailProfiles.Count == 0) return canvas;

        // ── Layout ────────────────────────────────────────────────────────────

        // Determine the axial range across all rail profiles
        double axMin = result.RailProfiles
            .SelectMany(r => r.StationPositionsMm)
            .DefaultIfEmpty(0).Min();
        double axMax = result.RailProfiles
            .SelectMany(r => r.StationPositionsMm)
            .DefaultIfEmpty(1).Max();
        double axRange = Math.Max(axMax - axMin, 1.0);

        double plotWidth = TargetWidth - MarginX * 2;
        double scaleX    = plotWidth / axRange;

        double xOf(double posMm) => MarginX + (posMm - axMin) * scaleX;

        int numRails = pwp.Rails.Count;
        double canvasH = MarginY + numRails * RailSpacingPx + MarginY;
        double canvasW = TargetWidth;

        // Y position (canvas) for a given rail index
        double yOf(int railIdx) => MarginY + railIdx * RailSpacingPx;

        // ── Colour for a height deviation value (t ∈ [0,1]) ─────────────────
        double hMin = result.RailProfiles
            .SelectMany(r => r.HeightProfileMm).DefaultIfEmpty(0).Min();
        double hMax = result.RailProfiles
            .SelectMany(r => r.HeightProfileMm).DefaultIfEmpty(0).Max();
        double hRange = hMax > hMin ? hMax - hMin : 1.0;

        // ── Skeleton rail lines ───────────────────────────────────────────────
        var railLineBrush = new SolidColorBrush(Color.FromArgb(80, 160, 160, 160));

        for (int r = 0; r < numRails; r++)
        {
            double y = yOf(r);
            canvas.Children.Add(new Line
            {
                X1 = MarginX,      Y1 = y,
                X2 = MarginX + plotWidth, Y2 = y,
                Stroke          = railLineBrush,
                StrokeThickness = 1.5
            });

            // Rail label on the left
            canvas.Children.Add(new TextBlock
            {
                Text       = pwp.Rails[r].Label.Length > 0 ? pwp.Rails[r].Label : $"Rail {r + 1}",
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 80, 80, 80))
            }.WithPosition(4, y - 8));
        }

        // ── Along-rail step segments ──────────────────────────────────────────
        var alBrush = new SolidColorBrush(Color.FromArgb(120, 70, 130, 220));

        foreach (var step in steps.Where(s => IsAlongRailStep(s)))
        {
            var (rFrom, sFrom) = LevelApp.Core.Geometry.ParallelWays.Strategies.ParallelWaysStrategy
                .ParseNodeId(step.NodeId);
            var (rTo, sTo) = LevelApp.Core.Geometry.ParallelWays.Strategies.ParallelWaysStrategy
                .ParseNodeId(step.ToNodeId);
            if (rFrom != rTo) continue;

            var profile = result.RailProfiles.FirstOrDefault(p => p.RailIndex == rFrom);
            if (profile is null || profile.StationPositionsMm.Length == 0) continue;

            int nSta = profile.StationPositionsMm.Length;
            int fromSta = Math.Min(sFrom, sTo);
            int toSta   = Math.Max(sFrom, sTo);
            if (fromSta >= nSta || toSta >= nSta) continue;

            double x1 = xOf(profile.StationPositionsMm[fromSta]);
            double x2 = xOf(profile.StationPositionsMm[toSta]);
            double y  = yOf(rFrom);

            canvas.Children.Add(new Line
            {
                X1 = x1, Y1 = y, X2 = x2, Y2 = y,
                Stroke = alBrush, StrokeThickness = 2.0
            });
        }

        // ── Bridge step segments ──────────────────────────────────────────────
        var brBrush = new SolidColorBrush(Color.FromArgb(140, 200, 100, 30));

        foreach (var step in steps.Where(s => !IsAlongRailStep(s)
                                           && s.PassPhase != PassPhase.Return))
        {
            var (rFrom, sSta) = LevelApp.Core.Geometry.ParallelWays.Strategies.ParallelWaysStrategy
                .ParseNodeId(step.NodeId);
            var (rTo, _) = LevelApp.Core.Geometry.ParallelWays.Strategies.ParallelWaysStrategy
                .ParseNodeId(step.ToNodeId);

            var profA = result.RailProfiles.FirstOrDefault(p => p.RailIndex == rFrom);
            if (profA is null || sSta >= profA.StationPositionsMm.Length) continue;

            double x  = xOf(profA.StationPositionsMm[sSta]);
            double y1 = yOf(rFrom);
            double y2 = yOf(rTo);

            canvas.Children.Add(new Line
            {
                X1 = x, Y1 = y1, X2 = x, Y2 = y2,
                Stroke = brBrush, StrokeThickness = 2.0
            });
        }

        // ── Station dots ──────────────────────────────────────────────────────
        foreach (var profile in result.RailProfiles)
        {
            double y = yOf(profile.RailIndex);

            for (int s = 0; s < profile.StationPositionsMm.Length; s++)
            {
                double x = xOf(profile.StationPositionsMm[s]);
                double h = profile.HeightProfileMm[s];
                double t = (h - hMin) / hRange;
                var    c = HeightColor(t);

                var dot = new Ellipse
                {
                    Width  = NodeRadius * 2,
                    Height = NodeRadius * 2,
                    Fill   = new SolidColorBrush(c)
                };
                Canvas.SetLeft(dot, x - NodeRadius);
                Canvas.SetTop(dot,  y - NodeRadius);
                canvas.Children.Add(dot);
            }
        }

        canvas.Width  = canvasW;
        canvas.Height = canvasH;
        return canvas;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsAlongRailStep(MeasurementStep step)
    {
        var (rFrom, _) = LevelApp.Core.Geometry.ParallelWays.Strategies.ParallelWaysStrategy
            .ParseNodeId(step.NodeId);
        var (rTo, _) = LevelApp.Core.Geometry.ParallelWays.Strategies.ParallelWaysStrategy
            .ParseNodeId(step.ToNodeId);
        return rFrom == rTo;
    }

    private static Color HeightColor(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t switch
        {
            <= 0.5 => Lerp(Colors.Blue, Colors.Lime,   t / 0.5),
            _      => Lerp(Colors.Lime, Colors.Red,    (t - 0.5) / 0.5)
        };
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromArgb(255,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
}

/// <summary>Extension to position a TextBlock on a Canvas.</summary>
file static class CanvasExtensions
{
    public static TextBlock WithPosition(this TextBlock tb, double left, double top)
    {
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb,  top);
        return tb;
    }
}
