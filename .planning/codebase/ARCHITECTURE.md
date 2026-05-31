<!-- refreshed: 2026-05-31 -->
# Architecture

**Analysis Date:** 2026-05-31

## System Overview

```text
┌──────────────────────────────────────────────────────────────────────┐
│                      LevelApp.App (WinUI 3)                          │
│  Views/         ViewModels/       Services/        DisplayModules/   │
│  *.xaml         *ViewModel.cs     *Service.cs       *Renderer.cs     │
└────────┬─────────────┬────────────────┬──────────────────┬──────────┘
         │             │                │                  │
         ▼             ▼                ▼                  │
┌──────────────────────────────────────────────────────┐  │
│                   LevelApp.Core                      │  │
│  Interfaces/   Models/   Geometry/   Serialization/  │◄─┘
│  I*.cs         *.cs      *.cs        *.cs            │
└──────────────────────┬───────────────────────────────┘
                       │ (interfaces only)
         ┌─────────────┼──────────────────┐
         ▼             ▼                  ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────────┐
│ Instruments  │ │ Instruments  │ │   Instruments    │
│ .Manual      │ │ .BLE         │ │   .UsbHid        │
│ (IInstrument │ │ (BLE infra + │ │ (USB HID infra + │
│  Plugin)     │ │  reconnect)  │ │  STM32 DFU)      │
└──────────────┘ └──────────────┘ └──────────────────┘
```

## Component Responsibilities

| Component | Responsibility | Location |
|-----------|----------------|----------|
| `LevelApp.Core` | Domain models, interfaces, geometry algorithms, serialization. Zero UI deps. | `LevelApp.Core/` |
| `LevelApp.App` | WinUI 3 shell, MVVM, services, navigation, display modules | `LevelApp.App/` |
| `LevelApp.Instruments.Manual` | Manual-entry instrument plugin (no hardware) | `LevelApp.Instruments.Manual/` |
| `LevelApp.Instruments.BLE` | BLE transport infrastructure, abstract provider base | `LevelApp.Instruments.BLE/` |
| `LevelApp.Instruments.UsbHid` | USB HID transport infrastructure + STM32 DFU subsystem | `LevelApp.Instruments.UsbHid/` |
| `LevelApp.Tests` | xUnit tests for Core algorithms and instrument infrastructure | `LevelApp.Tests/` |
| `LevelApp.Updater` | Standalone copy-to-temp updater, extracts zip and relaunches app | `LevelApp.Updater/` |

## Pattern Overview

**Overall:** Layered MVVM with interface-based dependency injection

**Key Characteristics:**
- `LevelApp.Core` has zero UI dependencies — fully unit-testable
- All services and ViewModels are resolved from `App.Services` (Microsoft.Extensions.DependencyInjection)
- ViewModels never reference View types; navigation uses `PageKey` enum + typed nav-args records
- Instrument plugins are registered via `IEnumerable<IInstrumentPlugin>` DI; app resolves all at runtime
- CommunityToolkit.Mvvm 8.3.2 provides source-generated `ObservableProperty`, `RelayCommand`, `ObservableObject`

## Layers

**Core (Domain):**
- Purpose: Domain models, calculation interfaces and implementations, serialization
- Location: `LevelApp.Core/`
- Contains: `Models/`, `Interfaces/`, `Geometry/`, `Serialization/`, `Instruments/` (enums/value types), `AppVersion.cs`
- Depends on: Nothing (no NuGet except System.Text.Json; no UI)
- Used by: All other projects

**Application (UI + Services):**
- Purpose: WinUI 3 shell, MVVM, page navigation, file I/O, settings, theming, activity logging
- Location: `LevelApp.App/`
- Contains: `Views/`, `ViewModels/`, `Services/`, `Navigation/`, `DisplayModules/`, `Helpers/`, `Styles/`, `Strings/`, `Converters/`
- Depends on: `LevelApp.Core`, `LevelApp.Instruments.Manual`
- Used by: Nothing (entry point)

**Instrument Plugins:**
- Purpose: Hardware transport implementations, each exposing `IInstrumentPlugin`
- Locations: `LevelApp.Instruments.Manual/`, `LevelApp.Instruments.BLE/`, `LevelApp.Instruments.UsbHid/`
- Depends on: `LevelApp.Core` interfaces only
- Used by: `LevelApp.App` (registered in DI container in `App.xaml.cs`)

## Data Flow

### Primary Measurement Path

