using LevelApp.App.Helpers;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Orientation = LevelApp.Core.Models.Orientation;

namespace LevelApp.App.DisplayModules.MeasurementsGrid;

/// <summary>
/// Renders the 2D measurements grid onto a <see cref="Canvas"/>.
///
/// Shows each measurement step as a directed arrow on a top-down orthographic
/// grid.  Each rectangular cell carries a loop closure error (always in µm).
/// Cell backgrounds are colour-coded green / amber / red by error magnitude
/// relative to the standard deviation of all cell errors.
///
/// Flagged steps (from <see cref="SurfaceResult.FlaggedStepIndices"/>) are
/// drawn in the flagged colour to distinguish them visually.
/// All colours are resolved from the app's ThemeColors resource dictionary at render time.
/// </summary>
public static class MeasurementsGridRenderer
{
    // ── Layout constants (at zoom = 1.0) ──────────────────────────────────────

    private const double TargetMaxPx     = 420.0;
    private const double MinSpacingPx    = 36.0;
    private const double CanvasPad       = 48.0;
    private const double NodeRadius      = 4.0;

    private const double LabelHalfW     = 15.0;
    private const double LabelHalfH     =  7.0;

    private const double ArrowLen       = 10.0;
    private const double ArrowHalfW     =  4.5;

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <param name="canvas">Target canvas — cleared and repopulated on each call.</param>
    /// <param name="steps">Ordered step list from <c>InitialRound.Steps</c>.</param>
    /// <param name="result">Active result (initial or latest correction).</param>
    /// <param name="definition">Object definition supplying grid dimensions.</param>
    /// <param name="isRawMode">
    ///   <c>true</c>  — edge labels show raw instrument readings.<br/>
    ///   <c>false</c> — edge labels show adjusted readings (reading − residual).
    /// </param>
    /// <param name="isUmUnits">
    ///   <c>true</c>  — edge labels in µm (height difference).<br/>
    ///   <c>false</c> — edge labels in mm/m (inclination / raw reading).
    /// </param>
    /// <param name="zoomFactor">Canvas scale multiplier (1.0 = default size).</param>
    public static void Render(
        Canvas                        canvas,
        IReadOnlyList<MeasurementStep> steps,
        SurfaceResult                 result,
        ObjectDefinition              definition,
        bool                          isRawMode,
        bool                          isUmUnits,
        double                        zoomFactor = 1.0)
    {
        canvas.Children.Clear();

        // Route Union Jack (and future non-grid strategies) to a separate renderer.
        if (!definition.Parameters.TryGetValue("columnsCount", out var cObj) ||
            !definition.Parameters.TryGetValue("rowsCount",    out var rObj))
        {
            if (definition.Parameters.TryGetValue("widthMm",  out var ujW) &&
                definition.Parameters.TryGetValue("heightMm", out var ujH))
            {
                RenderUnionJack(canvas, steps, result, definition,
                    Convert.ToDouble(ujW), Convert.ToDouble(ujH),
                    isRawMode, isUmUnits, zoomFactor);
            }
            return;
        }

        if (!definition.Parameters.TryGetValue("widthMm",  out var wObj) ||
            !definition.Parameters.TryGetValue("heightMm", out var hObj))
            return;

        int    cols     = Convert.ToInt32( cObj);
        int    rows     = Convert.ToInt32( rObj);
        double widthMm  = Convert.ToDouble(wObj);
        double heightMm = Convert.ToDouble(hObj);

        if (cols < 2 || rows < 2 || steps.Count == 0) return;

        double hDistM = widthMm  / (cols - 1) / 1000.0;
        double vDistM = heightMm / (rows - 1) / 1000.0;

        double effectiveMaxPx  = TargetMaxPx   * zoomFactor;
        double effectiveMinPx  = MinSpacingPx  * zoomFactor;
        double scale    = effectiveMaxPx / Math.Max(widthMm, heightMm);
        double xSpacing = Math.Max(effectiveMinPx, widthMm  / (cols - 1) * scale);
        double ySpacing = Math.Max(effectiveMinPx, heightMm / (rows - 1) * scale);

        double arrowLen        = ArrowLen   * zoomFactor;
        double arrowHalfW      = ArrowHalfW * zoomFactor;
        double fontSize        = Math.Clamp(8.0  * zoomFactor, 7.0, 28.0);
        double errorFontSize   = Math.Clamp(9.0  * zoomFactor, 7.0, 30.0);
        double scaledLabelHalfW = LabelHalfW * zoomFactor;

        (double x, double y) NodePos(int c, int r) =>
            (c * xSpacing + CanvasPad,
             r * ySpacing + CanvasPad);

        // ── Build edge lookup maps ─────────────────────────────────────────────
        //
        // hEdge[(c, r)] = (stepListIndex, isEast)
        //   covers the horizontal edge between node (c, r) and node (c+1, r).
        //   isEast = true  → step goes East  (from-node is (c, r))
        //   isEast = false → step goes West  (from-node is (c+1, r))
        //
        // vEdge[(c, r)] = (stepListIndex, isSouth)
        //   covers the vertical edge between node (c, r) and node (c, r+1).
        //   isSouth = true  → step goes South (from-node is (c, r))
        //   isSouth = false → step goes North (from-node is (c, r+1))
        var hEdge = new Dictionary<(int, int), (int idx, bool isEast)>();
        var vEdge = new Dictionary<(int, int), (int idx, bool isSouth)>();

        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            switch (s.Orientation)
            {
                case Orientation.East:
                    hEdge[(s.GridCol,     s.GridRow)] = (i, true);
                    break;
                case Orientation.West:
                    // From-node is the right node; canonical key uses left node
                    hEdge[(s.GridCol - 1, s.GridRow)] = (i, false);
                    break;
                case Orientation.South:
                    vEdge[(s.GridCol, s.GridRow)]     = (i, true);
                    break;
                case Orientation.North:
                    // From-node is the bottom node; canonical key uses top node
                    vEdge[(s.GridCol, s.GridRow - 1)] = (i, false);
                    break;
            }
        }

