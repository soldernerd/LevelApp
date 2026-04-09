# LevelApp — Project Architecture & Design Reference



> Living document. Update as the project evolves.

> Last updated: 2026-04-09 *(revised to reflect v0.8.4: ThemeHelper, IThemeService, code quality improvements)*



---



## 1. Purpose



A Windows desktop application for evaluating precision electronic level measurements used in machine tool geometry inspection and granite surface plate qualification. The software acquires readings from an instrument (manually entered initially, Bluetooth/USB HID later), guides the operator through a defined measurement procedure, computes a best-fit surface map using least-squares adjustment, detects suspect readings, and displays results graphically.



The industry reference for this domain is **Wyler AG, Winterthur** (wylerag.com) and their wylerSOFT suite.



---



## 2. Technology Stack



| Concern | Choice | Rationale |
|---|---|---|
| Language | C# (.NET 8) | Best WinUI 3 ecosystem support |
| UI Framework | WinUI 3 / Windows App SDK 1.8 | Modern Windows-native, Fluent Design, access to WinRT APIs |
| IDE | Visual Studio 2022 Community | Free, full WinUI 3 tooling, XAML designer |
| Solution format | `.slnx` | New Visual Studio solution format |
| UI Pattern | MVVM | Mandatory for WinUI 3; decouples UI from logic |
| MVVM helpers | CommunityToolkit.Mvvm 8.3.2 | Source-generated `ObservableProperty`, `RelayCommand`, `ObservableObject` |
| Dependency injection | Microsoft.Extensions.DependencyInjection 8.0.1 | ViewModels and services resolved from `App.Services` container |
| Persistence | JSON via System.Text.Json | Human-readable, no dependencies, diffable |
| Bluetooth (future) | Windows.Devices.Bluetooth (WinRT) | First-class Windows API, no third-party libs needed |
| USB HID (future) | Windows.Devices.HumanInterfaceDevice (WinRT) | Same rationale |



---



## 3. Solution Structure



