# Codebase Concerns

**Analysis Date:** 2026-06-02

---

## Acknowledged Technical Debt

These items are documented in `CLAUDE.md` and `docs/architecture.md` (section 14). They are known and partially mitigated. **Do not extend these patterns.**

### 1. `App.Services` Static Service Locator

**Severity:** Medium — architectural smell, not a runtime bug.

**Status:** Partially addressed in WP0.19. Remaining call sites are justified.

**Call sites (all carry explanatory comments):**
- `LevelApp.App\MainWindow.xaml.cs:31–38` — 8 lookups in the constructor; `MainWindow` is created with `new MainWindow()` in `App.OnLaunched`, not resolved from DI, making constructor injection genuinely unavailable. Comment present.
- `LevelApp.App\Views\ProjectSetupView.xaml.cs:18` — `Frame.Navigate` creates the page; comment present.
- `LevelApp.App\Views\MeasurementView.xaml.cs:38` — same reason; comment present.
- `LevelApp.App\Views\ResultsView.xaml.cs:23` — same reason; comment present.
- `LevelApp.App\Views\CorrectionView.xaml.cs:29` — same reason; comment present.
- `LevelApp.App\Views\InstrumentsPage.xaml.cs:16` — same reason; comment present.

**Impact:** All remaining call sites are either the composition root (`MainWindow`) or `Frame.Navigate`-created pages. Neither category can use constructor injection in WinUI 3 without a custom navigation factory. Current state is stable.

**Rule:** Never add a new `App.Services.GetRequiredService<>()` call without the composition-root or Frame-Navigate comment. Any new page must follow the same pattern as existing pages.

---

### 2. No Shared Rendering Contract Across Display Modules

**Severity:** Medium — blocks adding a fifth renderer cleanly.

**Status:** Active, unresolved.

**Evidence:**
- `LevelApp.App\DisplayModules\SurfacePlot3D\SurfacePlot3DDisplay.cs:21` — `public sealed class SurfacePlot3DDisplay` (no interface)
- `LevelApp.App\DisplayModules\MeasurementsGrid\MeasurementsGridRenderer.cs:27` — `public static class MeasurementsGridRenderer` (no interface)
- `LevelApp.App\DisplayModules\StrategyPreview\StrategyPreviewRenderer.cs:18` — `public static class StrategyPreviewRenderer` (no interface)
- `LevelApp.App\DisplayModules\ParallelWaysDisplay\ParallelWaysDisplay.cs:18` — `public sealed class ParallelWaysDisplay` (no interface)

Note: the four modules are inconsistent in their type modifiers — two are `sealed class`, two are `static class`. This makes polymorphic dispatch impossible even if an interface were extracted today.

**Impact:** Adding a new renderer (heat map, numerical table, residuals chart — all listed in `docs/architecture.md` section 10 as "Future") requires editing the calling view's code-behind each time and duplicating the calling convention.

**Rule:** Do not add a fifth static renderer module. When a new renderer is needed, define `IDisplayModule` first and migrate the four existing modules to it before adding the fifth.

---

### 3. `HttpClient` Timeout — Pattern Risk

**Severity:** Low — resolved for the one existing `HttpClient` instance.

**Status:** Resolved in WP0.19 for `UpdateService`. No other `HttpClient` instances in the codebase.

**Evidence:** `LevelApp.App\Services\UpdateService.cs:16` — `Timeout = TimeSpan.FromSeconds(10)` present.

**Residual risk:** If a future work package adds a second `HttpClient` (e.g. for telemetry or a hardware firmware download endpoint), the resolved debt will re-emerge if the rule is not followed.

**Rule:** Always set an explicit `Timeout` on any new `HttpClient` instance. Do not use the default (infinite).

---

## Observed Additional Concerns

### 4. Activity Logger — Incomplete `Cmd.*` and `Input.Changed` Wiring

**Severity:** Medium — replay testing is structurally broken until this is addressed.

**Evidence:**
- `LevelApp.Tests\Replay\ActivityReplayRunner.cs:102–151` — every `Cmd.*` and `Input.Changed` case is a stub `// TODO` that does nothing.
- `LevelApp.Tests\Replay\IReplayTarget.cs:6–14` — `IReplayTarget` has zero members; it is a placeholder.
- `LevelApp.App\ViewModels\MeasurementViewModel.cs`, `ResultsViewModel.cs`, `CorrectionViewModel.cs`, `ProjectSetupViewModel.cs` — none log `Cmd.*` or `Input.Changed` entries to `IActivityLogger`. Only `MainViewModel` logs `File.*` operations (`LevelApp.App\ViewModels\MainViewModel.cs:88–315`).

**Impact:** `ReplayTests` passes vacuously — it runs over `TestLogs/*.jsonl` (currently empty) and would do nothing useful even with real session bundles because all Cmd and Input actions are no-ops in the runner. Activity logging for WP0.10 is declared complete but the replay half of the feature is a stub.

