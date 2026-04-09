using LevelApp.App.Helpers;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LevelApp.App.DisplayModules.SurfacePlot3D;

/// <summary>
/// Renders a pseudo-3D isometric surface plot on a <see cref="Canvas"/>.
///
/// Graph-driven: nodes and edges come from the step list. Edges are drawn between
/// consecutive steps within the same pass (identified by PassId). Physical x/y
/// positions come from <see cref="IMeasurementStrategy.GetNodePosition"/>, so the
/// display correctly reflects plate proportions for any strategy.
///
/// Nodes are coloured low (blue) → mid-low (green) → mid (yellow) → mid-high (orange) → high (red).
/// Colours are resolved from the app's ThemeColors resource dictionary at render time.
/// </summary>
public sealed class SurfacePlot3DDisplay
{
    public string DisplayId   => "surface-plot-3d";
    public string DisplayName => "3D Surface Plot";

    private const double NodeRadius = 5.0;
    private const double Margin     = 24.0;

    public object Render(SurfaceResult result, IMeasurementStrategy strategy,
                         ObjectDefinition definition, IReadOnlyList<MeasurementStep> steps)
    {
        var canvas = new Canvas();
        if (result.NodeHeights.Count == 0 || steps.Count == 0) return canvas;

        double hMin   = result.NodeHeights.Values.Min();
        double hMax   = result.NodeHeights.Values.Max();
        double hRange = hMax > hMin ? hMax - hMin : 1.0;

        // ── Physical coordinate ranges ─────────────────────────────────────────
        var nodePos = new Dictionary<string, (double X, double Y)>();
        foreach (var step in steps)
        {
            if (!nodePos.ContainsKey(step.NodeId))
                nodePos[step.NodeId] = strategy.GetNodePosition(step, definition);
            if (!nodePos.ContainsKey(step.ToNodeId))
                nodePos[step.ToNodeId] = strategy.GetToNodePosition(step, definition);
        }

        double physMinX = nodePos.Values.Min(p => p.X);
        double physMaxX = nodePos.Values.Max(p => p.X);
        double physMinY = nodePos.Values.Min(p => p.Y);
        double physMaxY = nodePos.Values.Max(p => p.Y);
        double physW    = physMaxX - physMinX;
        double physH    = physMaxY - physMinY;
        if (physW < 1e-6) physW = 1.0;
        if (physH < 1e-6) physH = 1.0;

        // ── Isometric projection constants ────────────────────────────────────
        const double TargetWidth = 500.0;
        double isoW = TargetWidth / (physW + physH) * (physW / (physW + physH) * 2);
        double isoH = isoW * 0.5 * (physH / physW);
        isoW = Math.Max(isoW, 10.0);
        isoH = Math.Max(isoH, 5.0);

        double maxZPixels = Math.Max(10.0, (physW / physH + 1) * isoH * 4 * 0.20);

        double scaleX  = TargetWidth / (physW + physH);
        double scaleY  = scaleX * 0.5;
        double originX = scaleX * (physMaxY - physMinY) + Margin;
        double originY = maxZPixels + Margin;

        (double sx, double sy) ScreenPos(string nodeId)
        {
            var (px, py) = nodePos[nodeId];
            double normX = (px - physMinX) / physW;
            double normY = (py - physMinY) / physH;
            double t     = result.NodeHeights.TryGetValue(nodeId, out double hv)
                ? (hv - hMin) / hRange : 0.0;

            double sx = originX + (normX * physW - normY * physH) * scaleX;
            double sy = originY + (normX * physW + normY * physH) * scaleY - t * maxZPixels;
            return (sx, sy);
        }

        // ── Edges ──────────────────────────────────────────────────────────────
        // Use canvas as the FrameworkElement owner; falls through to App resources.
        // Edges are drawn faint to keep focus on node colours.
        var edgeBrush = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridStepArrowBrush"))
            { Opacity = 0.4 };

        var passBuckets = new Dictionary<int, List<MeasurementStep>>();
        foreach (var step in steps)
        {
            if (!passBuckets.TryGetValue(step.PassId, out var bucket))
                passBuckets[step.PassId] = bucket = [];
            bucket.Add(step);
        }

        foreach (var bucket in passBuckets.Values)
        {
            foreach (var step in bucket)
            {
                if (!nodePos.ContainsKey(step.NodeId) || !nodePos.ContainsKey(step.ToNodeId))
                    continue;
                canvas.Children.Add(MakeLine(ScreenPos(step.NodeId), ScreenPos(step.ToNodeId), edgeBrush));
            }
        }

        // ── Nodes (painter order: back-to-front by screen y) ──────────────────
        // Resolve the plot ramp once — avoids five resource lookups per node.
        var ramp = ThemeHelper.GetPlotRamp(canvas);

        var allNodes = nodePos.Keys
            .Where(id => result.NodeHeights.ContainsKey(id))
            .OrderBy(id => { var (_, sy) = ScreenPos(id); return -sy; })
            .ToList();

        foreach (var nodeId in allNodes)
        {
            double hv    = result.NodeHeights[nodeId];
            double t     = (hv - hMin) / hRange;
            var (sx, sy) = ScreenPos(nodeId);

            var ellipse = new Ellipse
            {
                Width  = NodeRadius * 2,
                Height = NodeRadius * 2,
                Fill   = new SolidColorBrush(ThemeHelper.InterpolateRamp(ramp, t))
            };
            Canvas.SetLeft(ellipse, sx - NodeRadius);
            Canvas.SetTop(ellipse,  sy - NodeRadius);
            canvas.Children.Add(ellipse);
        }

        // ── Canvas dimensions ─────────────────────────────────────────────────
        double canvasW = (physW + physH) * scaleX + Margin * 2;
        double canvasH = (physW + physH) * scaleY + maxZPixels + Margin * 2;
        canvas.Width  = canvasW;
        canvas.Height = canvasH;

        return canvas;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Line MakeLine(
        (double x, double y) p1, (double x, double y) p2, Brush stroke) => new()
    {
        X1 = p1.x, Y1 = p1.y, X2 = p2.x, Y2 = p2.y,
        Stroke = stroke, StrokeThickness = 1.0
    };
}