```
LevelApp/
├── LevelApp.slnx
├── LevelApp.Core/                 ← No UI dependencies. Fully unit-testable.
│   ├── AppVersion.cs              ← Single source of truth for Major.Minor.Patch
│   ├── Interfaces/
│   │   ├── IMeasurementStrategy.cs
│   │   └── ISurfaceCalculator.cs  ← single calculator interface (MethodId, Calculate)
│   ├── Models/
│   │   ├── Project.cs
│   │   ├── ObjectDefinition.cs
│   │   ├── MeasurementSession.cs
│   │   ├── MeasurementRound.cs    ← also contains MergeWithReplacements static helper
│   │   ├── MeasurementStep.cs     ← includes PassPhase enum
│   │   ├── CorrectionRound.cs     ← also contains ReplacedStep
│   │   ├── SurfaceResult.cs
│   │   ├── RailDefinition.cs
│   │   ├── ParallelWaysParameters.cs    ← WaysOrientation enum + From() helper
│   │   ├── ParallelWaysTask.cs          ← TaskType, PassDirection enums
│   │   ├── ParallelWaysStrategyParameters.cs  ← DriftCorrectionMethod, SolverMode enums + From()
│   │   └── ParallelWaysResult.cs        ← RailProfile, ParallelismProfile, ParallelWaysResult
│   ├── Geometry/
│   │   ├── StrategyFactory.cs     ← Create(strategyId) → IMeasurementStrategy
│   │   ├── CalculatorFactory.cs   ← Create(methodId, strategy) → ISurfaceCalculator
│   │   ├── Calculators/
│   │   │   ├── LeastSquaresCalculator.cs        ← least-squares solver + outlier detection
│   │   │   ├── SequentialIntegrationCalculator.cs ← proportional closure distribution
│   │   │   └── ClosureErrorCalculator.cs        ← shared closure-loop helper
│   │   ├── SurfacePlate/
│   │   │   └── Strategies/
│   │   │       ├── FullGridStrategy.cs
│   │   │       ├── UnionJackStrategy.cs
│   │   │       └── UnionJackRings.cs
│   │   └── ParallelWays/
│   │       ├── ParallelWaysCalculator.cs   ← standalone calculator returning ParallelWaysResult
│   │       └── Strategies/
│   │           └── ParallelWaysStrategy.cs
│   └── Serialization/
│       ├── ProjectSerializer.cs
│       ├── ObjectValueConverter.cs
│       └── OrientationConverter.cs   ← reads/writes Orientation as string enum
├── LevelApp.App/                  ← WinUI 3 application
│   ├── App.xaml / App.xaml.cs     ← DI container setup; merges resource dictionaries
│   ├── MainWindow.xaml / .cs      ← Menu bar (File, Edit, Help); wires IThemeService to RootFrame
│   ├── Helpers/
│   │   └── ThemeHelper.cs         ← GetColor, GetBrush, PlotRamp, InterpolateRamp (shared by all renderers)
│   ├── Styles/
│   │   ├── ThemeColors.xaml       ← ThemeDictionaries: all colour tokens (Light + Default/Dark)
│   │   ├── TextStyles.xaml        ← Named TextBlock styles keyed to ThemeResource tokens
│   │   └── ControlStyles.xaml     ← Implicit Button style; CardStyle; CompactCardStyle
│   ├── Navigation/
│   │   ├── PageKey.cs             ← Enum: ProjectSetup, Measurement, Results, Correction
│   │   ├── INavigationService.cs
│   │   ├── NavigationService.cs
│   │   ├── MeasurementArgs.cs     ← record(Project, Session)
│   │   ├── ResultsArgs.cs         ← record(Project, Session)
│   │   └── CorrectionArgs.cs      ← record(Project, Session)
│   ├── Services/
│   │   ├── IProjectFileService.cs ← interface for file I/O (testable)
│   │   ├── ProjectFileService.cs  ← Win32 IFileOpenDialog/IFileSaveDialog + JSON I/O
│   │   ├── ISettingsService.cs    ← DefaultProjectFolder, AppTheme (ElementTheme)
│   │   ├── SettingsService.cs     ← persists settings to %LOCALAPPDATA%\LevelApp\settings.json
│   │   ├── IThemeService.cs       ← Apply(ElementTheme), SetTarget(FrameworkElement)
│   │   └── ThemeService.cs        ← singleton; applies RequestedTheme to RootFrame
│   ├── Views/
│   │   ├── ProjectSetupView.xaml
│   │   ├── MeasurementView.xaml
│   │   ├── ResultsView.xaml
│   │   ├── CorrectionView.xaml
│   │   └── Dialogs/
│   │       ├── PreferencesDialog.xaml   ← default project folder + Light/Dark/System theme selector
│   │       ├── NewMeasurementDialog.xaml
│   │       ├── RecalculateDialog.xaml   ← recalculation parameters + save option
│   │       └── AboutDialog.xaml         ← version, copyright, license, GitHub link
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs       ← inherits ObservableObject
│   │   ├── MainViewModel.cs       ← shell state: window title, dirty flag, unsaved-changes dialog
│   │   ├── ProjectSetupViewModel.cs
│   │   ├── MeasurementViewModel.cs
│   │   ├── ResultsViewModel.cs
│   │   ├── CorrectionViewModel.cs
│   │   └── FlaggedStepItem.cs     ← display DTO for flagged step list
│   └── DisplayModules/
│       ├── SurfacePlot3D/
│       │   └── SurfacePlot3DDisplay.cs
│       ├── MeasurementsGrid/
│       │   └── MeasurementsGridRenderer.cs  ← 2D step-map canvas (arrows, loop fills, zoom)
│       ├── StrategyPreview/
│       │   └── StrategyPreviewRenderer.cs   ← small preview canvas in ProjectSetupView
│       └── ParallelWaysDisplay/
│           └── ParallelWaysDisplay.cs       ← 2D rail schematic with coloured station dots
├── LevelApp.Tests/
│   ├── FullGridStrategyTests.cs
│   ├── UnionJackStrategyTests.cs
│   ├── LeastSquaresCalculatorTests.cs
│   ├── ParallelWaysStrategyTests.cs
│   └── ParallelWaysCalculatorTests.cs
└── docs/
    ├── architecture.md               ← This file
    └── levelproj.md                  ← .levelproj JSON format reference
```



---



## 4. Core Interfaces



### ISurfaceCalculator

The single calculator interface. Both `LeastSquaresCalculator` and `SequentialIntegrationCalculator` implement it. Instantiated via `CalculatorFactory.Create(methodId, strategy)`.

```csharp
public interface ISurfaceCalculator
{
    string MethodId { get; }
    string DisplayName { get; }
    SurfaceResult Calculate(
        IReadOnlyList<MeasurementStep> steps,
        ObjectDefinition definition,
        CalculationParameters parameters);
}
```



### IMeasurementStrategy

Generates the ordered sequence of guided steps for a given object definition. A strategy's only job is to produce the step list — it knows nothing about calculation. Instantiated via `StrategyFactory.Create(strategyId)`.

```csharp
public interface IMeasurementStrategy
{
    string StrategyId { get; }
    string DisplayName { get; }
    IReadOnlyList<MeasurementStep> GenerateSteps(ObjectDefinition definition);
    (double X, double Y) GetNodePosition(MeasurementStep step, ObjectDefinition definition);
    (double X, double Y) GetToNodePosition(MeasurementStep step, ObjectDefinition definition);
    IReadOnlyList<IReadOnlyList<string>> GetPrimitiveLoopNodeIds(ObjectDefinition definition);
}
```



