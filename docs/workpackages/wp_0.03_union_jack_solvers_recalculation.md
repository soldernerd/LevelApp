\# Work Package 0.03 — Union Jack, Alternative Solver, Recalculation, Closure Statistics



> Target version on completion: \*\*0.3.0\*\*



\---



\## Goals



1\. Add Union Jack measurement strategy for surface plates

2\. Extend the least-squares solver to handle diagonal constraints

3\. Add sequential integration as an alternative calculation method

4\. Introduce `CalculationParameters` as the sole persisted record of how a result was produced; remove all derived results from the project file

5\. Add a recalculation UI: method selector, sigma threshold, manual step exclusion

6\. Add closure loop statistics to the calculation output and results panel



\---



\## 1. New orientations



The `Orientation` enum (currently `North | South | East | West`) must be extended:



```csharp

public enum Orientation

{

&#x20;   North,

&#x20;   South,

&#x20;   East,

&#x20;   West,

&#x20;   NorthEast,

&#x20;   NorthWest,

&#x20;   SouthEast,

&#x20;   SouthWest

}

```



Update everywhere `Orientation` is used: the serialiser/deserialiser, the instruction-text generator in `MeasurementStep`, and the orientation arrow in `MeasurementView`. The arrow display must handle all eight directions; 45° rotations of the existing arrow icon are sufficient.



\---



\## 2. Union Jack strategy



\### What it measures



The Union Jack for a surface plate with `C` columns and `R` rows consists of five pass types, all traversed boustrophedon:



| Pass type | Count | Description |

|---|---|---|

| Row passes | R | Full horizontal rows, East/West alternating — identical to Full Grid |

| Column passes | C | Full vertical columns, South/North alternating — identical to Full Grid |

| Perimeter | 1 | Single clockwise loop: bottom row East, right column South→North, top row West, left column North→South |

| Main diagonal \\ | 1 | (0,0) → (C-1, R-1) stepping NorthEast (or the reverse on alternate passes) |

| Main diagonal / | 1 | (C-1,0) → (0, R-1) stepping NorthWest |



> \*\*Note:\*\* For non-square grids (C ≠ R) the diagonals do not pass through every interior node. The diagonal pass visits only the nodes it naturally lands on. This is correct and expected.



\### Implementation



Create `LevelApp.Core/Geometry/SurfacePlate/Strategies/UnionJackStrategy.cs` implementing `IMeasurementStrategy`.



`GenerateSteps` should:

1\. Delegate row and column passes to the same logic used by `FullGridStrategy` (extract that logic to a shared helper or call `FullGridStrategy` internally)

2\. Add the perimeter pass

3\. Add the two diagonal passes



Register `UnionJackStrategy` in `SurfacePlateModule.AvailableStrategies`.



\---



\## 3. Data model changes — remove stored results, add `CalculationParameters`



\### 3a. New model: `CalculationParameters`



```csharp

public class CalculationParameters

{

&#x20;   public string MethodId { get; set; }          // "LeastSquares" | "SequentialIntegration"

&#x20;   public double SigmaThreshold { get; set; }    // default 2.5; 0 = no automatic exclusion

&#x20;   public bool AutoExcludeOutliers { get; set; } // if true, exclude steps beyond SigmaThreshold

&#x20;   public List<int> ManuallyExcludedStepIndices { get; set; } = new();

}

```



`MethodId` is a string rather than an enum so new methods can be added without a schema change.



\### 3b. Remove `Result` from `MeasurementRound` and `CorrectionRound`



`InitialRound` and each `CorrectionRound` no longer store a `Result` object. They store only the readings (as they already do) plus a `CalculationParameters` record indicating how the operator last chose to process this round.



```csharp

public class MeasurementRound      // covers both initial and correction rounds

{

&#x20;   // ... existing fields (steps / replaced steps, completedAt, etc.) ...

&#x20;   public CalculationParameters CalculationParameters { get; set; }

}

```



\### 3c. In-memory result: `SurfaceResult`



`SurfaceResult` remains as an \*\*in-memory\*\* object only — it is never serialised. It is computed on demand and held in the ViewModel.



Add new fields to `SurfaceResult` for closure statistics (see §6).



\### 3d. On load



When a project file is loaded and a round has `CalculationParameters` set, the app runs the solver immediately to populate in-memory results. If `CalculationParameters` is null (e.g. measurement still in progress), no solver run occurs.



\### 3e. JSON shape after this change