        var flaggedSet = result.FlaggedStepIndices.ToHashSet();

        double EffectiveReading(int stepIdx)
        {
            double raw = steps[stepIdx].Reading ?? 0.0;
            if (!isRawMode && stepIdx < result.Residuals.Length)
                raw -= result.Residuals[stepIdx];
            return raw;
        }

        // ── Compute loop closure errors (always µm) ───────────────────────────
        //
        // For the cell bounded by (c, r) and (c+1, r+1), traversed clockwise:
        //   top    edge → East  is positive
        //   right  edge → South is positive
        //   bottom edge → East  is negative  (clockwise goes West)
        //   left   edge → South is negative  (clockwise goes North)
        //
        // loop_error = top_norm - bottom_norm + right_norm - left_norm   (µm)
        //
        // where _norm means "normalised to East / South direction" in µm.
        int numCellsX = cols - 1;
        int numCellsY = rows - 1;
        var loopErrors = new double[numCellsX * numCellsY];

        for (int cr = 0; cr < numCellsY; cr++)
        {
            for (int cc = 0; cc < numCellsX; cc++)
            {
                double HNorm(int c, int r)
                {
                    if (!hEdge.TryGetValue((c, r), out var e)) return 0.0;
                    double delta = EffectiveReading(e.idx) * hDistM * 1000.0;
                    return e.isEast ? delta : -delta;
                }

                double VNorm(int c, int r)
                {
                    if (!vEdge.TryGetValue((c, r), out var e)) return 0.0;
                    double delta = EffectiveReading(e.idx) * vDistM * 1000.0;
                    return e.isSouth ? delta : -delta;
                }

                double top    =  HNorm(cc,     cr);
                double bottom =  HNorm(cc,     cr + 1);
                double right  =  VNorm(cc + 1, cr);
                double left   =  VNorm(cc,     cr);

                loopErrors[cr * numCellsX + cc] = top - bottom + right - left;
            }
        }

        double errMean  = loopErrors.Average();
        double errVar   = loopErrors.Select(e => (e - errMean) * (e - errMean)).Average();
        double errSigma = Math.Sqrt(errVar);
        if (errSigma < 1e-9) errSigma = 1.0;

        // ── Resolve theme brushes ─────────────────────────────────────────────
        var loopOkBrush    = ThemeHelper.GetBrush(canvas, "LoopOkBrush");
        var loopWarnBrush  = ThemeHelper.GetBrush(canvas, "LoopWarnBrush");
        var loopErrorBrush = ThemeHelper.GetBrush(canvas, "LoopErrorBrush");
        var normalBrush    = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridStepArrowBrush"));
        var flaggedBrush   = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridFlaggedStepBrush"));
        var labelFg        = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridStepArrowBrush"));
        var errorFg        = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridStepArrowBrush"));
        var nodeBrush      = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridPendingStepBrush"));