### StrategyFactory / CalculatorFactory

Two static factory classes in `Core/Geometry/` centralise creation of strategies and calculators. Adding a new strategy or algorithm = one new class + one line in the corresponding factory.

```csharp
// Core/Geometry/StrategyFactory.cs
public static IMeasurementStrategy Create(string strategyId);

// Core/Geometry/CalculatorFactory.cs
public static ISurfaceCalculator Create(string methodId, IMeasurementStrategy strategy);
```



---



## 5. Data Model Hierarchy



```
Project
├── Id, Name, CreatedAt, ModifiedAt, Operator, Notes
├── ObjectDefinition
│   ├── GeometryModuleId          ("SurfacePlate" or "ParallelWays")
│   └── Parameters                (Dictionary<string, object>; module interprets)
│       ├── SurfacePlate:   widthMm, heightMm, columnsCount / rowsCount  (FullGrid)
│       │                   widthMm, heightMm, segments, rings            (UnionJack)
│       └── ParallelWays:   orientation, referenceRailIndex, rails[], tasks[],
│                           driftCorrection, solverMode
└── Measurements[ ]
    └── MeasurementSession
        ├── Id, Label, TakenAt, Operator, InstrumentId, StrategyId, Notes
        ├── InitialRound  (MeasurementRound)
        │   ├── CompletedAt
        │   ├── Steps[ ]
        │   │   └── MeasurementStep
        │   │       ├── Index, GridCol, GridRow
        │   │       ├── NodeId, ToNodeId   (symbolic node identifiers)
        │   │       ├── Orientation        (North | South | East | West | diagonals)
        │   │       ├── PassPhase          (NotApplicable | Forward | Return)
        │   │       ├── InstructionText
        │   │       └── Reading  (double?  — null until operator records a value, mm/m)
        │   ├── Result  (SurfaceResult? — Surface Plate sessions)
        │   │   ├── NodeHeights            (Dictionary<string, double>)
        │   │   ├── FlatnessValueMm
        │   │   ├── Residuals[]            (one per step, in step order)
        │   │   ├── FlaggedStepIndices[]
        │   │   ├── SigmaThreshold
        │   │   ├── Sigma                  (residual RMS with DOF correction, mm)
        │   │   └── PrimitiveLoops[]       (closure loops for Union Jack Full)
        │   └── ParallelWaysResult  (ParallelWaysResult? — Parallel Ways sessions)
        │       ├── RailProfiles[]
        │       │   └── RailProfile
        │       │       ├── RailIndex
        │       │       ├── HeightProfileMm[]      (straightness after line removal)
        │       │       ├── StationPositionsMm[]
        │       │       └── StraightnessValueMm    (peak-to-valley)
        │       ├── ParallelismProfiles[]
        │       │   └── ParallelismProfile
        │       │       ├── RailIndexA, RailIndexB
        │       │       ├── DeviationMm[]          (hB − hA at common stations)
        │       │       ├── StationPositionsMm[]
        │       │       └── ParallelismValueMm     (peak-to-valley)
        │       ├── Residuals[]
        │       ├── FlaggedStepIndices[]
        │       ├── SigmaThreshold
        │       └── ResidualRms
        └── Corrections[ ]
            └── CorrectionRound
                ├── Id, TriggeredAt, Operator, Notes
                ├── ReplacedSteps[ ]
                │   └── ReplacedStep
                │       ├── OriginalStepIndex
                │       └── Reading  (double, mm/m)
                └── Result  (SurfaceResult? — same structure as above)
```

**Key rule:** Raw readings and all intermediate results are **always preserved**. Nothing is ever overwritten. Results reflect the latest correction round, but full history is queryable.



---



## 6. Measurement Strategies



### Full Grid

The standard approach. Traverses all rows (boustrophedon — alternating direction to avoid instrument repositioning) then all columns. Every interior grid point is visited twice, once horizontally and once vertically.

`GridCol`/`GridRow` on each step is the **from** endpoint; `Orientation` points toward the **to** endpoint.

Total steps = `rows × (cols − 1) + cols × (rows − 1)`

```
Row pass:
  Row 0:  (0,0)→(1,0)→...→(cols-2,0)   orientation: East
  Row 1:  (cols-1,1)→...→(1,1)          orientation: West
  Row 2:  (0,2)→...→(cols-2,2)          orientation: East
  ...

Column pass:
  Col 0:  (0,0)→(0,1)→...→(0,rows-2)   orientation: South
  Col 1:  (1,rows-1)→...→(1,1)          orientation: North
  ...
```



### Union Jack

Eight arms radiate from the centre node in the cardinal and diagonal directions (N, NE, E, SE, S, SW, W, NW). Each arm is divided into a configurable number of equal segments. Two ring variants are supported:

