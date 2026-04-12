# Work Package 07 — Parallel Ways Geometry Module

> Builds on WP_06 (cleanup). Introduces a complete second geometry type: **Parallel Ways**.
> Covers Core models, strategy, calculator, serialization, UI (setup + measurement + results), and persistence.
> No changes to Surface Plate code except where explicitly noted (e.g. shared infrastructure).

---

## 1. Overview

Parallel Ways models a set of N ≥ 2 linear rails (ways) arranged in 3D space — typical examples are lathe beds, milling machine guideways, and similar machine tool slideways. The module follows the same plugin architecture as Surface Plate: it implements `IGeometryModule`, provides one or more `IMeasurementStrategy` implementations, has its own calculator, and its own result display module.

---

## 2. Core — Models & Data

### 2.1 `RailDefinition.cs` (new, in `LevelApp.Core/Models/`)

```csharp
public class RailDefinition
{
    public string Label { get; set; } = string.Empty;   // user-defined, e.g. "Front", "Rear"
    public double LengthMm { get; set; }
    public double AxialOffsetMm { get; set; }            // offset from reference rail start, along travel axis
    public double LateralSeparationMm { get; set; }     // perpendicular distance from reference rail
    public double VerticalOffsetMm { get; set; }        // nominal height difference from reference rail
}
```

All offsets are zero for the reference rail.

### 2.2 `ParallelWaysParameters.cs` (new, in `LevelApp.Core/Models/` or inline as a helper — use whichever pattern matches existing code)

This is the typed parameter bag for the Parallel Ways object definition. It is serialized into `ObjectDefinition.Parameters` (the existing flexible key-value dictionary) via JSON round-trip, exactly as Surface Plate parameters are handled today.

```csharp
public class ParallelWaysParameters
{
    public WaysOrientation Orientation { get; set; } = WaysOrientation.Horizontal;
    public int ReferenceRailIndex { get; set; } = 0;
    public List<RailDefinition> Rails { get; set; } = new();
}

public enum WaysOrientation { Horizontal, Vertical }
```

Serialization note: store as `"orientation"`, `"referenceRailIndex"`, `"rails"` keys inside `ObjectDefinition.Parameters`, consistent with how surface plate stores `"widthMm"` etc.

### 2.3 `ParallelWaysTask.cs` (new, in `LevelApp.Core/Models/`)

A measurement task is the unit of work within a Parallel Ways strategy. The strategy produces an **ordered list of tasks**; each task produces a contiguous block of `MeasurementStep`s.

```csharp
public enum TaskType { AlongRail, Bridge }
public enum PassDirection { SinglePass, ForwardAndReturn }

public class ParallelWaysTask
{
    public TaskType TaskType { get; set; }
    public int RailIndexA { get; set; }          // for AlongRail: the rail; for Bridge: first rail
    public int RailIndexB { get; set; }          // for Bridge only: second rail
    public PassDirection PassDirection { get; set; } = PassDirection.SinglePass;
    public double StepDistanceMm { get; set; }   // station spacing along the rail(s)
    // Derived — not stored, computed from rail length and step distance:
    // int StationCount => (int)Math.Round(RailLength / StepDistanceMm) + 1
}
```

### 2.4 `ParallelWaysStrategyParameters.cs` (new)

```csharp
public enum DriftCorrectionMethod { FirstStationAnchor, LinearDriftCorrection, LeastSquares }
public enum SolverMode { IndependentThenReconcile, GlobalLeastSquares }

public class ParallelWaysStrategyParameters
{
    public List<ParallelWaysTask> Tasks { get; set; } = new();
    public DriftCorrectionMethod DriftCorrection { get; set; } = DriftCorrectionMethod.LeastSquares;
    public SolverMode SolverMode { get; set; } = SolverMode.GlobalLeastSquares;
}
```

These are stored in `MeasurementSession` alongside `StrategyId`, serialized to JSON.

### 2.5 `ParallelWaysResult.cs` (new, in `LevelApp.Core/Models/`)

