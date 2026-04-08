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
/// drawn in orange-red to distinguish them visually.
/// </summary>
public static class MeasurementsGridRenderer
{
    // ── Layout constants (at zoom = 1.0) ──────────────────────────────────────

    private const double TargetMaxPx     = 420.0;  // max canvas dimension
    private const double MinSpacingPx    = 36.0;   // minimum cell size for legibility
    private const double CanvasPad       = 48.0;   // outer margin (room for labels)
    private const double NodeRadius      = 4.0;

    // Approximate half-dimensions of an 8pt label (e.g. "−1.234")
    private const double LabelHalfW     = 15.0;
    private const double LabelHalfH     =  7.0;

    // Arrowhead geometry (canvas pixels at zoom = 1.0)
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

        // Physical interval in metres (used for µm conversion)
        double hDistM = widthMm  / (cols - 1) / 1000.0;
        double vDistM = heightMm / (rows - 1) / 1000.0;

        // Pixel spacing preserving physical aspect ratio
        double effectiveMaxPx  = TargetMaxPx   * zoomFactor;
        double effectiveMinPx  = MinSpacingPx  * zoomFactor;
        double scale    = effectiveMaxPx / Math.Max(widthMm, heightMm);
        double xSpacing = Math.Max(effectiveMinPx, widthMm  / (cols - 1) * scale);
        double ySpacing = Math.Max(effectiveMinPx, heightMm / (rows - 1) * scale);

        // Arrowhead size scaled with zoom
        double arrowLen  = ArrowLen  * zoomFactor;
        double arrowHalfW = ArrowHalfW * zoomFactor;

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

        // ── Effective reading (raw or adjusted) ───────────────────────────────
        double EffectiveReading(int stepIdx)
        {
            double raw = steps[stepIdx].Reading ?? 0.0;
            if (!isRawMode && stepIdx < result.Residuals.Length)
                raw -= result.Residuals[stepIdx];
            return raw;
        }

        // ── Compute loop closure errors (always µm) ───────────────────────────
        //
        // For cell bounded by (c, r) and (c+1, r+1), clockwise:
        //   top    edge → East  is positive
        //   right  edge → South is positive
        //   bottom edge → East  is negative  (clockwise goes West)
        //   left   edge → South is negative  (clockwise goes North)
        //
        // loop_error = top_norm - bottom_norm + right_norm - left_norm   (µm)
        //
        // where _norm means "normalized to East / South direction" in µm.

        int numCellsX = cols - 1;
        int numCellsY = rows - 1;
        var loopErrors = new double[numCellsX * numCellsY];

        for (int cr = 0; cr < numCellsY; cr++)
        {
            for (int cc = 0; cc < numCellsX; cc++)
            {
                double HNorm(int c, int r)   // horizontal edge normalised to East (µm)
                {
                    if (!hEdge.TryGetValue((c, r), out var e)) return 0.0;
                    double delta = EffectiveReading(e.idx) * hDistM * 1000.0;
                    return e.isEast ? delta : -delta;
                }

                double VNorm(int c, int r)   // vertical edge normalised to South (µm)
                {
                    if (!vEdge.TryGetValue((c, r), out var e)) return 0.0;
                    double delta = EffectiveReading(e.idx) * vDistM * 1000.0;
                    return e.isSouth ? delta : -delta;
                }

                double top    =  HNorm(cc,     cr);      // East  is clockwise-positive for top
                double bottom =  HNorm(cc,     cr + 1);  // East  is clockwise-negative for bottom
                double right  =  VNorm(cc + 1, cr);      // South is clockwise-positive for right
                double left   =  VNorm(cc,     cr);      // South is clockwise-negative for left

                loopErrors[cr * numCellsX + cc] = top - bottom + right - left;
            }
        }

        // Standard deviation of all loop errors (for colour coding)
        double errMean  = loopErrors.Average();
        double errVar   = loopErrors.Select(e => (e - errMean) * (e - errMean)).Average();
        double errSigma = Math.Sqrt(errVar);
        if (errSigma < 1e-9) errSigma = 1.0;  // guard against zero-variance (adjusted mode)