- **Full** — a complete circumference ring is added, creating closure loops between adjacent arm tips. The solver can compute closure errors for each loop, and the Measurements tab colour-codes each triangular sector by its closure error relative to σ.
- **Circumference** — only the eight arms are measured (no ring). There are no closure loops and no loop-error colour coding.

`NodeId` / `ToNodeId` on each step are symbolic identifiers (e.g. `"center"`, `"armN_seg2"`, `"armNE_seg3"`). `UnionJackStrategy.NodePositionById` converts them to physical (mm) coordinates for rendering and calculation. Total steps depend on the number of arms, segments per arm, and whether a circumference ring is present.



### Parallel Ways

Measures the straightness of two or more parallel rails (or machine beds, slideways, etc.) and the parallelism between them.

**Rail / task model** — the operator defines two or more `RailDefinition` objects (label, length, lateral separation, vertical offset, axial offset) and a list of `ParallelWaysTask` objects. Each task is either:
- `AlongRail` — traverse one rail end-to-end with a given step distance; forward-only or forward-and-return pass.
- `Bridge` — measure across two rails at each common station; same pass options.

**Node ID scheme** — every node is `"rail{r}_sta{s}"` where `r` is the 0-based rail index and `s` is the 0-based station index. Bridge steps go from `"rail{rA}_sta{s}"` to `"rail{rB}_sta{s}"`.

**`PassPhase`** — each step carries a `PassPhase` enum value:
- `NotApplicable` — single-pass tasks.
- `Forward` — first pass of a forward-and-return task.
- `Return` — second (return) pass.

**Two solver modes** (set in `SolverMode` enum on `ParallelWaysStrategyParameters`):
- `GlobalLeastSquares` — all readings are submitted to one joint least-squares system; the reference rail station `r=0, s=0` is the height datum.
- `IndependentThenReconcile` — each rail is solved independently first, then a secondary least-squares step reconciles the bridge readings to bring the rails onto a common datum.

**Output** — `ParallelWaysCalculator.Calculate` returns a `ParallelWaysResult` (stored separately from `SurfaceResult` on `MeasurementRound`) containing per-rail `RailProfile` objects (height profile after best-fit line removal, straightness peak-to-valley value) and per-pair `ParallelismProfile` objects (height difference profile, parallelism peak-to-valley value).

**`ParallelWaysStrategy`** implements `IMeasurementStrategy` and is registered in `StrategyFactory` under the strategy ID `"ParallelWays"`. `ParallelWaysCalculator` is a standalone class (not `ISurfaceCalculator`) invoked directly by `MeasurementViewModel`.

### Adding new strategies

Implement `IMeasurementStrategy`, register with the geometry module. No other changes required.



---



## 7. Algorithm: Least-Squares Surface Fitting



### Why least-squares?

With a full grid, every interior point appears in two independent measurement lines (one row pass, one column pass). In theory the integrated heights must agree at every crossing point. In practice they don't, due to instrument noise and drift. Least-squares distributes the inconsistencies optimally across all steps simultaneously.



### Linear model

Each step contributes one equation:

```
h[to] − h[from] = reading × stepLen / 1000
```

where `reading` is in mm/m and `stepLen` is the physical distance between adjacent grid nodes in mm. The full overdetermined system `A h = b` (one row per step, one column per grid node) is solved via normal equations `AᵀA h = Aᵀb`. The node `h[0]` is fixed to zero as the height reference. Gaussian elimination with partial pivoting is used internally.

Degrees of freedom: `DOF = M − (N − 1)` where M = number of steps, N = number of grid nodes.



### Per-step residuals

After solving, every step has a residual:

```
residual_i = (h[to] − h[from]) − delta_i
```



### Sigma (RMS)

```
σ = sqrt( Σ residual_i² / DOF )
```

Stored as `SurfaceResult.Sigma`.



### Outlier detection

Flag any step where:

```
|residual_i| > k × σ
```

where σ is the RMS of all residuals and k is configurable (default: 2.5, stored as `SigmaThreshold`).

The software presents flagged steps sorted by step index, with the original reading shown for comparison.



### Correction workflow

1. Solver flags suspect steps after initial round
2. User reviews flagged list, optionally triggers a correction session
3. Guided mini-session visits only the flagged steps (showing original reading for comparison)
4. New readings are stored as a `CorrectionRound` — originals untouched
5. Full recalculation runs on the merged dataset (original readings, with replacements applied)
6. Process can repeat until no steps are flagged or user accepts the result



---



## 8. Guided Measurement State Machine



Navigation is managed by `INavigationService` / `NavigationService`, which maps a `PageKey` enum to concrete `Page` types. Each transition passes a typed navigation-args record (`MeasurementArgs`, `ResultsArgs`, or `CorrectionArgs`) carrying the `Project` and `MeasurementSession`.