        // ── Cell background fills ─────────────────────────────────────────────
        for (int cr = 0; cr < numCellsY; cr++)
        {
            for (int cc = 0; cc < numCellsX; cc++)
            {
                double abs = Math.Abs(loopErrors[cr * numCellsX + cc]);
                var bg = abs < errSigma       ? loopOkBrush
                       : abs < 2 * errSigma   ? loopWarnBrush
                                              : loopErrorBrush;

                var (x1, y1) = NodePos(cc,     cr);
                var (x2, y2) = NodePos(cc + 1, cr + 1);

                var rect = new Rectangle
                {
                    Width  = x2 - x1,
                    Height = y2 - y1,
                    Fill   = bg
                };
                Canvas.SetLeft(rect, x1);
                Canvas.SetTop(rect,  y1);
                canvas.Children.Add(rect);
            }
        }

        // ── Edge arrows ───────────────────────────────────────────────────────
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                bool hasStep   = hEdge.TryGetValue((c, r), out var he);
                bool isFlagged = hasStep && flaggedSet.Contains(he.idx);
                var  brush     = isFlagged ? flaggedBrush : normalBrush;
                double thick   = isFlagged ? 2.0 : 1.5;

                var (fx, fy) = hasStep && !he.isEast ? NodePos(c + 1, r) : NodePos(c, r);
                var (tx, ty) = hasStep && !he.isEast ? NodePos(c,     r) : NodePos(c + 1, r);

