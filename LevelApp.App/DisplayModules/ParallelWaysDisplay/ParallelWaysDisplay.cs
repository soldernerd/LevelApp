using LevelApp.App.Helpers;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LevelApp.App.DisplayModules.ParallelWaysDisplay;

/// <summary>
/// Renders a 2D schematic of Parallel Ways measurement results on a <see cref="Canvas"/>.
///
/// Each rail is drawn as a horizontal (or vertical) line.  Station dots are coloured
/// using the themed five-stop height ramp (low → mid-low → mid → mid-high → high).
/// Along-rail measurement segments are drawn in the current-step accent colour.
/// Bridge measurement segments are drawn in the flagged-step accent colour.
/// All colours are resolved from the app's ThemeColors resource dictionary at render time.
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

        double yOf(int railIdx) => MarginY + railIdx * RailSpacingPx;

        double hMin = result.RailProfiles
            .SelectMany(r => r.HeightProfileMm).DefaultIfEmpty(0).Min();
        double hMax = result.RailProfiles
            .SelectMany(r => r.HeightProfileMm).DefaultIfEmpty(0).Max();
        double hRange = hMax > hMin ? hMax - hMin : 1.0;

        // ── Resolve theme colours ─────────────────────────────────────────────
        var railLineBrush  = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridPendingStepBrush"))
            { Opacity = 0.5 };
        var railLabelBrush = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridStepArrowBrush"));
        var alBrush        = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridCurrentStepBrush"))
            { Opacity = 0.6 };
        var brBrush        = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridFlaggedStepBrush"))
            { Opacity = 0.7 };

        // Resolve the plot ramp once — avoids five resource lookups per station dot.
        var ramp = ThemeHelper.GetPlotRamp(canvas);

        // ── Skeleton rail lines ───────────────────────────────────────────────
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

            canvas.Children.Add(new TextBlock
            {
                Text       = pwp.Rails[r].Label.Length > 0 ? pwp.Rails[r].Label : $"Rail {r + 1}",
                FontSize   = 11,
                Foreground = railLabelBrush
            }.WithPosition(4, y - 8));
        }

        // ── Along-rail step segments ──────────────────────────────────────────
        foreach (var step in steps.Where(s => IsAlongRailStep(s)))
        {
            var (rFrom, sFrom) = LevelApp.Core.Geometry.ParallelWays.Strategies.ParallelWaysStrategy
                .ParseNodeId(step.NodeId);
            var (rTo, sTo) = LevelApp.Core.Geometry.ParallelWays.Strategies.ParallelWaysStrategy
                .ParseNodeId(step.ToNodeId);
            if (rFrom != rTo) continue;

            var profile = result.RailProfiles.FirstOrDefault(p => p.RailIndex == rFrom);
            if (profile is null || profile.StationPositionsMm.Length == 0) continue;

            int nSta    = profile.StationPositionsMm.Length;
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

                var dot = new Ellipse
                {
                    Width  = NodeRadius * 2,
                    Height = NodeRadius * 2,
                    Fill   = new SolidColorBrush(ThemeHelper.InterpolateRamp(ramp, t))
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