```
[ProjectSetup]
      ↓ StartMeasurement → MeasurementArgs(project, session)
[Measurement: step N of M]  ←─────────────────────────────┐
      │                                                     │
      │  UI shows:                                          │
      │  • Grid canvas, current position highlighted        │
      │  • Instrument orientation arrow (↑↓→←)             │
      │  • Instruction text                                 │
      │  • Progress (N of M, progress bar)                  │
      │  • Reading entry field (NumberBox, mm/m)            │
      │  • "Calculating…" overlay when solver is running    │
      ↓                                                     │
[Reading accepted] ──────────────────────────────────────→─┘
      ↓ (all steps done — calculator runs on background thread)
[Results] ← ResultsArgs(project, session)
      │   Shows: flatness (µm), σ, flagged step list, 3D surface plot
      │   Commands: Save Project, New Measurement, Start Correction Session
      ↓ (if flagged steps exist)
[Correction: flagged step N of M]  ←──────────────────────┐
      │  Shows: original reading, orientation, instruction  │
      ↓                                                     │
[Reading accepted] ──────────────────────────────────────→─┘
      ↓ (all flagged steps re-measured — recalculates, stores CorrectionRound)
[Results] ← ResultsArgs(project, session)  [loop possible]
```



---



## 9. Persistence — JSON File Format



File extension: `.levelproj` (internally JSON, indented, camelCase property names). The complete field-by-field format reference is in [`docs/levelproj.md`](levelproj.md).

Serialisation is handled by `LevelApp.Core/Serialization/ProjectSerializer` (using `System.Text.Json`), `ObjectValueConverter` (preserves concrete types in `Dictionary<string, object>`), and `OrientationConverter` (see below). File I/O is handled by `LevelApp.App/Services/ProjectFileService` using Win32 `IFileOpenDialog` / `IFileSaveDialog` directly via COM (the WinRT picker wrappers were bypassed because they create their underlying COM dialog object lazily, making `SetFolder` calls ineffective for controlling the initial directory).

#### Orientation serialisation

`Orientation` values are always written as strings (`"North"`, `"South"`, `"East"`, `"West"`). On read, `OrientationConverter` accepts both the current string format and the legacy integer format (`0`=North, `1`=South, `2`=East, `3`=West) that was written by earlier builds, ensuring backwards compatibility.

#### Application settings

User preferences (default project folder and app theme) are persisted to `%LOCALAPPDATA%\LevelApp\settings.json` by `SettingsService`. This location is reliable for unpackaged Win32/WinUI 3 apps; `ApplicationData.Current.LocalFolder` was not used because it requires packaging infrastructure.

```json
{
  "schemaVersion": "1.1",
  "appVersion": "0.7.0",
  "project": {
    "id": "<uuid>",
    "name": "Granite plate workshop 3",
    "createdAt": "2026-04-04T09:00:00Z",
    "modifiedAt": "2026-04-04T14:23:00Z",
    "operator": "J. Müller",
    "notes": "After resurfacing",

    "objectDefinition": {
      "geometryModuleId": "SurfacePlate",
      "parameters": {
        "widthMm": 1200,
        "heightMm": 800,
        "columnsCount": 8,
        "rowsCount": 5
      }
    },

    "measurements": [
      {
        "id": "<uuid>",
        "label": "Measurement 1",
        "takenAt": "2026-04-04T10:15:00Z",
        "operator": "J. Müller",
        "instrumentId": "manual-entry",
        "strategyId": "FullGrid",
        "notes": "",

        "initialRound": {
          "completedAt": "2026-04-04T10:45:00Z",
          "steps": [
            {
              "index": 0,
              "gridCol": 0, "gridRow": 0,
              "orientation": "East",
              "instructionText": "Row pass — row 1, instrument at column 1 → 2, facing East",
              "reading": 0.012
            }
          ],
          "result": {
            "heightMapMm": [[0.0, 0.012], [0.008, 0.019]],
            "flatnessValueMm": 0.041,
            "residuals": [0.001, 0.002, 0.087],
            "flaggedStepIndices": [14, 31],
            "sigmaThreshold": 2.5,
            "sigma": 0.00034
          }
        },

        "corrections": [
          {
            "id": "<uuid>",
            "triggeredAt": "2026-04-04T11:02:00Z",
            "operator": "J. Müller",
            "notes": "Step 14 — instrument had not settled",
            "replacedSteps": [
              { "originalStepIndex": 14, "reading": 0.008 }
            ],
            "result": {
              "heightMapMm": [[...]],
              "flatnessValueMm": 0.038,
              "residuals": [...],
              "flaggedStepIndices": [],
              "sigmaThreshold": 2.5,
              "sigma": 0.00021
            }
          }
        ]
      }
    ]
  }
}
```



### Schema versioning