```csharp
public class RailProfile
{
    public int RailIndex { get; set; }
    public double[] HeightProfileMm { get; set; } = Array.Empty<double>();  // deviation from best-fit line, per station
    public double[] StationPositionsMm { get; set; } = Array.Empty<double>(); // absolute axial positions
    public double StraightnessValueMm { get; set; }   // peak-to-valley of HeightProfileMm
}

public class ParallelismProfile
{
    public int RailIndexA { get; set; }
    public int RailIndexB { get; set; }
    public double[] DeviationMm { get; set; } = Array.Empty<double>();   // height difference at common stations
    public double[] StationPositionsMm { get; set; } = Array.Empty<double>();
    public double ParallelismValueMm { get; set; }    // peak-to-valley
}

public class ParallelWaysResult
{
    public List<RailProfile> RailProfiles { get; set; } = new();
    public List<ParallelismProfile> ParallelismProfiles { get; set; } = new();
    public double[] Residuals { get; set; } = Array.Empty<double>();
    public int[] FlaggedStepIndices { get; set; } = Array.Empty<int>();
    public double SigmaThreshold { get; set; } = 2.5;
    public double ResidualRms { get; set; }
}
```

---

## 3. Core — Geometry Module

### 3.1 `ParallelWaysModule.cs` (new, in `LevelApp.Core/Geometry/ParallelWays/`)

Implements `IGeometryModule`:

```csharp
public class ParallelWaysModule : IGeometryModule
{
    public string ModuleId => "ParallelWays";
    public string DisplayName => "Parallel Ways";
    public IEnumerable<IMeasurementStrategy> AvailableStrategies => new[] { new ParallelWaysStrategy() };
    public IGeometryCalculator CreateCalculator(ObjectDefinition definition) => new ParallelWaysCalculator(definition);
}
```

Register this module alongside `SurfacePlateModule` wherever the app assembles its module list (likely in `App` startup / DI registration).

---

## 4. Core — Measurement Strategy

### 4.1 `ParallelWaysStrategy.cs` (new, in `LevelApp.Core/Geometry/ParallelWays/Strategies/`)

Implements `IMeasurementStrategy`:

- `StrategyId` = `"ParallelWays"`
- `DisplayName` = `"Parallel Ways"`

`GenerateSteps(ObjectDefinition definition)` interprets `definition.Parameters` to extract `ParallelWaysParameters` and `ParallelWaysStrategyParameters`, then produces the full flat `IReadOnlyList<MeasurementStep>` by iterating tasks in order.

**Step generation rules:**

For each task in `Tasks` (in order):

**AlongRail task, SinglePass:**
- Stations: 0, 1, … N (where N = floor(RailLength / StepDistance))
- Each step: `GridCol` = station index, `GridRow` = rail index, `Orientation` = derived from WaysOrientation (Horizontal → East for forward pass; Vertical → South for forward pass)
- `InstructionText` = e.g. `"Rail '{label}' — station {i} of {N}, moving {direction}"`

**AlongRail task, ForwardAndReturn:**
- Forward: stations 0 → N (as above)
- Return: stations N → 0 (orientation flips: West / North)
- All steps contiguous in the list, tagged with a `PassPhase` property (Forward / Return) — **add `PassPhase` enum and property to `MeasurementStep`** (see §4.2)

**Bridge task, SinglePass:**
- Stations: 0, 1, … N along the common axis
- Each step: `GridCol` = station index, `GridRow` = encodes the pair (e.g. `RailIndexA * 100 + RailIndexB` — document this encoding), `Orientation` = perpendicular to travel
- `InstructionText` = e.g. `"Bridge {labelA}↔{labelB} — station {i}, instrument spanning from {labelA} to {labelB}"`

**Bridge task, ForwardAndReturn:**
- Same forward/return pattern as AlongRail.

Station positions in mm: `StationPositionMm = RailAxialOffset + stationIndex * StepDistanceMm`

### 4.2 `MeasurementStep` additions

Add to existing `MeasurementStep.cs`:

