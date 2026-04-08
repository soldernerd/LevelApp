# Work Package WP-0.06 — Code Quality Cleanup

> LevelApp | C# WinUI 3 | Windows App SDK / .NET 8/9
> Depends on: WP-0.05 being complete

---

## Overview

Internal refactoring pass to improve modularity, remove dead code, eliminate duplication, and clean up the calculator architecture. **No changes to functionality or user-facing behavior.** The UI, measurement workflow, and `.levelproj` file format remain identical.

Since no one has used the app yet, all backward compatibility code for legacy `.levelproj` formats is removed.

---

## H1 — Modular calculator architecture

### Problem

The codebase has three independent extension axes — **object types** (surface plate; future: straightedge, cylindricity), **measurement strategies** (Full Grid, Union Jack), and **calculation algorithms** (Least Squares, Sequential Integration) — but the current code conflates them:

1. Two incompatible calculator interfaces with different signatures:
   - `IGeometryCalculator.Calculate(MeasurementRound)` — used by `SurfacePlateCalculator`
   - `ISurfaceCalculator.Calculate(IReadOnlyList<MeasurementStep>, ObjectDefinition, CalculationParameters)` — used by `SequentialIntegrationCalculator`
2. Algorithm selection is a hard-coded `if` string check in `ResultsViewModel.RecalculateAsync()`
3. `SurfacePlateCalculator` ignores `CalculationParameters.AutoExcludeOutliers` while `SequentialIntegrationCalculator` respects it
4. Both calculators live under `Geometry/SurfacePlate/` despite being general algorithms

### Changes

1. **Delete `IGeometryCalculator`**. Consolidate on `ISurfaceCalculator` as the single calculator interface.

2. **Rename `SurfacePlateCalculator` to `LeastSquaresCalculator`:**
   - Implement `ISurfaceCalculator` (the richer signature)
   - Accept and respect `CalculationParameters` (including `AutoExcludeOutliers`)
   - Move from `Core/Geometry/SurfacePlate/` to `Core/Geometry/Calculators/`

3. **Move `SequentialIntegrationCalculator`** to `Core/Geometry/Calculators/` (already implements `ISurfaceCalculator`)

4. **Simplify all ViewModel call sites** to use the polymorphic `ISurfaceCalculator` interface — no more conditional branching on `MethodId`.

### Files

- `Core/Interfaces/IGeometryCalculator.cs` — delete
- `Core/Interfaces/ISurfaceCalculator.cs` — keep (single calculator interface)
- `Core/Geometry/SurfacePlate/SurfacePlateCalculator.cs` — rename to `LeastSquaresCalculator`, move to `Core/Geometry/Calculators/`, implement `ISurfaceCalculator`
- `Core/Geometry/SurfacePlate/SequentialIntegrationCalculator.cs` — move to `Core/Geometry/Calculators/`
- `App/ViewModels/ResultsViewModel.cs` — simplify `RecalculateAsync`
- `App/ViewModels/MeasurementViewModel.cs` — update calculator call
- `App/ViewModels/CorrectionViewModel.cs` — update calculator call
- `App/ViewModels/MainViewModel.cs` — update `RecalculateSessionAsync`
- `Tests/SurfacePlateCalculatorTests.cs` — update to new class name and signature

---

## H2 — Extract duplicated merged-step construction

### Problem

The logic to merge initial measurement steps with correction replacements is copy-pasted in 3 places:

- `ResultsViewModel.GetMergedSteps()` — full copy including NodeId/ToNodeId/PassId
- `CorrectionViewModel.AcceptReadingAsync()` — full copy
- `MainViewModel.RecalculateSessionAsync()` — **incomplete copy** missing NodeId, ToNodeId, PassId (subtle bug)

### Changes

Extract to a static helper in Core: `MeasurementRound.MergeWithReplacements(IReadOnlyList<MeasurementStep> originalSteps, IEnumerable<ReplacedStep> replacements)`. Call from all 3 sites. This also fixes the MainViewModel bug.

### Files

- `Core/Models/MeasurementRound.cs` — add `MergeWithReplacements` method
- `App/ViewModels/ResultsViewModel.cs` — replace `GetMergedSteps()`
- `App/ViewModels/CorrectionViewModel.cs` — replace inline merge
- `App/ViewModels/MainViewModel.cs` — replace inline merge

---

## H3 — Strategy and calculator factories in Core

### Problem

`ResultsViewModel.CreateStrategy()` is a `static` method called from 4 other ViewModels, creating tight cross-VM coupling. Calculator instantiation is similarly scattered.

### Changes

Create two static factory classes in `Core/Geometry/`:

1. **`StrategyFactory.Create(string strategyId)`** — returns `IMeasurementStrategy`
2. **`CalculatorFactory.Create(string methodId, IMeasurementStrategy strategy)`** — returns `ISurfaceCalculator`

Adding a new strategy or algorithm = one new class + one line in the corresponding factory.

### Files

- New: `Core/Geometry/StrategyFactory.cs`
- New: `Core/Geometry/CalculatorFactory.cs`
- `App/ViewModels/ResultsViewModel.cs` — remove `CreateStrategy()`, use factories
- `App/ViewModels/MeasurementViewModel.cs` — use factories
- `App/ViewModels/CorrectionViewModel.cs` — use factories
- `App/ViewModels/MainViewModel.cs` — use factories
- `App/ViewModels/ProjectSetupViewModel.cs` — use `StrategyFactory`