**Fix approach:**
1. Add `IActivityLogger` injection to `MeasurementViewModel`, `ResultsViewModel`, `CorrectionViewModel`.
2. Log `Cmd.StartMeasurement`, `Cmd.StopMeasurement`, `Cmd.RecalculateFlatness`, `Cmd.ApplyCorrection`, and `Input.Changed` events at each ViewModel call site.
3. Wire `ActivityReplayRunner` dispatch cases to real ViewModel methods, replacing the stubs.
4. Replace `IReplayTarget` with `MainViewModel` once the test project targets `net8.0-windows10.0.19041.0`.

---

### 5. `MeasurementViewModel` Never Calls `ConnectAsync` / `DisconnectAsync`

**Severity:** High for hardware plugins — blocks all future hardware instrument work packages.

**Evidence:** `LevelApp.App\ViewModels\MeasurementViewModel.cs:59–102` — `Initialize()` calls `plugin.CreateProvider(device)` and wires `ConnectionStateChanged`, but never calls `_provider.ConnectAsync()`. The `IInstrumentProvider` interface requires explicit `ConnectAsync()` / `DisconnectAsync()` calls.

**Current workaround:** `ManualEntryProvider` starts in `Connected` state and needs no connect call, so the app appears correct today.

**Impact:** When the first BLE or USB HID instrument plugin is registered, the instrument will never leave `Disconnected` state because `ConnectAsync()` is never called. The `ShowConnectionWarning` InfoBar will be permanently visible and `CanAcceptReading()` (`LevelApp.App\ViewModels\MeasurementViewModel.cs:244–250`) will always return `false`, blocking measurement entirely.

**Fix approach:** Add `await _provider.ConnectAsync()` in `MeasurementViewModel.Initialize()` (or in `MeasurementView.OnNavigatedTo`) and `await _provider.DisconnectAsync()` in `OnNavigatedFrom`. This is a one-time, low-effort change that unblocks all hardware instrument WPs.

---

### 6. `MeasurementViewModel` Has a Direct Reference to `ManualEntryProvider`

**Severity:** Low — minor layer violation.

**Evidence:**
- `LevelApp.App\ViewModels\MeasurementViewModel.cs:10` — `using LevelApp.Instruments.Manual;`
- `LevelApp.App\ViewModels\MeasurementViewModel.cs:71` — `?? ManualEntryProvider.BuiltInDevice` as a hardcoded fallback device

**Impact:** `MeasurementViewModel` bypasses the plugin/registry abstraction for its fallback. If the Manual plugin is restructured or renamed, this line silently falls through to the wrong device. It also couples the ViewModel directly to a concrete plugin project, violating the "depend on interfaces" coding standard.

**Fix approach:** Move the built-in device constant to `ManualEntryPlugin` (already accessible from `App.xaml.cs:80`) and expose the fallback through the registry or through a plugin capability, removing the direct `LevelApp.Instruments.Manual` reference from the ViewModel.

---

### 7. Parallel Ways Correction Workflow Is Missing

**Severity:** Medium — functional gap documented in roadmap.

**Evidence:**
- `docs/architecture.md:1162` — "Parallel Ways: correction workflow (currently Surface Plate only)"
- `README.md:146` — "[ ] Parallel Ways correction workflow"
- `docs/workpackages/wp_0.07_ParallelWays.md:441` — explicitly deferred to a later WP.

**Impact:** After a Parallel Ways measurement session, flagged steps cannot be re-measured. There is no guided correction path for Parallel Ways sessions. Operators must start a new full session to address outliers.

**Fix approach:** Wire `CorrectionViewModel` and `CorrectionView` to accept `ParallelWaysResult` flagged steps. `ParallelWaysCalculator.Calculate` already returns `FlaggedStepIndices`. The correction round infrastructure (`CorrectionRound`, `MergeWithReplacements`) is geometry-agnostic and should work for Parallel Ways sessions with minor adaptation.

---

### 8. `UpdaterContract` Duplicated in Two Projects

**Severity:** Low — acknowledged, documented, and stable.

**Evidence:**
- `LevelApp.App\Services\UpdaterContract.cs`
- `LevelApp.Updater\UpdaterContract.cs`

Both files carry a sync comment. The duplication exists because `LevelApp.Updater` targets `net8.0` (no Windows TFM) and cannot reference `LevelApp.App`.

**Impact:** If the argument contract changes (position of `zipPath`, `installFolder`, or `mainExeName`, or a new flag), both files must be updated in the same commit or the updater will mis-parse arguments silently. The log at `%TEMP%\LevelApp.Updater.log` is the only diagnostic.

**Fix approach:** Add a unit test that asserts the constant values in both files are identical. This catches drift at CI without removing the structural constraint.

---

### 9. `UpdateDialog` Constructs Arguments via Raw String Interpolation

**Severity:** Low — partial use of the `UpdaterContract` abstraction.

**Evidence:** `LevelApp.App\Views\Dialogs\UpdateDialog.xaml.cs:51–55` — constructs the `Process.Start` argument string as a raw interpolated string rather than using `UpdaterContract` positional constants. The `mainExeName` value `"LevelApp.App.exe"` is also hardcoded in the interpolation.

