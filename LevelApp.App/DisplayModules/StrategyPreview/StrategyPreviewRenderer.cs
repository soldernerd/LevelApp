using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace LevelApp.App.DisplayModules.StrategyPreview;

/// <summary>
/// Renders a 2-D top-down preview of a measurement strategy on a <see cref="Canvas"/>.
/// Nodes are drawn as small filled circles; edges connect consecutive steps within
/// each pass.  Physical plate proportions are preserved.
/// Pass colours are semi-transparent hues chosen to distinguish passes in both themes.
/// Node fill and stroke colours are resolved from the app's ThemeColors resource dictionary.
/// </summary>
public static class StrategyPreviewRenderer
{
    private const double NodeRadius  = 4.0;
    private const double Margin      = 16.0;

    public static Canvas Render(
        IMeasurementStrategy strategy,
        ObjectDefinition     definition,
        double               previewWidth = 440)
    {
        var canvas = new Canvas();

        var steps = strategy.GenerateSteps(definition);
        if (steps.Count == 0) return canvas;

        // ── Collect all node physical positions ───────────────────────────────
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

        // ── Scale to fit previewWidth preserving aspect ratio ─────────────────
        double innerW = previewWidth - Margin * 2;
        double innerH = innerW * (physH / physW);

        double scaleX = innerW / physW;
        double scaleY = innerH / physH;

        (double sx, double sy) ToScreen(double px, double py) =>
            (Margin + (px - physMinX) * scaleX,
             Margin + (py - physMinY) * scaleY);

        // ── Edges (per pass, consecutive steps) ───────────────────────────────
        var passBuckets = new Dictionary<int, List<MeasurementStep>>();
        foreach (var step in steps)
        {
            if (!passBuckets.TryGetValue(step.PassId, out var bucket))
                passBuckets[step.PassId] = bucket = [];
            bucket.Add(step);
        }

        // Semi-transparent pass colours — chosen for visibility in both Light and Dark themes
        var passColors = new[]
        {
            Windows.UI.Color.FromArgb(180, 70,  130, 220),
            Windows.UI.Color.FromArgb(180, 220, 80,  80),
            Windows.UI.Color.FromArgb(180, 60,  180, 100),
            Windows.UI.Color.FromArgb(180, 200, 150, 30),
            Windows.UI.Color.FromArgb(180, 160, 80,  200),
            Windows.UI.Color.FromArgb(180, 30,  180, 200),
            Windows.UI.Color.FromArgb(180, 200, 100, 150),
            Windows.UI.Color.FromArgb(180, 120, 140, 60),
        };

        int passColorIdx = 0;
        foreach (var (passId, bucket) in passBuckets.OrderBy(kv => kv.Key))
        {
            var color     = passColors[passColorIdx % passColors.Length];
            var lineBrush = new SolidColorBrush(color);
            passColorIdx++;

            foreach (var step in bucket)
            {
                if (!nodePos.TryGetValue(step.NodeId,   out var fromPos)) continue;
                if (!nodePos.TryGetValue(step.ToNodeId, out var toPos))   continue;

                var (x1, y1) = ToScreen(fromPos.X, fromPos.Y);
                var (x2, y2) = ToScreen(toPos.X,   toPos.Y);

                canvas.Children.Add(new Line
                {
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke          = lineBrush,
                    StrokeThickness = 1.5
                });
            }
        }

        // ── Nodes ─────────────────────────────────────────────────────────────
        // Node fill uses the canvas background colour so they contrast with lines;
        // stroke uses the current step accent colour.
        var nodeFillColor   = GetThemeColor(canvas, "GridCanvasBackgroundBrush");
        var nodeStrokeColor = GetThemeColor(canvas, "GridCurrentStepBrush");
        var nodeFill        = new SolidColorBrush(nodeFillColor);
        var nodeStroke      = new SolidColorBrush(nodeStrokeColor);

        foreach (var (nodeId, (px, py)) in nodePos)
        {
            var (sx, sy) = ToScreen(px, py);
            var ellipse = new Ellipse
            {
                Width           = NodeRadius * 2,
                Height          = NodeRadius * 2,
                Fill            = nodeFill,
                Stroke          = nodeStroke,
                StrokeThickness = 1.0
            };
            Canvas.SetLeft(ellipse, sx - NodeRadius);
            Canvas.SetTop(ellipse,  sy - NodeRadius);
            canvas.Children.Add(ellipse);
        }

        // ── Canvas dimensions ─────────────────────────────────────────────────
        canvas.Width  = previewWidth;
        canvas.Height = innerH + Margin * 2;

        return canvas;
    }

    // ── Theme helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a named theme brush colour from the resource dictionary.
    /// Checks the element's own resources first, then the application resources.
    /// </summary>
    private static Color GetThemeColor(FrameworkElement element, string resourceKey)
    {
        if (element.Resources.TryGetValue(resourceKey, out var res)
            || Application.Current.Resources.TryGetValue(resourceKey, out res))
        {
            return res is SolidColorBrush brush ? brush.Color : Colors.Gray;
        }
        return Colors.Gray;
    }
}
