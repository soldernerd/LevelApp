# Work Package 0.04 — Union Jack Rework, Graph-Driven Display, Setup Preview, Primitive Closure Loops

> Target version on completion: **0.4.0**

---

## Goals

1. Rework the Union Jack strategy with a correct geometric model (`segments` + `rings`)
2. Display object info, dimensions, strategy and calculation parameters alongside results
3. Make the 3D result display graph-driven — nodes and edges come from the step list, physical positions from plate dimensions
4. Add a live 2D strategy preview in the project setup screen
5. Restrict closure loop statistics to primitive loops with physically correct distance-weighted closure errors

---

## 1. Union Jack strategy rework

### 1a. Parameters

The Union Jack strategy is no longer parameterised by `columnsCount` / `rowsCount`. It uses:

| Parameter | Type | Constraints | Description |
|---|---|---|---|
| `segments` | `int` | ≥ 1 | Number of measuring steps from center to edge along each arm |
| `rings` | `int` | 0, 1, or `segments` | Number of rectangular circuits around the center |

`widthMm` and `heightMm` remain in `ObjectDefinition.parameters` as before.

Remove `columnsCount` / `rowsCount` from `ObjectDefinition.parameters` for Union Jack sessions. Full Grid continues to use `columnsCount` / `rowsCount` unchanged.

### 1b. Node positions

All positions are in physical mm, origin at plate bottom-left corner `(0, 0)`.

**Center node:** `(width/2, height/2)`

**Horizontal arm nodes** (k = 1 .. segments):
- Right: `(width/2 + k × width/(2×segments), height/2)`
- Left:  `(width/2 − k × width/(2×segments), height/2)`

**Vertical arm nodes** (k = 1 .. segments):
- Bottom: `(width/2, height/2 + k × height/(2×segments))`  *(row index increases downward)*
- Top:    `(width/2, height/2 − k × height/(2×segments))`

**Diagonal arm nodes** (k = 1 .. segments):
- SE: `(width/2 + k × width/(2×segments),  height/2 + k × height/(2×segments))`
- SW: `(width/2 − k × width/(2×segments),  height/2 + k × height/(2×segments))`
- NE: `(width/2 + k × width/(2×segments),  height/2 − k × height/(2×segments))`
- NW: `(width/2 − k × width/(2×segments),  height/2 − k × height/(2×segments))`

At k = segments, the horizontal arm tips reach `(0, height/2)` and `(width, height/2)`, the vertical arm tips reach `(width/2, 0)` and `(width/2, height)`, and the diagonal arm tips reach the four corners `(0,0)`, `(width,0)`, `(0,height)`, `(width,height)`.

**Ring nodes** (ring r = 1 .. rings) are the 8 nodes at arm level k = r:
one on each arm. They form an axis-aligned rectangle of size
`(r × width/segments) × (r × height/segments)` centred on the plate.

Note: ring nodes are not additional nodes — they are the arm nodes at depth k = r. A ring simply connects 8 existing arm nodes with 8 measured segments.

### 1c. Step sequence

Steps are generated in this order:

1. **Horizontal arm** — center → right tip (East), then right tip → center is NOT re-measured; instead continue: center → left tip (West). Both sub-arms are one continuous pass through center: left tip → center → right tip (East), boustrophedon between passes not applicable here since it is a single straight line.
   Actually: treat each full arm as one pass. Left-to-right = East. Right-to-left = West. The two half-arms form one continuous line.
   - Pass: `(left tip) → (center) → (right tip)`, orientation East, `2 × segments` steps