```json

"initialRound": {

&#x20; "completedAt": "2026-04-04T10:45:00Z",

&#x20; "steps": \[ ... ],

&#x20; "calculationParameters": {

&#x20;   "methodId": "LeastSquares",

&#x20;   "sigmaThreshold": 2.5,

&#x20;   "autoExcludeOutliers": true,

&#x20;   "manuallyExcludedStepIndices": \[14]

&#x20; }

}

```



No `result` object in the file.



\---



\## 4. Calculation methods



Both methods implement a new interface:



```csharp

public interface ISurfaceCalculator

{

&#x20;   string MethodId { get; }

&#x20;   string DisplayName { get; }

&#x20;   SurfaceResult Calculate(

&#x20;       IReadOnlyList<MeasurementStep> steps,

&#x20;       ObjectDefinition definition,

&#x20;       CalculationParameters parameters);

}

```



Register available calculators in `SurfacePlateModule` (or a static registry). The `MethodId` on `CalculationParameters` is used to look up the correct implementation at runtime.



\### 4a. Least-squares (extended for diagonals)



\*\*Existing behaviour:\*\* builds a system of closure equations at row/column crossing points and solves via least-squares (e.g. `MathNet.Numerics` or hand-rolled normal equations).



\*\*Extension for Union Jack:\*\* diagonal passes create additional crossing points where a diagonal node coincides with a row or column node. Each such coincidence generates an additional closure equation of the same form:



```

height\_from\_diagonal(i,j) − height\_from\_row\_or\_col(i,j) = 0  (+ residual)

```



The equation system grows but the solver structure is unchanged — it remains a linear least-squares problem `Ax = b`.



\*\*Outlier handling:\*\* after the initial solve, compute per-step residuals. If `AutoExcludeOutliers` is true, flag steps where `|residual| > SigmaThreshold × σ`. Add their indices to an effective exclusion set (union of flagged and `ManuallyExcludedStepIndices`). Re-solve with those steps removed from the equation system. Store the effective exclusion set in `SurfaceResult` (in-memory only).



\### 4b. Sequential integration with proportional closure distribution



```

For each line L (row, column, or diagonal):

&#x20; 1. Integrate: height\[node\_k] = sum of readings from start of line to node\_k × span\_per\_step

&#x20; 2. At each crossing point with another line L':

&#x20;      closure\_error = height\_L(crossing) − height\_L'(crossing)

&#x20; 3. Distribute the closure error linearly along line L:

&#x20;      correction\[node\_k] = −closure\_error × (k / (nodes\_in\_line − 1))

&#x20; 4. Apply corrections to get adjusted heights for line L



Final height at each grid node = average of all adjusted heights from lines passing through it.

```



This method requires no matrix algebra. It is less optimal than least-squares (errors are distributed line-by-line rather than globally) but produces intuitively understandable results and is a useful cross-check.



Outlier handling: same sigma-threshold logic applied to per-step residuals after the final heights are determined.



\---



\## 5. Recalculation UI



\### Trigger



Add a \*\*Recalculate\*\* button to `ResultsView`, visible whenever a completed session is shown.



\### Recalculation dialog / panel