---

## M1 — Remove backward compatibility code

No users exist, so all legacy format support can be removed:

| Location | What to remove |
|---|---|
| `Core/Serialization/OrientationConverter.cs` lines 37-47 | Integer format branch (`JsonTokenType.Number`) — only keep string format |
| `UnionJackStrategy.ParseRingsOption()` line 256 | Legacy `Convert.ToInt32(value)` numeric branch — only accept string enum |
| `MainViewModel.RecalculateMissingResultsAsync()` | Entire method + call in `OpenProjectAsync()` — existed for pre-result-serialization files |
| `SurfacePlateCalculator` constructor line 37 | Convenience constructor "preserves backward compatibility" — delete |

---

## M2 — Remove unused interfaces and dead code

| Item | Rationale |
|---|---|
| `Core/Interfaces/IGeometryModule.cs` | Never implemented. Replaced by `StrategyFactory` + `CalculatorFactory` from H3 |
| `Core/Interfaces/IResultDisplay.cs` | Vestigial — `Render(SurfaceResult)` returns empty Canvas. Real render takes 4 params. Display modules belong in App |
| `Core/Instruments/ManualEntry/ManualEntryProvider.cs` | Never instantiated or registered. VMs handle manual entry directly. Re-create when instrument connectivity is built |
| `SurfacePlot3DDisplay.Render(SurfaceResult)` legacy overload | Returns empty Canvas. Delete with `IResultDisplay` |
| `Core/Interfaces/IInstrumentProvider.cs` | Review: keep only if instrument connectivity is imminent |

---

## M3 — Extract duplicated closure error computation

### Problem

Nearly identical closure error code (~70 lines) appears in both `SurfacePlateCalculator` (lines 136-204) and `SequentialIntegrationCalculator` (lines 171-229): building the stepFwd dictionary, iterating loops, computing statistics.

### Changes

Extract to `Core/Geometry/Calculators/ClosureErrorCalculator.cs` — a static helper both calculators call.

### Files

- New: `Core/Geometry/Calculators/ClosureErrorCalculator.cs`
- `Core/Geometry/Calculators/LeastSquaresCalculator.cs` — delegate to helper
- `Core/Geometry/Calculators/SequentialIntegrationCalculator.cs` — delegate to helper

---

## M4 — Add IProjectFileService interface

`ProjectFileService` is registered as a concrete type — can't be mocked for testing.

### Changes

Extract `IProjectFileService` interface. Register via interface in DI. `MainViewModel` depends on the interface.

### Files

- New: `App/Services/IProjectFileService.cs`
- `App/Services/ProjectFileService.cs` — implement interface
- `App/App.xaml.cs` — register as `services.AddSingleton<IProjectFileService, ProjectFileService>()`
- `App/ViewModels/MainViewModel.cs` — depend on interface

---

## L1 — Replace parallel arrays in CorrectionViewModel

Replace `_flaggedSteps` (List) + `_newReadings` (double[]) kept in sync by index with a single collection of `(MeasurementStep Step, double? NewReading)`.

### Files

- `App/ViewModels/CorrectionViewModel.cs`

---

## Target directory structure after refactoring

```
Core/Geometry/
├── StrategyFactory.cs                    (new — H3)
├── CalculatorFactory.cs                  (new — H3)
├── Calculators/
│   ├── LeastSquaresCalculator.cs         (renamed+moved from SurfacePlateCalculator — H1)
│   ├── SequentialIntegrationCalculator.cs (moved — H1)
│   └── ClosureErrorCalculator.cs         (extracted — M3)
└── SurfacePlate/
    └── Strategies/
        ├── FullGridStrategy.cs
        ├── UnionJackStrategy.cs
        └── UnionJackRings.cs
```

---

## Implementation order

1. M1 — remove backward compat (simplest, reduces noise)
2. H3 — strategy + calculator factories (establishes extensibility pattern)
3. H1 — unify calculator interface, rename + move calculators (biggest change, builds on H3)
4. H2 — extract merged-step helper (fixes a subtle bug)
5. M3 — extract closure error computation (natural follow-on from H1)
6. M2 — remove dead code (cleanup after interface changes)
7. M4 — IProjectFileService (independent)
8. L1 — parallel arrays (small cleanup)

---

## Constraints

- **No user-facing changes.** UI, measurement workflow, file format, and all observable behavior remain identical.
- **All existing tests must pass** after updating call sites.
- Verify with sample projects in `docs/sampleProjects/`.

---

## Verification

1. `dotnet build` — solution compiles without errors or warnings
2. `dotnet test` — all existing tests pass (update call sites as needed)
3. Load a sample `.levelproj` from `docs/sampleProjects/` — verify it opens and displays correctly
4. Create a new Full Grid project, run measurement, check results
5. Create a new Union Jack project, run measurement, check results
6. Run a correction workflow — verify corrections apply correctly
7. Recalculate with both Least Squares and Sequential Integration — verify both work
8. Save and reload — verify round-trip fidelity