2. **Vertical arm** — top → bottom (South), `2 × segments` steps
3. **SE diagonal** — NW corner → SE corner, orientation SouthEast, `2 × segments` steps
4. **NW diagonal** — NE corner → SW corner, orientation SouthWest, `2 × segments` steps
5. **Ring passes** — for r = 1 .. rings, traverse the rectangle at ring level r clockwise:
   - Bottom edge: W-side node → E-side node (East), 2 steps (W→center-bottom→E... 
   
   Actually each ring rectangle has exactly 8 nodes (one per arm at depth r) and connects them with 8 segments traversed clockwise:
   - Bottom-left → Bottom-right (East) — connects SW arm node to SE arm node, passing through the bottom vertical arm node. That is 2 steps.
   - Bottom-right → Top-right (North) — connects SE arm node to NE arm node, passing through the right horizontal arm node. 2 steps.
   - Top-right → Top-left (West) — 2 steps.
   - Top-left → Bottom-left (South) — 2 steps.
   - Total: 8 steps per ring. ✓

### 1d. Total step count

```
steps = 4 × 2 × segments   +   rings × 8
      = 8 × segments        +   8 × rings
```

Examples:
- segments=1, rings=0: 8 steps
- segments=1, rings=1: 16 steps
- segments=2, rings=0: 16 steps
- segments=2, rings=2: 32 steps
- segments=4, rings=4: 64 steps

### 1e. `ObjectDefinition.parameters` for Union Jack

```json
"parameters": {
  "widthMm": 1200,
  "heightMm": 800,
  "segments": 4,
  "rings": 1
}
```

### 1f. Update `UnionJackStrategy.cs`

Replace the current implementation entirely. The previous version used `columnsCount` / `rowsCount` and a different geometric model. The new implementation follows §1a–1e above.

Update `SurfacePlateModule` to pass `segments` and `rings` to the strategy.

---

## 2. Info panel in ResultsView

Add a read-only info section to the statistics panel in `ResultsView`, above the flatness/residuals block. Display:

| Field | Example |
|---|---|
| Object type | Surface Plate |
| Dimensions | 1200 × 800 mm |
| Strategy | Union Jack |
| Segments / Grid | segments: 4, rings: 1  **or**  9 × 6 (Full Grid) |
| Calculation method | Least Squares |
| Sigma threshold | 2.5σ (auto-exclude on) |
| Excluded steps | 2 manual, 1 auto |

The ViewModel already holds the session and `CalculationParameters` — this is a pure binding exercise, no new logic required.

---

## 3. Graph-driven 3D display

### 3a. Node positions

Each `MeasurementStep` has a `(gridCol, gridRow)` pair for Full Grid, but for Union Jack this is not sufficient — the physical `(x, y)` position must be derivable from the strategy and object definition.

Add a method to `IMeasurementStrategy` (or a helper on the module):

```csharp
(double X, double Y) GetNodePosition(MeasurementStep step, ObjectDefinition definition);
```

For Full Grid:
```
x = gridCol × widthMm / (columnsCount − 1)
y = gridRow × heightMm / (rowsCount − 1)
```

For Union Jack, use the arm-position formulas from §1b.

### 3b. Edges

The display draws an edge between consecutive steps **within the same pass**. Passes are already implicit in the step sequence — consecutive steps with the same orientation and continuous grid position belong to the same pass. Add a `PassId` integer to `MeasurementStep` (populated by the strategy, not persisted — derived on load) to make pass grouping explicit.

### 3c. 3D height

Z coordinate = height from `SurfaceResult.HeightMapMm` at the node's physical position. For Union Jack nodes, the height map is indexed by node identity rather than (col, row) grid position — the result must carry a `NodeHeights` dictionary keyed by a node identifier rather than a 2D array.

Adjust `SurfaceResult`:
```csharp
// Replace double[][] HeightMapMm with:
public Dictionary<string, double> NodeHeights { get; set; }  // key = node id
public double FlatnessValueMm { get; set; }
// ... rest unchanged
```

Node id = `"col{gridCol}_row{gridRow}"` for Full Grid, `"arm{direction}_seg{k}"` for Union Jack (e.g. `"armSE_seg2"`).

### 3d. Display module

Update `SurfacePlot3DDisplay` to:
1. Iterate nodes, compute `(x, y)` via `GetNodePosition`, look up z from `NodeHeights`
2. Draw edges between nodes that share a pass
3. Scale x/y to reflect physical plate proportions (a 1200×800 plate renders wider than tall)
4. Color nodes by z height as before

---

## 4. Live strategy preview in ProjectSetupView

### 4a. Location

Replace / integrate with the current step count display below the strategy and parameter controls.

### 4b. Content

A 2D top-down canvas showing:
- Nodes as filled circles (same size regardless of zoom)
- Edges as lines between measured-adjacent nodes
- Plate boundary as a faint rectangle
- Physical aspect ratio preserved (plate proportions reflected in canvas shape)
- Step count as a text label below the canvas

No height information. No interaction required.

### 4c. Update trigger

Redraws whenever any of the following change:
- Strategy selection
- Width or height (mm)
- segments / rings (Union Jack) or columnsCount / rowsCount (Full Grid)

Use a debounce of ~300 ms on numeric inputs to avoid redrawing on every keystroke.

### 4d. Implementation

The preview calls `IMeasurementStrategy.GenerateSteps(objectDefinition)` and `GetNodePosition` on the resulting steps to get physical coordinates. It then renders them onto a `Canvas` or `SkiaSharp` surface scaled to fit the available panel width while preserving aspect ratio.

No new data model changes required — the preview is purely a rendering concern in the ViewModel/View layer.

---

## 5. Primitive closure loops

### 5a. Definition

Only **primitive loops** are computed — loops that cannot be decomposed into smaller measured loops.

**Full Grid primitive loops:** unit rectangles — 4 nodes, 4 edges. One per interior crossing point. For a grid of C columns × R rows: `(C−1) × (R−1)` primitive loops.

**Union Jack primitive loops:** two types:
- **Triangles** — 3 nodes, 3 edges — formed where a diagonal arm meets a horizontal or vertical arm at a shared ring node. Each ring level r has 8 such triangles (one at each of the 8 arm intersections on that ring rectangle).
- **Unit ring rectangles** — 4 nodes, 4 edges — between adjacent ring levels r and r+1, 4 per level pair. Only present when rings ≥ 2.

For segments=1, rings=1: only 8 triangles, no rectangles.
For segments=2, rings=2: 8 triangles at ring 1, 8 triangles at ring 2, 4 rectangles between ring 1 and ring 2.

### 5b. Closure error calculation

Each step reading is in **mm/m** (inclination). Convert to height difference using physical step distance:

```
Δh_step = reading_mm_per_m × distance_m
```

where `distance_m` is the Euclidean distance between the step's start and end nodes in metres, computed from physical node positions (§1b / §3a).

Closure error for a loop:

```
ε = Σ signed_Δh  around the loop (clockwise positive)
```

In theory ε = 0. Practically it reflects accumulated noise.

### 5c. `SurfaceResult` additions

```csharp
public record PrimitiveLoop(string[] NodeIds, double ClosureErrorMm);

public PrimitiveLoop[] PrimitiveLoops        { get; set; }
public double          ClosureErrorMean      { get; set; }
public double          ClosureErrorMedian    { get; set; }
public double          ClosureErrorMax       { get; set; }  // absolute value
public double          ClosureErrorRms       { get; set; }
```

### 5d. Display

Closure statistics panel in `ResultsView` unchanged from WP 0.03 spec — mean, median, max, RMS. The per-loop `PrimitiveLoops` array is available for future display modules (heat map of closure errors per loop) but not displayed in this work package.

---

## 6. Files created / modified

### New files
- *(none — all changes are to existing files)*

### Modified files
- `LevelApp.Core/Geometry/SurfacePlate/Strategies/UnionJackStrategy.cs` — full rewrite per §1
- `LevelApp.Core/Interfaces/IGeometryModule.cs` or strategy helper — add `GetNodePosition`
- `LevelApp.Core/Models/MeasurementStep.cs` — add `PassId` (not persisted)
- `LevelApp.Core/Models/SurfaceResult.cs` — replace `HeightMapMm[][]` with `NodeHeights`, add `PrimitiveLoops`
- `LevelApp.Core/Geometry/SurfacePlate/SurfacePlateCalculator.cs` — update for new node model, primitive loop enumeration, physical distance weighting
- `LevelApp.Core/Geometry/SurfacePlate/Calculators/LeastSquaresCalculator.cs` — update for Union Jack node positions
- `LevelApp.Core/Geometry/SurfacePlate/Calculators/SequentialIntegrationCalculator.cs` — same
- `LevelApp.App/DisplayModules/SurfacePlot3D/SurfacePlot3DDisplay.cs` — graph-driven rendering per §3
- `LevelApp.App/Views/ResultsView.xaml` — info panel per §2
- `LevelApp.App/ViewModels/ResultsViewModel.cs` — bindings for info panel
- `LevelApp.App/Views/ProjectSetupView.xaml` — live preview canvas per §4
- `LevelApp.App/ViewModels/ProjectSetupViewModel.cs` — preview generation logic
- `LevelApp.Core/Serialization/ProjectSerializer.cs` — handle new Union Jack parameters
- `docs/architecture.md` — update strategy descriptions, data model, node position model

---

## 7. Acceptance criteria

- [ ] Union Jack strategy generates correct step sequence for segments=1 rings=0, segments=1 rings=1, segments=2 rings=2, segments=4 rings=4
- [ ] Total step count matches `8 × segments + 8 × rings` for all cases
- [ ] Node physical positions are correct for non-square plates (diagonal tips reach corners, arm tips reach edge midpoints)
- [ ] Full Grid display reflects physical plate proportions (1200×800 renders as wider than tall)
- [ ] Union Jack display shows correct star/ring pattern with correct proportions
- [ ] Info panel shows object type, dimensions, strategy parameters, calculation method, exclusions
- [ ] Live preview updates on every parameter change in project setup
- [ ] Preview step count matches what the strategy actually generates
- [ ] Primitive loop enumeration: correct count for Full Grid (`(C−1)×(R−1)`) and Union Jack (8 triangles per ring + 4 rectangles per adjacent ring pair)
- [ ] Closure errors use physical step distances (mm/m × metres = mm)
- [ ] All existing unit tests pass
- [ ] New unit tests cover: `UnionJackStrategy.GenerateSteps` for multiple segment/ring combinations, `GetNodePosition` for non-square plate, primitive loop enumeration, closure error calculation with physical distances