                AddArrow(canvas, fx, fy, tx, ty, brush, thick, arrowLen, arrowHalfW);
            }
        }

        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows - 1; r++)
            {
                bool hasStep   = vEdge.TryGetValue((c, r), out var ve);
                bool isFlagged = hasStep && flaggedSet.Contains(ve.idx);
                var  brush     = isFlagged ? flaggedBrush : normalBrush;
                double thick   = isFlagged ? 2.0 : 1.5;

                var (fx, fy) = hasStep && !ve.isSouth ? NodePos(c, r + 1) : NodePos(c, r);
                var (tx, ty) = hasStep && !ve.isSouth ? NodePos(c, r)     : NodePos(c, r + 1);

                AddArrow(canvas, fx, fy, tx, ty, brush, thick, arrowLen, arrowHalfW);
            }
        }

        // ── Edge value labels ─────────────────────────────────────────────────

        string FormatEdgeValue(int stepIdx, bool isHorizontal)
        {
            double reading = EffectiveReading(stepIdx);
            if (isUmUnits)
            {
                double distM = isHorizontal ? hDistM : vDistM;
                return $"{reading * distM * 1000.0:F2}";
            }
            return $"{reading:F3}";
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                if (!hEdge.TryGetValue((c, r), out var he)) continue;

                var (x1, y1) = NodePos(c,     r);
                var (x2, _)  = NodePos(c + 1, r);
                double midX  = (x1 + x2) / 2;

                var tb = new TextBlock
                {
                    Text       = FormatEdgeValue(he.idx, true),
                    FontSize   = fontSize,
                    Foreground = labelFg
                };
                Canvas.SetLeft(tb, midX - scaledLabelHalfW);
                Canvas.SetTop(tb,  y1   - fontSize - 2);
                canvas.Children.Add(tb);
            }
        }

        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows - 1; r++)
            {
                if (!vEdge.TryGetValue((c, r), out var ve)) continue;

                var (x1, y1) = NodePos(c, r);
                var (_, y2)  = NodePos(c, r + 1);
                double midY  = (y1 + y2) / 2;

                var tb = new TextBlock
                {
                    Text       = FormatEdgeValue(ve.idx, false),
                    FontSize   = fontSize,
                    Foreground = labelFg,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform       = new RotateTransform { Angle = -90 }
                };
                Canvas.SetLeft(tb, x1 - scaledLabelHalfW - fontSize * 0.7 - 2);
                Canvas.SetTop(tb,  midY - fontSize * 0.6);
                canvas.Children.Add(tb);
            }
        }

        // ── Loop closure error labels ─────────────────────────────────────────
        for (int cr = 0; cr < numCellsY; cr++)
        {
            for (int cc = 0; cc < numCellsX; cc++)
            {
                double err   = loopErrors[cr * numCellsX + cc];
                string label = $"{err:F2}µm";

                var (x1, y1) = NodePos(cc,     cr);
                var (x2, y2) = NodePos(cc + 1, cr + 1);
                double midX  = (x1 + x2) / 2;
                double midY  = (y1 + y2) / 2;

                var tb = new TextBlock
                {
                    Text       = label,
                    FontSize   = errorFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = errorFg
                };
                Canvas.SetLeft(tb, midX - label.Length * errorFontSize * 0.35);
                Canvas.SetTop(tb,  midY - errorFontSize * 0.6);
                canvas.Children.Add(tb);
            }
        }

        // ── Node dots ─────────────────────────────────────────────────────────
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var (cx, cy) = NodePos(c, r);
                var e = new Ellipse
                {
                    Width  = NodeRadius * 2,
                    Height = NodeRadius * 2,
                    Fill   = nodeBrush
                };
                Canvas.SetLeft(e, cx - NodeRadius);
                Canvas.SetTop(e,  cy - NodeRadius);
                canvas.Children.Add(e);
            }
        }

        canvas.Width  = (cols - 1) * xSpacing + CanvasPad * 2;
        canvas.Height = (rows - 1) * ySpacing + CanvasPad * 2;
    }

    // ── Union Jack renderer ───────────────────────────────────────────────────

    private static void RenderUnionJack(
        Canvas                         canvas,
        IReadOnlyList<MeasurementStep> steps,
        SurfaceResult                  result,
        ObjectDefinition               definition,
        double                         widthMm,
        double                         heightMm,
        bool                           isRawMode,
        bool                           isUmUnits,
        double                         zoomFactor)
    {
        if (steps.Count == 0) return;

        double effectiveMaxPx   = TargetMaxPx * zoomFactor;
        double scale            = effectiveMaxPx / Math.Max(widthMm, heightMm);
        double arrowLen         = ArrowLen   * zoomFactor;
        double arrowHalfW       = ArrowHalfW * zoomFactor;
        double fontSize         = Math.Clamp(8.0 * zoomFactor, 7.0, 28.0);
        double errorFontSize    = Math.Clamp(9.0 * zoomFactor, 7.0, 30.0);
        double scaledLabelHalfW = LabelHalfW * zoomFactor;

        (double px, double py) ToCanvas(double mmX, double mmY) =>
            (mmX * scale + CanvasPad,
             mmY * scale + CanvasPad);

        (double px, double py) NodePx(string nodeId)
        {
            var (mmX, mmY) = UnionJackStrategy.NodePositionById(nodeId, definition);
            return ToCanvas(mmX, mmY);
        }

        double canvasW = widthMm  * scale + CanvasPad * 2;
        double canvasH = heightMm * scale + CanvasPad * 2;

        canvas.Children.Add(new Rectangle { Width = canvasW, Height = canvasH,
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) });

        var flaggedSet   = result.FlaggedStepIndices.ToHashSet();
        var normalBrush  = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridStepArrowBrush"));
        var flaggedBrush = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridFlaggedStepBrush"));
        var labelFg      = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridStepArrowBrush"));

        // ── Loop closure polygons ─────────────────────────────────────────────
        if (result.PrimitiveLoops.Length > 0)
        {
            double loopSigmaUm = result.ClosureErrorRms * 1000.0;
            if (loopSigmaUm < 1e-9) loopSigmaUm = 1.0;
            var errorFg = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridStepArrowBrush"));

            var loopOkBrush    = ThemeHelper.GetBrush(canvas, "LoopOkBrush");
            var loopWarnBrush  = ThemeHelper.GetBrush(canvas, "LoopWarnBrush");
            var loopErrorBrush = ThemeHelper.GetBrush(canvas, "LoopErrorBrush");

            foreach (var loop in result.PrimitiveLoops)
            {
                double absUm = Math.Abs(loop.ClosureErrorMm * 1000.0);
                var bg = absUm < loopSigmaUm     ? loopOkBrush
                       : absUm < 2 * loopSigmaUm ? loopWarnBrush
                                                  : loopErrorBrush;

                var points = new PointCollection();
                double cx = 0, cy = 0;
                foreach (var nodeId in loop.NodeIds)
                {
                    var (px, py) = NodePx(nodeId);
                    points.Add(new Point(px, py));
                    cx += px;
                    cy += py;
                }
                cx /= loop.NodeIds.Length;
                cy /= loop.NodeIds.Length;

                canvas.Children.Add(new Polygon { Points = points, Fill = bg });

                string errLabel = $"{loop.ClosureErrorMm * 1000.0:F2}µm";
                var tb = new TextBlock
                {
                    Text       = errLabel,
                    FontSize   = errorFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = errorFg
                };
                Canvas.SetLeft(tb, cx - errLabel.Length * errorFontSize * 0.35);
                Canvas.SetTop(tb,  cy - errorFontSize * 0.6);
                canvas.Children.Add(tb);
            }
        }

        // ── Directed edge arrows ──────────────────────────────────────────────
        for (int i = 0; i < steps.Count; i++)
        {
            var s        = steps[i];
            var (fx, fy) = NodePx(s.NodeId);
            var (tx, ty) = NodePx(s.ToNodeId);

            bool isFlagged = flaggedSet.Contains(i);
            AddArrow(canvas, fx, fy, tx, ty,
                isFlagged ? flaggedBrush : normalBrush,
                isFlagged ? 2.0 : 1.5,
                arrowLen, arrowHalfW);
        }

        // ── Edge value labels ─────────────────────────────────────────────────
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (s.Reading is null) continue;

            double raw = s.Reading.Value;
            if (!isRawMode && i < result.Residuals.Length)
                raw -= result.Residuals[i];

            string label;
            if (isUmUnits)
            {
                var (fmmX, fmmY) = UnionJackStrategy.NodePositionById(s.NodeId,   definition);
                var (tmmX, tmmY) = UnionJackStrategy.NodePositionById(s.ToNodeId, definition);
                double dx    = tmmX - fmmX;
                double dy    = tmmY - fmmY;
                double distM = Math.Sqrt(dx * dx + dy * dy) / 1000.0;
                label = $"{raw * distM * 1000.0:F2}";
            }
            else
            {
                label = $"{raw:F3}";
            }

            var (fx, fy) = NodePx(s.NodeId);
            var (tx, ty) = NodePx(s.ToNodeId);
            double midX  = (fx + tx) / 2;
            double midY  = (fy + ty) / 2;

            var tb = new TextBlock
            {
                Text       = label,
                FontSize   = fontSize,
                Foreground = labelFg
            };
            Canvas.SetLeft(tb, midX - scaledLabelHalfW);
            Canvas.SetTop(tb,  midY - fontSize - 2);
            canvas.Children.Add(tb);
        }

        // ── Node dots ─────────────────────────────────────────────────────────
        var nodeBrush = new SolidColorBrush(ThemeHelper.GetColor(canvas, "GridPendingStepBrush"));
        var nodeIds   = steps
            .SelectMany(s => new[] { s.NodeId, s.ToNodeId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct();

        foreach (var nodeId in nodeIds)
        {
            var (cx, cy) = NodePx(nodeId);
            var e = new Ellipse
            {
                Width  = NodeRadius * 2,
                Height = NodeRadius * 2,
                Fill   = nodeBrush
            };
            Canvas.SetLeft(e, cx - NodeRadius);
            Canvas.SetTop(e,  cy - NodeRadius);
            canvas.Children.Add(e);
        }

        canvas.Width  = canvasW;
        canvas.Height = canvasH;
    }

    // ── Shared arrow helper ───────────────────────────────────────────────────

    private static void AddArrow(
        Canvas          canvas,
        double          x1, double y1,
        double          x2, double y2,
        SolidColorBrush brush,
        double          thickness,
        double          arrowLen,
        double          arrowHalfW)
    {
        double dx  = x2 - x1;
        double dy  = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;

        double nx = dx / len;
        double ny = dy / len;
        double px = -ny;
        double py =  nx;

        double al = Math.Min(arrowLen,  len * 0.40);
        double hw = Math.Min(arrowHalfW, al * 0.50);

        double bx = x2 - nx * al;
        double by = y2 - ny * al;

        canvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1,
            X2 = bx, Y2 = by,
            Stroke          = brush,
            StrokeThickness = thickness
        });

        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(x2,          y2),
                new Point(bx + px * hw, by + py * hw),
                new Point(bx - px * hw, by - py * hw)
            },
            Fill = brush
        });
    }
}