**Impact:** If argument positions change in `UpdaterContract`, `UpdateDialog` is not updated automatically — the interpolated string must be manually reviewed. The hardcoded exe name is a secondary concern.

**Fix approach:** Add a `UpdaterContract.MainExeName` constant and build the argument string using the positional constants, or switch to `ProcessStartInfo.ArgumentList` (one element per argument, no quoting needed).

---

### 10. No Elevation / Write-Permission Handling in the Updater

**Severity:** Low — documented known limitation.

**Evidence:** `docs/architecture.md:1101` — "Known limitation: update fails if the app is installed in a write-protected directory (e.g. Program Files)." `LevelApp.Updater\Program.cs:79` — `ZipFile.ExtractToDirectory` throws `UnauthorizedAccessException` on protected paths.

**Impact:** Users who install to `Program Files` cannot auto-update. The FATAL log entry is written but the app is left un-updated with no rollback.

**Fix approach:** Before extraction, test write access to `installFolder`. If denied, relaunch the updater elevated via `ShellExecuteEx` with `runas`, or surface a user-readable error with instructions to move the install location.

---

### 11. Inline Canvas Rendering in `MeasurementView` and `CorrectionView` Code-Behind

**Severity:** Low — technical smell, not a bug.

**Evidence:**
- `LevelApp.App\Views\MeasurementView.xaml.cs:84–290` — `DrawGridMap()`, `DrawParallelWaysMap()`, `DrawUnionJackMap()` implemented inline (~200 lines of rendering code in code-behind)
- `LevelApp.App\Views\CorrectionView.xaml.cs:78–160` — `DrawCorrectionMap()` implemented inline (~80 lines)

These duplicate colour-resolution and geometry patterns from the four `DisplayModules\` classes and are not factored into that module system.

**Impact:** A fifth (and sixth) rendering concern already exists as a static pattern in code-behind rather than `DisplayModules\`. When `IDisplayModule` is defined, these will also need migration.

**Fix approach:** Extract both inline renderers into `DisplayModules\MeasurementGridLive\` and `DisplayModules\CorrectionGridLive\` when the `IDisplayModule` interface is introduced. Low urgency until then.

---

## Risk Register

| # | Concern | Blocks | Probability | Impact |
|---|---------|--------|-------------|--------|
| 5 | `ConnectAsync` never called in `MeasurementViewModel` | First hardware plugin is permanently `Disconnected`; measurement blocked | Certain on first hardware WP | High |
| 7 | Parallel Ways correction workflow missing | Operators cannot re-measure Parallel Ways outliers | Already deferred; affects current users | Medium |
| 4 | Replay runner is entirely stubs | Crash replay and regression testing non-functional | Current; grows with every new session type | Medium |
| 2 | No `IDisplayModule` interface | Fifth renderer introduces new inconsistency; no polymorphic dispatch | Any future display WP | Medium |
| 6 | `ManualEntryProvider` hardcoded fallback in ViewModel | Wrong provider used silently after plugin restructure | Plugin restructure WP | Low |
| 8 | Duplicate `UpdaterContract` | Argument-position drift causes silent updater failures | Any updater change WP | Low |
| 9 | `UpdateDialog` raw argument string | Positional contract changes not caught at compile time | Any updater change WP | Low |
| 10 | No elevation in updater | `Program Files` installs cannot auto-update | User-facing; already documented | Low |
| 11 | Inline canvas rendering in views | Migration cost grows; `IDisplayModule` WP harder | Future display WP | Low |
| 3 | `HttpClient` timeout pattern | New HTTP services block on slow networks | Future networking WP | Low |

---

## Recommendations (ordered by impact/effort ratio)

1. **Fix `ConnectAsync` / `DisconnectAsync` lifecycle in `MeasurementViewModel`** (`LevelApp.App\ViewModels\MeasurementViewModel.cs:59–102`). One-time, low-effort change that unblocks all hardware instrument work packages. Do this before any hardware plugin WP begins.

2. **Wire `Cmd.*` and `Input.Changed` activity logging** across all page ViewModels. Medium effort; unblocks replay testing and crash reproduction, which benefits all future WPs.

3. **Implement Parallel Ways correction workflow.** Already scoped in the roadmap. The infrastructure (`CorrectionRound`, `MergeWithReplacements`, `FlaggedStepIndices` on `ParallelWaysResult`) is in place — only ViewModel/View wiring is missing.

4. **Define `IDisplayModule` and migrate display modules** when the first new renderer is needed. Do not add a fifth static renderer before the interface exists. Include the two inline code-behind renderers in that migration.

5. **Add a `UpdaterContract` drift test** to `LevelApp.Tests`. Low effort; prevents the duplicate-file pattern from causing silent runtime argument mis-parsing.

6. **Remove `MeasurementViewModel`'s direct reference to `ManualEntryProvider`.** Replace with a registry-mediated fallback to eliminate the cross-layer coupling.

---

*Concerns audit: 2026-06-02*
