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
    private const double NodeRadius = 5.0;
    private const double Margin     = 24.0;

    // MaxZPixels is computed per render from the grid size — see Render().
    // It must not exceed ~20% of the grid's total isometric depth so that
    // z-displacement can never visually push a node past an adjacent one.

    // ── IResultDisplay ────────────────────────────────────────────────────────

    public object Render(SurfaceResult result)
    {
        double[][] h = result.HeightMapMm;

        // HeightMapMm is indexed [nodeRow][nodeCol], matching gridRow/gridCol exactly.
        int nodeRows = h.Length;
        int nodeCols = nodeRows > 0 ? h[0].Length : 0;

        var canvas = new Canvas();
        if (nodeRows == 0 || nodeCols == 0) return canvas;

        double hMin   = h.SelectMany(row => row).Min();
        double hMax   = h.SelectMany(row => row).Max();
        double hRange = hMax > hMin ? hMax - hMin : 1.0;

        int colIntervals = nodeCols - 1;
        int rowIntervals = nodeRows - 1;

        // Z-scale: cap at 20% of the grid's total isometric depth so that
        // z-displacement can never visually push a node past an adjacent one.
        // Without this cap, large exaggeration scrambles the apparent connectivity
        // even though the edges are topologically correct.
        // Floor of 10px keeps something visible on very small grids.
        double maxZPixels = Math.Max(10.0, (colIntervals + rowIntervals) * IsoH * 0.20);

        // Origin: node (gridCol=0, gridRow=0) maps to this screen point.
        // originX must account for the deepest row pushing nodes leftward by rowIntervals*IsoW.
        double originX = rowIntervals * IsoW + Margin;
        double originY = maxZPixels + Margin;

        // Screen position for a node — derived purely from (gridCol, gridRow).
        (double sx, double sy) Pos(int gridCol, int gridRow)
        {
            double t   = (h[gridRow][gridCol] - hMin) / hRange;
            double scx = originX + (gridCol - gridRow) * IsoW;
            double scy = originY + (gridCol + gridRow) * IsoH - t * maxZPixels;
            return (scx, scy);
        }

        // ── Grid edges ────────────────────────────────────────────────────────
        // Horizontal: (col, row) → (col+1, row)   count: colIntervals × nodeRows
        // Vertical:   (col, row) → (col, row+1)   count: nodeCols × rowIntervals

        int expectedEdgeCount = colIntervals * nodeRows + rowIntervals * nodeCols;
        int edgeCount         = 0;

        var edgeBrush = new SolidColorBrush(Color.FromArgb(100, 160, 160, 160));

        for (int col = 0; col < colIntervals; col++)
            for (int row = 0; row < nodeRows; row++)
            {
                canvas.Children.Add(MakeLine(Pos(col, row), Pos(col + 1, row), edgeBrush));
                edgeCount++;
            }

        for (int col = 0; col < nodeCols; col++)
            for (int row = 0; row < rowIntervals; row++)
            {
                canvas.Children.Add(MakeLine(Pos(col, row), Pos(col, row + 1), edgeBrush));
                edgeCount++;
            }

        System.Diagnostics.Debug.Assert(
            edgeCount == expectedEdgeCount,
            $"Surface plot edge count mismatch: drew {edgeCount}, expected {expectedEdgeCount} " +
            $"({colIntervals}×{nodeRows} horizontal + {rowIntervals}×{nodeCols} vertical).");

        // ── Nodes (painter order: back-to-front by gridCol+gridRow) ──────────
        for (int sum = 0; sum < nodeCols + nodeRows - 1; sum++)
        {
            for (int row = Math.Max(0, sum - nodeCols + 1); row <= Math.Min(nodeRows - 1, sum); row++)
            {
                int col = sum - row;
                if (col < 0 || col >= nodeCols) continue;

                double t     = (h[row][col] - hMin) / hRange;
                var (sx, sy) = Pos(col, row);

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
        }

        // ── Canvas dimensions ─────────────────────────────────────────────────
        canvas.Width  = (colIntervals + rowIntervals) * IsoW + Margin * 2;
        canvas.Height = (colIntervals + rowIntervals) * IsoH + maxZPixels + Margin * 2;

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