```csharp
public enum PassPhase { NotApplicable, Forward, Return }

// Add property:
public PassPhase PassPhase { get; set; } = PassPhase.NotApplicable;
```

This is a non-breaking addition. Surface Plate steps will have `PassPhase.NotApplicable`.

---

## 5. Core — Calculator

### 5.1 `ParallelWaysCalculator.cs` (new, in `LevelApp.Core/Geometry/ParallelWays/`)

Implements `IGeometryCalculator` (or the equivalent interface pattern used by `SurfacePlateCalculator`).

**Input:** completed `MeasurementSession` with all steps having readings, plus `ParallelWaysParameters` and `ParallelWaysStrategyParameters`.

**Output:** `ParallelWaysResult`

#### Algorithm — step by step:

**Step 1: Group steps by task**
Partition the flat step list back into tasks using the same task order as the strategy.

**Step 2: Integrate along-rail readings into raw height profiles**

For each AlongRail task:
```
Δh[i] = reading[i] × stepDistanceMm / 1000.0
h[0] = 0
h[i+1] = h[i] + Δh[i]
```

**Step 3: Apply drift correction (per ForwardAndReturn task)**

Three modes, selected by `DriftCorrectionMethod`:

- **FirstStationAnchor:** Use forward pass only. `h[0] = 0`, integrate forward. Return pass ignored for height (but its residuals are still computed).
- **LinearDriftCorrection:** Compute closure error `ε = h_forward[N] - h_return_reversed[N]`. Apply linear correction: `h_corrected[i] = h_forward[i] - i * ε / N`.
- **LeastSquares:** Set up a least-squares system where forward and return readings are both observations of the same station heights. Solve for the N+1 station heights that minimise the sum of squared residuals. Datum: first station = 0.

**Step 4: Remove best-fit line (datum fixing)**

For each measured rail profile, fit a least-squares line through all station heights and subtract it. This anchors the datum — the reference rail's best-fit line becomes zero by definition. If the reference rail is unmeasured, its profile is all zeros.

**Step 5: Integrate bridge readings**

For each Bridge task:
```
Δh_bridge[i] = reading[i] × gaugeMm / 1000.0
```
where `gaugeMm = sqrt(lateralSeparation² + verticalOffset²)` between the two rails.

Bridge readings give direct height difference between rails at each station: `h_B[i] - h_A[i] = Δh_bridge[i]`.

**Step 6: Solver mode**

- **IndependentThenReconcile:** Rail profiles are solved independently (Steps 2–4). Bridge data is then used to check consistency — compute differences between the independently-solved profiles at common bridge stations and compare to bridge readings. Discrepancies become residuals.
- **GlobalLeastSquares:** Set up a single least-squares system with all along-rail readings and all bridge readings as observations, all station heights as unknowns. Solve simultaneously. Datum: reference rail first station = 0 and reference rail best-fit line removed.

**Step 7: Compute parallelism profiles**

For each pair of rails that share common station positions (interpolate if needed, but prefer coincident stations):
```
parallelism[i] = h_B[i] - h_A[i]
ParallelismValueMm = max(parallelism) - min(parallelism)
```

**Step 8: Compute residuals and flag outliers**

Per-step residual = observed reading minus the reading implied by the solved profile. Flag steps where `|residual| > sigmaThreshold × σ`.

**Step 9: Compute RMS**

`ResidualRms = sqrt(mean(residuals²))`

---

## 6. Serialization

### 6.1 `ParallelWaysResult` serialization

Add `ParallelWaysResult` to the JSON serialization infrastructure alongside `SurfaceResult`. The `MeasurementSession.Result` field (or equivalent) must be able to hold either type. Use the same pattern already established — likely a discriminated union via a type tag in JSON, or a wrapper object. Follow whatever pattern `SurfaceResult` uses today.

### 6.2 `ParallelWaysStrategyParameters` serialization

Store in `MeasurementSession` as a new field `StrategyParameters` (object, nullable), serialized as JSON. Surface Plate sessions leave this null.

### 6.3 `PassPhase` serialization

