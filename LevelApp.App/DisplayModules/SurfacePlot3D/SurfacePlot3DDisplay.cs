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
/// Nodes are coloured blue (low) → cyan → green → yellow → red (high).
/// The z-axis is exaggerated so even sub-µm height differences are visible.
/// </summary>
public sealed class SurfacePlot3DDisplay : IResultDisplay
{
    public string DisplayId   => "surface-plot-3d";
    public string DisplayName => "3D Surface Plot";

    // ── Layout constants ──────────────────────────────────────────────────────
    private const double IsoW       = 30.0;  // px per grid unit, horizontal axis
    private const double IsoH       = 15.0;  // px per grid unit, depth axis
    private const double MaxZPixels = 60.0;  // total vertical pixel range for heights
    private const double NodeRadius = 5.0;
    private const double Margin     = 24.0;

    // ── IResultDisplay ────────────────────────────────────────────────────────

    public object Render(SurfaceResult result)
    {
        double[][] h = result.HeightMapMm;
        int rows = h.Length;
        int cols  = rows > 0 ? h[0].Length : 0;

        var canvas = new Canvas();
        if (rows == 0 || cols == 0) return canvas;

        double hMin   = h.SelectMany(r => r).Min();
        double hMax   = h.SelectMany(r => r).Max();
        double hRange = hMax > hMin ? hMax - hMin : 1.0;

        // Origin: node (0,0) sits here on screen
        double originX = (rows - 1) * IsoW + Margin;
        double originY = MaxZPixels + Margin;

        // Isometric screen position for grid node (col, row)
        (double sx, double sy) Pos(int col, int row)
        {
            double t   = (h[row][col] - hMin) / hRange;
            double scx = originX + (col - row) * IsoW;
            double scy = originY + (col + row) * IsoH - t * MaxZPixels;
            return (scx, scy);
        }

        // ── Grid edges ────────────────────────────────────────────────────────

        var edgeBrush = new SolidColorBrush(Color.FromArgb(100, 160, 160, 160));

        // Horizontal edges (East)
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols - 1; c++)
                canvas.Children.Add(MakeLine(Pos(c, r), Pos(c + 1, r), edgeBrush));

        // Vertical edges (South)
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows - 1; r++)
                canvas.Children.Add(MakeLine(Pos(c, r), Pos(c, r + 1), edgeBrush));

        // ── Nodes (painter order: ascending col + row) ────────────────────────
        for (int sum = 0; sum < cols + rows - 1; sum++)
        {
            for (int r = Math.Max(0, sum - cols + 1); r <= Math.Min(rows - 1, sum); r++)
            {
                int c = sum - r;
                if (c < 0 || c >= cols) continue;

                double t       = (h[r][c] - hMin) / hRange;
                var (sx, sy)   = Pos(c, r);
                Color colour   = HeightColor(t);

                var ellipse = new Ellipse
                {
                    Width  = NodeRadius * 2,
                    Height = NodeRadius * 2,
                    Fill   = new SolidColorBrush(colour)
                };
                Canvas.SetLeft(ellipse, sx - NodeRadius);
                Canvas.SetTop(ellipse,  sy - NodeRadius);
                canvas.Children.Add(ellipse);
            }
        }

        // ── Canvas dimensions ─────────────────────────────────────────────────
        canvas.Width  = (cols - 1 + rows - 1) * IsoW + Margin * 2;
        canvas.Height = (cols - 1 + rows - 1) * IsoH + MaxZPixels + Margin * 2;

        return canvas;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Line MakeLine(
        (double x, double y) p1,
        (double x, double y) p2,
        Brush stroke) => new()
        {
            X1 = p1.x, Y1 = p1.y,
            X2 = p2.x, Y2 = p2.y,
            Stroke = stroke,
            StrokeThickness = 1.0
        };

    /// <summary>
    /// Maps t ∈ [0,1] to the blue → cyan → green → yellow → red gradient.
    /// </summary>
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
        Color.FromArgb(
            255,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
}