The `schemaVersion` field at the root allows the app to detect older files and apply migration logic before deserialising. `ProjectSerializer.Deserialize` throws `NotSupportedException` for unrecognised versions. Always increment when making breaking changes to the format.



---



## 10. Display Modules



Implements `IResultDisplay`. Each module receives a `SurfaceResult` and returns a renderable object (cast to `UIElement` by the caller).

| Module | Status | Notes |
|---|---|---|
| 3D Surface Plot | **Built** | Pseudo-3D isometric canvas; nodes coloured low→mid-low→mid→mid-high→high by height |
| Measurements Grid | **Built** | 2D step-map canvas with directed arrows, value labels, loop-closure colour fills, and mouse-wheel zoom |
| Strategy Preview | **Built** | Small read-only canvas in ProjectSetupView showing step layout for the selected strategy |
| Parallel Ways Display | **Built** | 2D rail schematic canvas showing station dots coloured by height on the themed ramp |
| Colour / Heat Map | Future | Intuitive flatness overview |
| Numerical Table | Future | Raw height values per grid point |
| Residuals Chart | Future | Useful for diagnosing bad readings |

Adding a new display: implement `IResultDisplay`, register it. The results page discovers available modules automatically.

#### Theme colour resolution in renderers

All display modules resolve colours via `ThemeHelper` (`LevelApp.App/Helpers/ThemeHelper.cs`) rather than hardcoding ARGB values. `ThemeHelper.GetColor` and `ThemeHelper.GetBrush` look up named keys from `ThemeColors.xaml` at render time, falling back to `Colors.Gray` if a key is missing. The five-stop plot ramp (`PlotRamp`) is resolved once per render pass with `ThemeHelper.GetPlotRamp`, then interpolated per-node with `ThemeHelper.InterpolateRamp` — avoiding repeated resource lookups in tight loops. Views subscribe to `ActualThemeChanged` and re-render the canvas so colour is always correct for the active theme.

#### 3D Surface Plot — z-scale

The vertical exaggeration (`maxZPixels`) is computed per render as `max(10, (colIntervals + rowIntervals) × IsoH × 0.20)` rather than being a fixed constant. This ensures that even on a very flat surface the z-displacement of any node can never exceed one isometric grid step (IsoH = 15 px), which would otherwise cause correct edges to appear crossed.



---



## 11. Key Design Decisions & Rationale



| Decision | Rationale |
|---|---|
| Single `ISurfaceCalculator` interface | Eliminates the old `IGeometryCalculator` / `ISurfaceCalculator` split; both calculators share one polymorphic contract |
| `StrategyFactory` / `CalculatorFactory` in Core | Centralise instantiation; adding a strategy or algorithm = one class + one line. Removes cross-VM static coupling |
| Measurement strategies as plugins | Full Grid and Union Jack share the same guided workflow infrastructure |
| ObjectDefinition.Parameters as flexible key-value | Different object types need very different parameters; avoids a rigid schema |
| Least-squares over simple integration | Distributes inconsistencies optimally across all steps; more robust for noisy readings |
| `MeasurementRound.MergeWithReplacements` static helper | Single definition for merging correction replacements into the original step list; eliminates three-way duplication and a subtle bug (missing NodeId/ToNodeId/PassId) |
| `ClosureErrorCalculator` shared helper | Deduplicates ~70 lines of closure computation between the two calculators |
| `IProjectFileService` interface | Decouples `MainViewModel` from the concrete file-service implementation; enables mocking in tests |
| Corrections as separate rounds, originals preserved | Full audit trail; operator can review the history of a session |
| JSON with schemaVersion | Human-readable, diffable, easily migrated as format evolves |
| .levelproj file extension | Clearly identifies the file type; internally standard JSON |
| MVVM pattern with CommunityToolkit.Mvvm | Source-generated boilerplate; Views are fully replaceable |
| INavigationService / PageKey | ViewModels trigger page transitions without compile-time dependency on View types |
| Microsoft.Extensions.DependencyInjection | Standard DI; services and ViewModels resolved from `App.Services` |
| Core project has zero UI dependencies | All models, interfaces, and algorithms are unit-testable without a UI |
| Win32 COM file dialogs instead of WinRT pickers | WinRT pickers create the underlying COM dialog lazily; `SetFolder` on the wrapper targets a discarded object. Driving `IFileOpenDialog`/`IFileSaveDialog` directly via `CoCreateInstance` gives reliable control over the initial folder |
| Settings in `%LOCALAPPDATA%\LevelApp\settings.json` | `ApplicationData.Current.LocalFolder` throws for unpackaged apps; `Environment.SpecialFolder.LocalApplicationData` works unconditionally |
| `OrientationConverter` reads string enum only | No users exist, so legacy integer format support was removed (WP0.06) |
| Theme colours in `ResourceDictionary.ThemeDictionaries` | All canvas renderers resolve colours at render time from named keys in `ThemeColors.xaml`; on theme change views subscribe to `ActualThemeChanged` and re-render, so the full colour ramp is always correct for the active theme |
| `ThemeHelper` in `LevelApp.App/Helpers/` | Single shared helper (`GetColor`, `GetBrush`, `PlotRamp`, `InterpolateRamp`) eliminates copy-pasted lookup methods across all four display modules and two views; the `PlotRamp` struct is resolved once per render pass to avoid repeated dictionary lookups inside node loops |
| `IThemeService` / `ThemeService` singleton | Decouples live theme switching from `MainWindow`; `PreferencesDialog` calls `_theme.Apply()` directly without holding a reference to the window; `ThemeService.SetTarget(RootFrame)` is called once on startup by `MainWindow` |
| Plot canvas rebuild on theme change | `ResultsViewModel.RebuildPlotCanvas()` reconstructs the `Canvas` from cached session/result data; `ResultsView.OnActualThemeChanged` swaps `PlotContainer.Content` — avoids storing mutable brushes on a live canvas |



