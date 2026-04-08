using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace LevelApp.App.DisplayModules.SurfacePlot3D;

/// <summary>
/// Renders a pseudo-3D isometric surface plot on a <see cref="Canvas"/>.
///
/// Graph-driven: nodes and edges come from the step list. Edges are drawn between
/// consecutive steps within the same pass (identified by PassId). Physical x/y
/// positions come from <see cref="IMeasurementStrategy.GetNodePosition"/>, so the
/// display correctly reflects plate proportions for any strategy.
///
/// Nodes are coloured blue (low) → cyan → green → yellow → red (high).
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
        // Collect all node physical positions to determine bounding box.
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
        // Map physical (x, y) in mm to isometric pixel offsets.
        // We choose IsoW and IsoH so that the total canvas width is ~500 px.
        const double TargetWidth = 500.0;
        double isoW = TargetWidth / (physW + physH) * (physW / (physW + physH) * 2);
        double isoH = isoW * 0.5 * (physH / physW);
        isoW = Math.Max(isoW, 10.0);
        isoH = Math.Max(isoH, 5.0);

        double maxZPixels = Math.Max(10.0, (physW / physH + 1) * isoH * 4 * 0.20);

        // originX places (physMinX, physMinY) node at screen left accounting for y-depth offset
        double originX = (physH / physH) * isoW * (physH / (physW + physH) * (physW + physH) / physW) + Margin;
        // Simpler: use a scale factor
        double scaleX = TargetWidth / (physW + physH);
        double scaleY = scaleX * 0.5;
        originX = scaleX * (physMaxY - physMinY) + Margin;
        double originY = maxZPixels + Margin;

        // Screen position for a physical node
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

        // ── Edges (graph-driven, between consecutive steps in same pass) ───────
        var edgeBrush = new SolidColorBrush(Color.FromArgb(100, 160, 160, 160));

        // Group steps by PassId; within each group, draw node[i] → node[i+1]
        var passBuckets = new Dictionary<int, List<MeasurementStep>>();
        foreach (var step in steps)
        {
            if (!passBuckets.TryGetValue(step.PassId, out var bucket))
                passBuckets[step.PassId] = bucket = [];
            bucket.Add(step);
        }

        foreach (var bucket in passBuckets.Values)
        {
            // Draw edge from each step's from-node to its to-node
            foreach (var step in bucket)
            {
                if (!nodePos.ContainsKey(step.NodeId) || !nodePos.ContainsKey(step.ToNodeId))
                    continue;
                canvas.Children.Add(MakeLine(ScreenPos(step.NodeId), ScreenPos(step.ToNodeId), edgeBrush));
            }
        }

        // ── Nodes (painter order: sort by screen y descending = back-to-front) ─
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
                Fill   = new SolidColorBrush(HeightColor(t))
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

    private static Color HeightColor(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t switch
        {
            <= 0.25 => Lerp(Colors.Blue,   Colors.Cyan,   t / 0.25),
            <= 0.50 => Lerp(Colors.Cyan,   Colors.Lime,   (t - 0.25) / 0.25),
            <= 0.75 => Lerp(Colors.Lime,   Colors.Yellow, (t - 0.50) / 0.25),
            _       => Lerp(Colors.Yellow, Colors.Red,    (t - 0.75) / 0.25)
        };
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromArgb(255,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
}