        // ── Cell background fills ─────────────────────────────────────────────
        for (int cr = 0; cr < numCellsY; cr++)
        {
            for (int cc = 0; cc < numCellsX; cc++)
            {
                double abs = Math.Abs(loopErrors[cr * numCellsX + cc]);
                Color bg = abs < errSigma
                    ? Color.FromArgb( 35, 0,   180,  60)   // < 1σ — green tint
                    : abs < 2 * errSigma
                        ? Color.FromArgb( 55, 220, 160,   0)  // 1σ–2σ — amber tint
                        : Color.FromArgb( 75, 210,  40,  40); // > 2σ — red tint

                var (x1, y1) = NodePos(cc,     cr);
                var (x2, y2) = NodePos(cc + 1, cr + 1);

                var rect = new Rectangle
                {
                    Width  = x2 - x1,
                    Height = y2 - y1,
                    Fill   = new SolidColorBrush(bg)
                };
                Canvas.SetLeft(rect, x1);
                Canvas.SetTop(rect,  y1);
                canvas.Children.Add(rect);
            }
        }

        // ── Edge arrows ───────────────────────────────────────────────────────
        var normalBrush  = new SolidColorBrush(Color.FromArgb(200, 100, 100, 100));
        var flaggedBrush = new SolidColorBrush(Color.FromArgb(255, 210,  80,  20));

        // Horizontal edges — arrow direction reflects actual step orientation
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                bool hasStep   = hEdge.TryGetValue((c, r), out var he);
                bool isFlagged = hasStep && flaggedSet.Contains(he.idx);
                var  brush     = isFlagged ? flaggedBrush : normalBrush;
                double thick   = isFlagged ? 2.0 : 1.5;

                // isEast = true → from (c,r) to (c+1,r); false → from (c+1,r) to (c,r)
                var (fx, fy) = hasStep && !he.isEast ? NodePos(c + 1, r) : NodePos(c, r);
                var (tx, ty) = hasStep && !he.isEast ? NodePos(c,     r) : NodePos(c + 1, r);