---



## 12. Build Order / Roadmap



### Phase 1 — Core foundation (no UI) ✓ Complete
1. Core models (`Project`, `ObjectDefinition`, `MeasurementSession`, `MeasurementStep`, etc.)
2. Core interfaces (`IGeometryCalculator`, `IGeometryModule`, `IMeasurementStrategy`, `IInstrumentProvider`, `IResultDisplay`)
3. `FullGridStrategy` — generates ordered step list with orientations
4. `SurfacePlateCalculator` — least-squares solver + outlier detection
5. `ManualEntryProvider` — delegate pass-through
6. Unit tests for calculator and strategy (`LevelApp.Tests`)

### Phase 2 — WinUI 3 app shell ✓ Complete
7. Solution setup, WinUI 3 project, DI container, navigation framework (`INavigationService`, `NavigationService`, `PageKey`)
8. `ProjectSetupView` — parameter entry, step-count preview, open-project button
9. `MeasurementView` — guided step-by-step workflow with grid canvas
10. `ResultsView` with `SurfacePlot3DDisplay`

### Phase 3 — Persistence ✓ Complete
11. JSON serialisation / deserialisation (`ProjectSerializer`, `ObjectValueConverter`)
12. Save / load project file via `ProjectFileService` (native file-picker dialogs)

### Phase 4 — Corrections workflow ✓ Complete
13. Flagged step review UI in `ResultsView`
14. `CorrectionView` — guided correction session
15. `CorrectionRound` storage and recalculation

### WP0.02 — Versioning & About ✓ Complete (v0.2.1)
- `AppVersion.cs` — single source of truth for `Major.Minor.Patch`
- `appVersion` field written to `.levelproj` root at save time
- `Help > About LevelApp...` menu item and `AboutDialog`
- Assembly/file version metadata in `.csproj`
- Commit message convention: `[vX.Y.Z] description`

### WP0.08 — Theme architecture ✓ Complete (v0.8.1)
- `LevelApp.App/Styles/` folder with three `ResourceDictionary` XAML files merged in `App.xaml`:
  - `ThemeColors.xaml` — all colour tokens in `ThemeDictionaries` (`Light` + `Default`/Dark); covers plot ramp (5 stops), grid canvas colours, loop-closure brushes
  - `TextStyles.xaml` — named `TextBlock` styles (`PageTitleStyle`, `SectionHeaderStyle`, `MetricValueStyle`, etc.) using `{ThemeResource}` tokens
  - `ControlStyles.xaml` — implicit `Button` style; `CardStyle`; `CompactCardStyle`
- `ISettingsService.AppTheme` (`ElementTheme`) persisted as string in `settings.json`
- `MainWindow`: `ApplyPersistedTheme()` on startup; `ApplyTheme(ElementTheme)` public method; menu bar with `File`, `Edit` (→ Preferences…), `Help` (→ About LevelApp…)
- `PreferencesDialog`: Theme `RadioButtons` (Follow system / Light / Dark); live preview on selection change; reverts to original on Cancel
- All four canvas renderers (`SurfacePlot3DDisplay`, `MeasurementsGridRenderer`, `StrategyPreviewRenderer`, `ParallelWaysDisplay`) resolve colours from theme resources at render time via `Application.Current.Resources.TryGetValue()`
- All four views subscribe to `ActualThemeChanged` for live theme-switch re-render; `ResultsViewModel.RebuildPlotCanvas()` reconstructs plot canvases with updated colours
- No hardcoded colours, font sizes, or font weights in any View XAML