A `ContentDialog` (or inline expander panel — designer's choice) with:



| Control | Purpose |

|---|---|

| `ComboBox` — Calculation method | Lists available `ISurfaceCalculator` implementations by `DisplayName` |

| `NumberBox` — Sigma threshold | Default 2.5; enabled only when auto-exclude is on |

| `ToggleSwitch` — Auto-exclude outliers | Enables/disables sigma-threshold exclusion |

| List of flagged steps with checkboxes | Shows steps flagged in the \*current\* result; operator can uncheck to manually exclude |

| \*\*Recalculate\*\* button | Runs solver with new parameters; updates in-memory result and UI |

| \*\*Save parameters\*\* button | Persists the new `CalculationParameters` to the project (triggers file save) |



\*\*Important:\*\* "Recalculate" only updates the in-memory result. "Save parameters" is a separate action that writes `CalculationParameters` to the project file. This keeps the raw-readings-only persistence contract clean.



\---



\## 6. Closure loop statistics



\### Definition



A \*\*closure loop\*\* is a minimal convex polygon whose vertices are grid nodes and whose edges are segments that were directly measured (i.e. consecutive steps within a single pass).



For Full Grid: the unit rectangles formed by adjacent row and column segments.

For Union Jack: additionally, the triangles formed by one row segment + one column segment + one diagonal segment at their shared corner nodes.



Only enumerate loops involving edges that were actually measured in the current session — do not construct hypothetical loops.



\### Closure error per loop



```

closure\_error = sum of signed incremental readings around the loop

```



Sign convention: traverse the loop clockwise; readings in the traversal direction are positive, against it are negative.



In theory this sum is zero. In practice it reflects the noise and drift accumulated around that loop.



\### New fields on `SurfaceResult`



```csharp

public class SurfaceResult

{

&#x20;   // ... existing fields ...

&#x20;   public double\[]   HeightMapMm         { get; set; }   // 2D grid, row-major

&#x20;   public double     FlatnessValueMm     { get; set; }

&#x20;   public double\[]   StepResiduals       { get; set; }   // one per step

&#x20;   public int\[]      FlaggedStepIndices  { get; set; }

&#x20;   public int\[]      EffectivelyExcludedStepIndices { get; set; }



&#x20;   // New closure statistics

&#x20;   public double\[]   ClosureErrors       { get; set; }   // one per loop

&#x20;   public double     ClosureErrorMean    { get; set; }

&#x20;   public double     ClosureErrorMedian  { get; set; }

&#x20;   public double     ClosureErrorMax     { get; set; }   // absolute value

&#x20;   public double     ClosureErrorRms     { get; set; }

}

```



\### Display



Add a \*\*Closure errors\*\* section to the statistics panel in `ResultsView`, below the existing Flatness and Residual RMS entries:



```

Closure errors

&#x20; Mean      0.003 μm

&#x20; Median    0.002 μm

&#x20; Max       0.041 μm

&#x20; RMS       0.008 μm

```



No new views required. This is purely additive to the existing panel.



\---



\## 7. Files created / modified



\### New files

\- `LevelApp.Core/Geometry/SurfacePlate/Strategies/UnionJackStrategy.cs`

\- `LevelApp.Core/Geometry/SurfacePlate/Calculators/ISequentialIntegrationCalculator.cs` \*(if splitting interface per method)\*

\- `LevelApp.Core/Geometry/SurfacePlate/Calculators/LeastSquaresCalculator.cs`

\- `LevelApp.Core/Geometry/SurfacePlate/Calculators/SequentialIntegrationCalculator.cs`

\- `LevelApp.Core/Models/CalculationParameters.cs`

\- `LevelApp.Core/Interfaces/ISurfaceCalculator.cs`



\### Modified files

\- `LevelApp.Core/Models/Orientation.cs` (or wherever the enum lives) — add diagonal values

\- `LevelApp.Core/Models/MeasurementRound.cs` — remove `Result`, add `CalculationParameters`

\- `LevelApp.Core/Models/CorrectionRound.cs` — remove `Result`

\- `LevelApp.Core/Models/SurfaceResult.cs` — add closure fields

\- `LevelApp.Core/Geometry/SurfacePlate/SurfacePlateModule.cs` — register `UnionJackStrategy` and both calculators

\- `LevelApp.Core/Geometry/SurfacePlate/SurfacePlateCalculator.cs` — refactor into `LeastSquaresCalculator`, extend for diagonals

\- `LevelApp.Core/Serialization/ProjectSerializer.cs` — update for new JSON shape (no results, `CalculationParameters` added)

\- `LevelApp.App/Views/ResultsView.xaml` — Recalculate button, closure stats panel

\- `LevelApp.App/ViewModels/ResultsViewModel.cs` — recalculation logic, dialog handling

\- `LevelApp.App/Views/MeasurementView.xaml` — orientation arrow for diagonal directions

\- `docs/architecture.md` — update data model, strategy list, solver description



\---



\## 8. Acceptance criteria



\- \[ ] Union Jack strategy appears in the strategy selector on the project setup screen

\- \[ ] A Union Jack session generates the correct step sequence: all rows, all columns, perimeter, both diagonals

\- \[ ] Step instructions and orientation arrows display correctly for all eight orientations

\- \[ ] The least-squares solver produces a valid result for a Union Jack session

\- \[ ] The sequential integration method produces a result for both Full Grid and Union Jack sessions

\- \[ ] Calculation method and parameters are stored in the project file; no derived result data is stored

\- \[ ] Loading a project with `CalculationParameters` present automatically runs the solver and displays results

\- \[ ] The Recalculate button opens the recalculation dialog; changing method or parameters and clicking Recalculate updates the displayed result

\- \[ ] Save parameters persists the new `CalculationParameters` to the project file

\- \[ ] Auto-exclude and manual exclusion both correctly remove steps from the solver

\- \[ ] Closure error statistics (mean, median, max, RMS) appear in the results panel for both strategy types

\- \[ ] All existing unit tests pass; new unit tests cover `UnionJackStrategy.GenerateSteps`, `LeastSquaresCalculator` with diagonal constraints, `SequentialIntegrationCalculator`, and closure loop enumeration