                AddArrow(canvas, fx, fy, tx, ty, brush, thick, arrowLen, arrowHalfW);
            }
        }

        // Vertical edges — arrow direction reflects actual step orientation
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows - 1; r++)
            {
                bool hasStep   = vEdge.TryGetValue((c, r), out var ve);
                bool isFlagged = hasStep && flaggedSet.Contains(ve.idx);
                var  brush     = isFlagged ? flaggedBrush : normalBrush;
                double thick   = isFlagged ? 2.0 : 1.5;

                // isSouth = true → from (c,r) to (c,r+1); false → from (c,r+1) to (c,r)
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

        var labelFg = new SolidColorBrush(Color.FromArgb(220, 50, 50, 50));

        // Horizontal edge labels — placed above the edge midpoint
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
                    FontSize   = 8,
                    Foreground = labelFg
                };
                Canvas.SetLeft(tb, midX  - LabelHalfW);
                Canvas.SetTop(tb,  y1    - LabelHalfH * 2 - 2);
                canvas.Children.Add(tb);
            }
        }

        // Vertical edge labels — placed left of the edge midpoint, rotated −90°
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows - 1; r++)
            {
                if (!vEdge.TryGetValue((c, r), out var ve)) continue;

                var (x1, y1) = NodePos(c, r);
                var (_, y2)  = NodePos(c, r + 1);
                double midY  = (y1 + y2) / 2;

                // After −90° rotation around the element centre the text appears
                // vertical.  Position is approximate (8pt text ≈ 30 × 14 px).
                var tb = new TextBlock
                {
                    Text       = FormatEdgeValue(ve.idx, false),
                    FontSize   = 8,
                    Foreground = labelFg,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform       = new RotateTransform { Angle = -90 }
                };
                Canvas.SetLeft(tb, x1 - LabelHalfW - LabelHalfH - 4);
                Canvas.SetTop(tb,  midY - LabelHalfH);
                canvas.Children.Add(tb);
            }
        }

        // ── Loop closure error labels ─────────────────────────────────────────
        var errorFg = new SolidColorBrush(Color.FromArgb(230, 20, 20, 20));

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
                    FontSize   = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = errorFg
                };
                // Centre approximately on the cell midpoint
                Canvas.SetLeft(tb, midX - label.Length * 2.8);
                Canvas.SetTop(tb,  midY - LabelHalfH);
                canvas.Children.Add(tb);
            }
        }

        // ── Node dots (drawn last — on top of labels that run close to nodes) ─
        var nodeBrush = new SolidColorBrush(Color.FromArgb(220, 110, 110, 110));

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

        // ── Canvas size ───────────────────────────────────────────────────────
        canvas.Width  = (cols - 1) * xSpacing + CanvasPad * 2;
        canvas.Height = (rows - 1) * ySpacing + CanvasPad * 2;
    }

    // ── Union Jack renderer ───────────────────────────────────────────────────

    /// <summary>
    /// Renders a Union Jack step map: directed arrows labelled with readings,
    /// flagged edges highlighted, loop closure polygons colour-coded by σ.
    /// </summary>
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

        double effectiveMaxPx = TargetMaxPx * zoomFactor;
        double scale = effectiveMaxPx / Math.Max(widthMm, heightMm);
        double arrowLen   = ArrowLen   * zoomFactor;
        double arrowHalfW = ArrowHalfW * zoomFactor;

        // Convert physical mm position to canvas pixel position
        (double px, double py) ToCanvas(double mmX, double mmY) =>
            (mmX * scale + CanvasPad,
             mmY * scale + CanvasPad);

        (double px, double py) NodePx(string nodeId)
        {
            var (mmX, mmY) = UnionJackStrategy.NodePositionById(nodeId, definition);
            return ToCanvas(mmX, mmY);
        }

        var flaggedSet   = result.FlaggedStepIndices.ToHashSet();
        var normalBrush  = new SolidColorBrush(Color.FromArgb(200, 100, 100, 100));
        var flaggedBrush = new SolidColorBrush(Color.FromArgb(255, 210,  80,  20));
        var labelFg      = new SolidColorBrush(Color.FromArgb(220,  50,  50,  50));

        // ── Loop closure polygons ─────────────────────────────────────────────
        if (result.PrimitiveLoops.Length > 0)
        {
            double loopSigmaUm = result.ClosureErrorRms * 1000.0;
            if (loopSigmaUm < 1e-9) loopSigmaUm = 1.0;
            var errorFg = new SolidColorBrush(Color.FromArgb(230, 20, 20, 20));

            foreach (var loop in result.PrimitiveLoops)
            {
                double absUm = Math.Abs(loop.ClosureErrorMm * 1000.0);
                Color bg = absUm < loopSigmaUm
                    ? Color.FromArgb( 50,   0, 180,  60)
                    : absUm < 2 * loopSigmaUm
                        ? Color.FromArgb( 70, 220, 160,   0)
                        : Color.FromArgb( 90, 210,  40,  40);

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

                canvas.Children.Add(new Polygon { Points = points, Fill = new SolidColorBrush(bg) });

                string errLabel = $"{loop.ClosureErrorMm * 1000.0:F2}µm";
                var tb = new TextBlock
                {
                    Text       = errLabel,
                    FontSize   = 8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = errorFg
                };
                Canvas.SetLeft(tb, cx - errLabel.Length * 2.8);
                Canvas.SetTop(tb,  cy - LabelHalfH);
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
                FontSize   = 8,
                Foreground = labelFg
            };
            Canvas.SetLeft(tb, midX - LabelHalfW);
            Canvas.SetTop(tb,  midY - LabelHalfH * 2 - 2);
            canvas.Children.Add(tb);
        }

        // ── Node dots ─────────────────────────────────────────────────────────
        var nodeBrush = new SolidColorBrush(Color.FromArgb(220, 110, 110, 110));
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

        // ── Canvas size ───────────────────────────────────────────────────────
        canvas.Width  = widthMm * scale + CanvasPad * 2;
        canvas.Height = heightMm * scale + CanvasPad * 2;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Draws a directed arrow from (x1,y1) to (x2,y2): a line body ending in a
    /// filled arrowhead triangle.  The line is shortened by <paramref name="arrowLen"/>
    /// so the shaft does not overlap the head.
    /// </summary>
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

        double nx = dx / len;   // unit direction
        double ny = dy / len;
        double px = -ny;        // unit perpendicular
        double py =  nx;

        // Cap arrowhead to 40 % of edge length so very short edges still look right
        double al = Math.Min(arrowLen,  len * 0.40);
        double hw = Math.Min(arrowHalfW, al * 0.50);

        // Shorten the line shaft to end at the arrowhead base
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
                new Point(x2,          y2),           // tip
                new Point(bx + px * hw, by + py * hw), // base left
                new Point(bx - px * hw, by - py * hw)  // base right
            },
            Fill = brush
        });
    }
}