### WP0.07 — Parallel Ways geometry module ✓ Complete (v0.7.0)
- New Core models: `RailDefinition`, `ParallelWaysParameters`, `ParallelWaysTask`, `ParallelWaysStrategyParameters`, `ParallelWaysResult` (with `RailProfile` and `ParallelismProfile`)
- `PassPhase` enum added to `MeasurementStep`; `ParallelWaysResult` field added to `MeasurementRound`
- `ParallelWaysStrategy` implements `IMeasurementStrategy`: generates AlongRail and Bridge steps, node IDs `"rail{r}_sta{s}"`, forward/return passes, East/West orientation
- `ParallelWaysCalculator` (standalone, not `ISurfaceCalculator`): two solver modes (GlobalLeastSquares, IndependentThenReconcile), per-rail height profiles with best-fit line removal, parallelism between rail pairs
- `ParallelWaysDisplay` renders the 2D rail schematic in the Measurement view
- `ProjectSetupViewModel` / `ProjectSetupView.xaml` rewritten to support Parallel Ways parameter editor (rails, tasks, orientation, drift correction, solver mode)
- `MeasurementViewModel` dispatches to `ParallelWaysCalculator` for Parallel Ways sessions
- `ResultsViewModel` / `ResultsView.xaml` show per-rail straightness and per-pair parallelism results
- Schema bumped to `"1.1"` (adds `passPhase` on steps and `parallelWaysResult` on rounds)
- Unit tests: `ParallelWaysStrategyTests` (14 tests), `ParallelWaysCalculatorTests` (9 tests)

### WP0.06 — Code Quality Cleanup ✓ Complete (v0.6.0)
- Unified calculator interface: deleted `IGeometryCalculator`, consolidated on `ISurfaceCalculator`
- `LeastSquaresCalculator` (renamed from `SurfacePlateCalculator`) moved to `Core/Geometry/Calculators/`; now respects `CalculationParameters.AutoExcludeOutliers`
- `SequentialIntegrationCalculator` moved to `Core/Geometry/Calculators/`
- `ClosureErrorCalculator` extracted as shared helper for both calculators
- `StrategyFactory` and `CalculatorFactory` in `Core/Geometry/` replace scattered direct instantiation and cross-VM static calls
- `MeasurementRound.MergeWithReplacements` eliminates three-way duplication and fixes a subtle bug in `MainViewModel`
- `IProjectFileService` interface extracted; `MainViewModel` depends on it
- `CorrectionViewModel` parallel arrays (`_flaggedSteps` + `_newReadings`) replaced with a single `List<(MeasurementStep, double?)>`
- Deleted dead code: `IGeometryCalculator`, `IGeometryModule`, `IResultDisplay`, `IInstrumentProvider`, `ManualEntryProvider`, legacy `Render(SurfaceResult)` overload
- Removed backward-compatibility code (integer `Orientation` JSON, numeric `rings` param)
- `RecalculateMissingResultsAsync` in `MainViewModel` was temporarily removed during the cleanup but was restored in v0.6.2 — it is still needed to recompute results for files that were saved before result serialisation was enforced

### Future phases
- Additional display modules (heat map, numerical table, residuals chart)
- Parallel Ways: correction workflow (currently Surface Plate only)
- Bluetooth LE instrument provider
- USB HID instrument provider
- Additional geometry modules (straightness, squareness, etc.)
- Reporting / PDF export



---



## 13. Versioning Convention



`LevelApp.Core/AppVersion.cs` is the single source of truth for the version number. **Never hardcode a version string anywhere else** — not in XAML, not in C#, not in comments.



```csharp
public static class AppVersion
{
    public const int Major = 0;
    public const int Minor = 8;
    public const int Patch = 1;

    public static string Full    => $"{Major}.{Minor}.{Patch}";
    public static string Display => $"v{Full}";
}
```



`LevelApp.App/LevelApp.App.csproj` carries `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` kept in sync manually with `AppVersion.cs`.



Every saved `.levelproj` file records the writing app version as `"appVersion"` at the root level (informational only — not validated on load).



### Commit message format

```
[vX.Y.Z] Short imperative description
```

Examples:
```
[v0.2.1] WP0.02: versioning, appVersion in project file, About dialog
[v0.3.0] WP0.03: ...
```

Bump `AppVersion.cs` **before** committing so the delivered commit already carries the correct version.



---



## 14. Open Questions



- Should multiple instrument providers be selectable per measurement session (e.g. two axes simultaneously)?
- Reporting: what format? PDF export? Print directly?
- Localisation: German / English from the start, or English only initially?
- Licensing / distribution model for the application?
- Should the 3D surface plot be interactive (rotate, zoom)?



---



## 15. Model Switching Notes



When starting a new session with an AI assistant, paste this document as context. A concise session-start prompt:



> 'I'm building LevelApp — a C# WinUI 3 Windows app for precision level measurement evaluation. The architecture document is below. New features are implemented from work package files in docs/workpackages/. Please read both before starting any work.'


