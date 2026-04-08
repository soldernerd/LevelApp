# Work Package WP-0.05 — Graphics Improvements

> LevelApp | C# WinUI 3 | Windows App SDK / .NET 8/9
> Depends on: WP-0.04 (Results view with 3D surface plot) being complete

---

## Overview

Two related display improvements:

1. **WP-0.05a** — Fix the measurement progress grid during the guided measurement session
2. **WP-0.05b** — Add a new "Measurements & Loop Errors" tab to the Results view

---

## WP-0.05a — Measurement Progress Grid

### Problem

The current `MeasurementView` grid renders dots (nodes) and colours them orange/blue/green to indicate current/next/done. This is wrong conceptually: each measurement step is a **connection between two adjacent nodes**, not a node itself. The connections are not drawn at all, and the node colouring misrepresents what is being measured.

### Target behaviour

**Structure:** Draw the full grid as nodes (circles) connected by edges (lines). Both nodes and edges are drawn for every step in the step sequence before any measurements begin.

**Colour states:**

| Element | Pending | Current step | Completed |
|---|---|---|---|
| Edge (connection) | Grey | Highlighted (e.g. Fluent accent blue) | Green |
| Node | Grey | Grey — **except** the two endpoint nodes of the current step, which are highlighted (same accent colour) | Grey |

Nodes are always grey unless they are an endpoint of the active step. They never permanently turn green — only edges do.

**Reference:** The strategy-selection preview (on `ProjectSetupView`) already renders nodes + edges correctly and should look identical to this view. If a shared grid-drawing component exists, reuse it here with state colouring added.

### Implementation notes

- Each `MeasurementStep` has `gridCol` / `gridRow`. The edge for step N runs from `(steps[N].gridCol, steps[N].gridRow)` to `(steps[N+1].gridCol, steps[N+1].gridRow)`.  
  - **Note:** for a row pass, consecutive steps share the same row; for a column pass, the same column. The edge is always between the instrument's current position and the next position.
  - Alternatively: each step already represents one instrument placement. The *segment being measured* at step N is from the node where the instrument sits to the next node along the pass direction. Check `FullGridStrategy.GenerateSteps()` to confirm exactly which node pair each step represents.
- Node pixel positions are computed from `gridCol` / `gridRow` using the same isometric/orthographic projection already used in the strategy preview.
- Physical aspect ratio: horizontal node spacing in pixels should be proportional to `widthMm / (columnsCount - 1)` and vertical to `heightMm / (rowsCount - 1)`, so a non-square plate does not render as a square grid.

---

## WP-0.05b — Measurements & Loop Errors View

### Overview

A second tab on the Results view (the existing 3D surface plot is Tab 1; this is Tab 2). Tab label: **"Measurements"** or **"Loop Analysis"** — your choice.

Same 2D grid as WP-0.05a (nodes + edges, correct aspect ratio, no isometric projection needed here — plain orthographic top-down is clearer for a results view). Each edge carries a numerical label. Each minimal rectangular cell carries a loop closure error.

### Controls (above the grid)

Two independent toggles:

| Toggle | Option A | Option B |
|---|---|---|
| **Mode** | Raw | Adjusted |
| **Units** | mm/m (inclination) | µm (height difference) |

Four combinations are all valid.

### Edge labels

**Raw mode:** display `step.reading` directly (the value the instrument reported).

**Adjusted mode:** display `step.reading - residuals[stepIndex]` — the reading implied by the least-squares surface fit. This is what the reading "should have been" according to the solver.

**Unit conversion (µm height difference):**  
`height_um = reading_mm_per_m × physical_distance_m × 1000`  
where:
- For a horizontal step (row pass): `physical_distance_m = widthMm / (columnsCount - 1) / 1000`
- For a vertical step (column pass): `physical_distance_m = heightMm / (rowsCount - 1) / 1000`

The `MeasurementStep.orientation` field (North/South/East/West) tells you whether a step is horizontal or vertical.

Label placement: centred on the edge midpoint, rotated to align with the edge direction (horizontal labels on row edges, vertical labels on column edges). Font size should be small enough not to overlap on dense grids.

### Loop closure errors

For each minimal rectangular cell, compute the signed sum of height differences around its four edges (clockwise convention):

```
loop_error_um = top_edge_height - bottom_edge_height + right_edge_height - left_edge_height
```

where each `edge_height = reading × physical_distance_m × 1000` (µm), with sign determined by traversal direction relative to the clockwise walk around the cell.

More precisely: for a cell bounded by columns c and c+1, rows r and r+1:
- Top edge: row step from (c, r) to (c+1, r) — traversed left→right = positive
- Right edge: col step from (c+1, r) to (c+1, r+1) — traversed top→bottom = positive  
- Bottom edge: row step from (c+1, r+1) to (c, r+1) — traversed right→left = negative sign on its reading
- Left edge: col step from (c, r+1) to (c, r) — traversed bottom→top = negative sign on its reading

Loop error is always in **µm** regardless of the units toggle (inclination loop sums are only meaningful when all spacings are equal, which cannot be assumed).

In **Raw mode**: shows pre-fit closure residuals — raw inconsistency in the data.  
In **Adjusted mode**: should be near zero by construction (the solver enforces closure). Display anyway to confirm solver correctness.

### Colour coding of loop errors

Compute σ = standard deviation of all loop errors across all cells. Colour the cell background (subtle, low-opacity fill) or the error text:

| Magnitude | Colour |
|---|---|
| < 1σ | Green (or neutral — no fill) |
| 1σ – 2σ | Amber / yellow |
| > 2σ | Red |

### Optional: flagged edge highlight

If `result.flaggedStepIndices` contains a step index, draw that edge with a dashed stroke or a small warning marker. This connects the loop error view back to the outlier detection already done by the solver.

### Layout

- Toggles row at top
- Grid fills remaining space, respecting aspect ratio
- Cell loop error text centred in each cell
- Node dots small, grey, no labels
- Edge labels readable but not dominant — consider a slightly smaller font weight than the loop error values

---

## Data available in `SurfaceResult`

```csharp
double[][] heightMapMm          // fitted heights at each node [row][col]
double[] residuals              // per-step residual, indexed same as step list
int[] flaggedStepIndices        // steps flagged as outliers
double flatnessValueMm          // peak-to-valley
double sigmaThreshold           // k used for outlier detection
```

And from the parent `MeasurementSession`:
```csharp
IReadOnlyList<MeasurementStep> steps   // full ordered step list
// Each step: index, gridCol, gridRow, orientation, reading
```

And from `ObjectDefinition.parameters`:
```
widthMm, heightMm, columnsCount, rowsCount
```

---

## Out of scope for this WP

- Changes to the least-squares solver or data model
- Union Jack strategy loop detection (only rectangular minimal loops from Full Grid)
- Export / reporting of the loop analysis view
- Any Bluetooth/USB instrument changes