1. User configures project in `ProjectSetupView` → `ProjectSetupViewModel` builds `Project` + `ObjectDefinition`, calls `MainViewModel` to store it.
2. Navigate to `MeasurementView` via `INavigationService.NavigateTo(PageKey.Measurement, MeasurementArgs)`.
3. `MeasurementViewModel.Initialize(MeasurementArgs)` resolves `IInstrumentPlugin` + `IDeviceRegistry` to obtain an `IInstrumentProvider`, calls `ConnectAsync()`.
4. `IMeasurementStrategy.GenerateSteps(ObjectDefinition)` produces the ordered step list (via `StrategyFactory.Create`).
5. For each step, `IInstrumentProvider.GetReadingAsync(step, ct)` returns a `double` (mm/m reading). Step is stored as `MeasurementStep.Reading`.
6. When all steps complete, `CalculatorFactory.Create(methodId, strategy).Calculate(steps, definition, parameters)` runs on a background thread → `SurfaceResult`.
7. For Parallel Ways sessions, `ParallelWaysCalculator.Calculate(...)` is called directly (not via `ISurfaceCalculator`) → `ParallelWaysResult`.
8. Navigate to `ResultsView` via `ResultsArgs(project, session)`.
9. `ResultsViewModel` exposes results; display modules (`SurfacePlot3DDisplay`, `MeasurementsGridRenderer`, `ParallelWaysDisplay`) render onto `Canvas` elements.
10. User saves via `IProjectFileService.SaveAsync` → `ProjectSerializer.Serialize` → `.levelproj` JSON file.

### Correction Workflow

1. `ResultsView` shows flagged steps from `SurfaceResult.FlaggedStepIndices`.
2. User starts correction → navigate to `CorrectionView` via `CorrectionArgs(project, session)`.
3. `CorrectionViewModel` guides user through flagged steps only; stores `CorrectionRound.ReplacedSteps`.
4. On completion, `MeasurementRound.MergeWithReplacements(initialSteps, corrections)` merges originals + replacements.
5. Full recalculation runs; new `SurfaceResult` stored on `CorrectionRound.Result`; navigate back to Results.
6. Original readings are never overwritten.

### Plugin Resolution at Startup

1. `App.xaml.cs` `BuildServiceProvider()` registers `IInstrumentPlugin` singletons (e.g., `ManualEntryPlugin`).
2. App ensures built-in manual device is present in `IDeviceRegistry`.
3. `MeasurementViewModel` receives `IEnumerable<IInstrumentPlugin>` via constructor injection.
4. Active plugin is selected by matching `session.InstrumentId` against `plugin.PluginId`.

**State Management:**
- `MainViewModel` (singleton) holds the currently-open `Project` and dirty flag; shared across all page ViewModels.
- Page ViewModels are transient — a fresh instance is created on each navigation.
- Persistent state lives in `%LOCALAPPDATA%\LevelApp\settings.json` (settings) and `devices.json` (device registry).

## Key Abstractions

**IMeasurementStrategy:**
- Purpose: Generates ordered step list for a given `ObjectDefinition`; knows nothing about calculation
- Examples: `LevelApp.Core/Geometry/SurfacePlate/Strategies/FullGridStrategy.cs`, `UnionJackStrategy.cs`, `LevelApp.Core/Geometry/ParallelWays/Strategies/ParallelWaysStrategy.cs`
- Pattern: Created via `StrategyFactory.Create(strategyId)` in `LevelApp.Core/Geometry/StrategyFactory.cs`

**ISurfaceCalculator:**
- Purpose: Computes `SurfaceResult` from steps, definition, and parameters
- Examples: `LevelApp.Core/Geometry/Calculators/LeastSquaresCalculator.cs`, `SequentialIntegrationCalculator.cs`
- Pattern: Created via `CalculatorFactory.Create(methodId, strategy)` in `LevelApp.Core/Geometry/CalculatorFactory.cs`

**IInstrumentPlugin:**
- Purpose: Root plugin contract; factory for `IInstrumentProvider`, `IDeviceScanner`, optional `IFirmwareUpdater` and `ICalibrationWorkflow`
- Examples: `LevelApp.Instruments.Manual/ManualEntryPlugin.cs`
- Pattern: Registered as `IInstrumentPlugin` singletons in DI; app resolves `IEnumerable<IInstrumentPlugin>`

**IInstrumentProvider:**
- Purpose: Provides instrument readings, exposes connection state
- Examples: `LevelApp.Instruments.Manual/ManualEntryProvider.cs`, `LevelApp.Instruments.BLE/BleInstrumentProviderBase.cs` (abstract), `LevelApp.Instruments.UsbHid/UsbHidInstrumentProviderBase.cs` (abstract)
- Pattern: Created per-device via `IInstrumentPlugin.CreateProvider(KnownDevice)`

**IDeviceRegistry:**
- Purpose: Persists known devices across sessions; supports preferred device selection per plugin
- Implementation: `LevelApp.App/Services/DeviceRegistry.cs` → `%LOCALAPPDATA%\LevelApp\devices.json`

## Entry Points