The new `PassPhase` property on `MeasurementStep` must be included in JSON serialization. Default value `NotApplicable` should serialize/deserialize cleanly for existing Surface Plate files (add to existing converters as needed).

### 6.4 Schema version

Bump `schemaVersion` from `"1.0"` to `"1.1"` in new files. Add migration logic: if loading a `"1.0"` file, set `PassPhase = NotApplicable` on all steps (which will already be the case since the field didn't exist, so JSON default binding handles this automatically — just verify it works).

---

## 7. App — Project Setup UI

### 7.1 `ProjectSetupView.xaml` / `ProjectSetupViewModel.cs` changes

When the user selects **Parallel Ways** as geometry type, the parameter entry section changes dynamically (same pattern as Surface Plate shows Width/Height/Columns/Rows).

**Parallel Ways parameter entry UI:**

- **Orientation** — ComboBox: `Horizontal` / `Vertical`
- **Rails** — a dynamic list of rail entries. Each row shows:
  - Label (TextBox)
  - Length mm (NumberBox)
  - Axial Offset mm (NumberBox)
  - Lateral Separation mm (NumberBox)
  - Vertical Offset mm (NumberBox)
  - Reference rail radio button (only one selected at a time)
  - Remove button (enabled if > 2 rails remain)
- **Add Rail** button — appends a new rail with default values
- Validation: minimum 2 rails, exactly one reference rail

**Strategy section (below parameters, same as Surface Plate):**

For Parallel Ways, the strategy ComboBox shows only `"Parallel Ways"`. Below it, show the **task list editor**:

- Ordered list of tasks. Each task row shows:
  - Task type: ComboBox `Along Rail` / `Bridge`
  - For Along: rail selector ComboBox (rail labels), pass direction ComboBox (`Single` / `Forward + Return`), step distance NumberBox (mm)
  - For Bridge: two rail selectors (ComboBox), pass direction, step distance
  - Up/Down arrow buttons to reorder tasks
  - Remove button
- **Add Task** button
- **Drift Correction** ComboBox: `First Station Anchor` / `Linear Drift Correction` / `Least Squares` (default)
- **Solver Mode** ComboBox: `Independent then Reconcile` / `Global Least Squares` (default)
- Validation: at least one task must be present

---

## 8. App — Measurement UI

The existing `MeasurementView` and `MeasurementViewModel` are used unchanged for Parallel Ways — the guided step-by-step workflow is geometry-agnostic. The step instruction text and orientation arrow already come from `MeasurementStep.InstructionText` and `MeasurementStep.Orientation`.

**Grid map in MeasurementView:** The current grid map shows a 2D dot grid. For Parallel Ways:
- Show dots arranged as N rails × M stations (rows = rails, columns = stations)
- Current step highlighted in orange, completed steps in green, pending in grey
- This is a layout-only change driven by the step's `GridRow` / `GridCol` values, which already encode rail index and station index respectively

No ViewModel changes required if the grid map rendering already uses `GridRow`/`GridCol` generically. If it hard-codes Surface Plate assumptions, refactor minimally to make it generic.

---

## 9. App — Results UI

### 9.1 `ParallelWaysDisplay.cs` (new, in `LevelApp.App/DisplayModules/ParallelWaysDisplay/`)

Implements `IResultDisplay`.

- `DisplayId` = `"ParallelWays3D"`
- `DisplayName` = `"Parallel Ways Plot"`

**Visual design (3D view, consistent with existing `SurfacePlot3DDisplay`):**

The view uses the same Win2D / WinUI drawing infrastructure as the surface plate 3D plot.

Elements to render:

1. **Rail skeleton lines** — thin grey lines, one per rail, running along the rail's axial direction. Positioned in 3D space using each rail's `LateralSeparationMm` and `VerticalOffsetMm` from the geometry definition. Apply the same isometric projection used by the surface plate display.

2. **Station dots** — circle at every measured station. Color = deviation from theoretical geometry (the `HeightProfileMm` value at that station), using the same colour mapping as the surface plate (blue = negative deviation, green = zero, red = positive deviation). Scale the colour range to the min/max deviation across all rails.

3. **Along-rail measurement segments** — bold colored line connecting consecutive station dots on the same rail. Use a distinct color or weight from bridge segments. Suggested: use the accent color, medium weight.

4. **Bridge measurement segments** — bold line connecting the two station dots at the same axial position on the bridged rail pair. Use a different color (e.g. orange or a secondary accent) to distinguish from along-rail segments.

**Right panel (metrics):**

For each measured rail:
- Rail label
- Straightness (peak-to-valley): `{value} µm`

For each measured pair:
- Pair labels (A ↔ B)
- Parallelism (peak-to-valley): `{value} µm`

Global:
- Residual RMS: `σ = {value} µm`
- Flagged steps count (or "No flagged steps")

### 9.2 `ResultsView` / `ResultsViewModel` changes

The results view already discovers display modules. Register `ParallelWaysDisplay` alongside `SurfacePlot3DDisplay`. The active display is chosen based on the session's `GeometryModuleId` — Surface Plate sessions use the surface plot, Parallel Ways sessions use the parallel ways display.

---

## 10. File Structure — New Files

```
LevelApp.Core/
├── Models/
│   ├── RailDefinition.cs                          [NEW]
│   ├── ParallelWaysParameters.cs                  [NEW]
│   ├── ParallelWaysTask.cs                        [NEW]
│   ├── ParallelWaysStrategyParameters.cs          [NEW]
│   └── ParallelWaysResult.cs                      [NEW]
├── Geometry/
│   └── ParallelWays/
│       ├── ParallelWaysModule.cs                  [NEW]
│       ├── ParallelWaysCalculator.cs              [NEW]
│       └── Strategies/
│           └── ParallelWaysStrategy.cs            [NEW]

LevelApp.App/
└── DisplayModules/
    └── ParallelWaysDisplay/
        └── ParallelWaysDisplay.cs                 [NEW]
```

**Modified files:**
- `LevelApp.Core/Models/MeasurementStep.cs` — add `PassPhase` enum and property
- `LevelApp.Core/Serialization/` — extend JSON converters for new types
- `LevelApp.App/Views/ProjectSetupView.xaml` — Parallel Ways parameter + task editor UI
- `LevelApp.App/ViewModels/ProjectSetupViewModel.cs` — Parallel Ways parameter binding
- `LevelApp.App/Views/MeasurementView.xaml` (if grid map is not already generic)
- `LevelApp.App/ViewModels/ResultsViewModel.cs` — register new display module
- Module registration (wherever `SurfacePlateModule` is registered — add `ParallelWaysModule`)
- `docs/architecture.md` — update after completion (per CLAUDE.md instructions)

---

## 11. Unit Tests

Add to `LevelApp.Tests/`:

- `ParallelWaysStrategyTests.cs`
  - Verify correct step count for a simple 2-rail, 5-station, single-pass along-rail task
  - Verify step count doubles for forward+return
  - Verify station positions are correct with axial offset
  - Verify bridge task step count

- `ParallelWaysCalculatorTests.cs`
  - Single rail, known readings → verify integrated profile
  - Forward+return, linear drift → verify corrected profile matches known answer
  - Two rails + bridge readings → verify parallelism profile
  - Outlier detection: inject one large reading, verify it is flagged

---

## 12. Validation Rules

| Rule | Where enforced |
|---|---|
| Minimum 2 rails | ProjectSetupViewModel |
| Exactly one reference rail | ProjectSetupViewModel |
| At least one task | ProjectSetupViewModel |
| Step distance > 0 | ProjectSetupViewModel |
| Step distance ≤ rail length | ProjectSetupViewModel |
| Bridge task: two different rails | ProjectSetupViewModel |
| All rail lengths > 0 | ProjectSetupViewModel |

---

## 13. Out of Scope for WP_07

The following are explicitly deferred:

- Correction workflow for Parallel Ways (flagged step re-measurement UI) — same correction infrastructure as Surface Plate will be reused in a later WP
- Heat map / numerical table display modules
- Bluetooth / USB HID instrument providers
- PDF export