**Application Entry:**
- Location: `LevelApp.App/App.xaml.cs`
- Triggers: WinUI 3 `Application.OnLaunched`
- Responsibilities: Build DI container, load settings, bootstrap manual device in registry, create `MainWindow`

**MainWindow:**
- Location: `LevelApp.App/MainWindow.xaml` / `MainWindow.xaml.cs`
- Responsibilities: Menu bar (File, Edit, Instruments, Help), root navigation frame, theme wiring via `IThemeService`

**Navigation Bootstrap:**
- Location: `LevelApp.App/Navigation/NavigationService.cs`
- Responsibilities: Maps `PageKey` enum to concrete `Page` types; `Attach(Frame)` called once from `MainWindow`

## Architectural Constraints

- **Threading:** UI runs on WinUI 3 dispatcher thread; calculations dispatched to background thread via `Task.Run`; `IInstrumentProvider.GetReadingAsync` is async-safe
- **Global state:** `App.Services` (static `IServiceProvider`) is the only module-level singleton; `MainViewModel` is a DI singleton holding project state
- **Core isolation:** `LevelApp.Core` must never reference any UI assembly — this is enforced by the `.csproj` having no UI dependencies
- **Transport TFM:** `LevelApp.Instruments.BLE` and `LevelApp.Instruments.UsbHid` target `net8.0-windows10.0.19041.0` to access WinRT BLE/HID APIs without needing `Microsoft.Windows.SDK.Contracts`
- **Unpackaged app:** No MSIX packaging; WinRT `ApplicationData.Current.LocalFolder` is unavailable; use `Environment.SpecialFolder.LocalApplicationData` for all persistent paths

## Anti-Patterns

### Hardcoding version strings

**What happens:** Version string appears in XAML, code-behind, or comments outside `AppVersion.cs`
**Why it's wrong:** Creates divergence from `AppVersion.cs`; CI release step fails if tags mismatch
**Do this instead:** Always reference `AppVersion.Full` or `AppVersion.Display` from `LevelApp.Core/AppVersion.cs`

### Business logic in Views or code-behind

**What happens:** Calculation or state mutation placed in `*.xaml.cs` files
**Why it's wrong:** Violates MVVM; prevents unit testing; tightly couples UI to logic
**Do this instead:** All logic belongs in a ViewModel or a Core service/calculator; Views only bind and forward events

### Direct concrete instantiation of strategies or calculators

**What happens:** `new FullGridStrategy()` or `new LeastSquaresCalculator()` called from a ViewModel
**Why it's wrong:** Bypasses the factory layer; makes it hard to add strategies without touching call sites
**Do this instead:** Use `StrategyFactory.Create(strategyId)` (`LevelApp.Core/Geometry/StrategyFactory.cs`) and `CalculatorFactory.Create(methodId, strategy)` (`LevelApp.Core/Geometry/CalculatorFactory.cs`)

### Accessing UI resources directly from renderers

**What happens:** A display module calls `Application.Current.Resources["SomeColor"]` directly
**Why it's wrong:** Bypasses theme-aware resolution; breaks on theme switch
**Do this instead:** Use `ThemeHelper.GetColor`, `ThemeHelper.GetBrush`, or `ThemeHelper.GetPlotRamp` from `LevelApp.App/Helpers/ThemeHelper.cs`

## Error Handling

**Strategy:** Exceptions from calculations propagate to ViewModels; file I/O exceptions caught and surfaced via dialog; instrument errors surfaced via `InstrumentConnectionState` + `ConnectionStatusBar` InfoBar

**Patterns:**
- `IInstrumentProvider.ConnectionStateChanged` event drives `MeasurementViewModel.ShowConnectionWarning`
- `ProjectSerializer.Deserialize` throws `NotSupportedException` for unrecognised `schemaVersion`
- Unhandled exceptions logged as `CRASH` / `CRASH.UI` entries via `IActivityLogger`

## Cross-Cutting Concerns

**Logging:** `IActivityLogger` / `ActivityLogger` singleton writes `.jsonl` + `.instrument` files to `%LOCALAPPDATA%\LevelApp\Logs\`; toggleable via `ISettingsService.ActivityLoggingEnabled`
**Validation:** Input validation in ViewModels via CommunityToolkit.Mvvm observable properties; `CanExecute` guards on `RelayCommand`
**Localisation:** `.resw` resource files in `LevelApp.App/Strings/en-US/` and `de-DE/`; resolved via `ILocalisationService` / `LocalisationService`; XAML uses `x:Uid` mechanism
**Theming:** `IThemeService` / `ThemeService` singleton; theme-aware colours in `LevelApp.App/Styles/ThemeColors.xaml`; renderers use `ThemeHelper` at render time; views subscribe to `ActualThemeChanged`

---

*Architecture analysis: 2026-05-31*
