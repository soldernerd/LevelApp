<!-- refreshed: 2026-06-02 -->
# Architecture

**Analysis Date:** 2026-06-02

## System Overview

LevelApp is a WinUI 3 MVVM desktop application for evaluating precision electronic level measurements. It is organized as a multi-project .NET 8 solution with strict separation between a UI-free core library, instrument transport infrastructure, the WinUI 3 app shell, and a standalone updater executable.

```text
┌──────────────────────────────────────────────────────────────────────────┐
│                         LevelApp.App (WinUI 3)                           │
│  Views/  ←x:Bind→  ViewModels/  ←DI→  Services/  Navigation/            │
│  DisplayModules/ (static renderers)    Styles/ Strings/ Converters/      │
└───────┬────────────────────┬──────────────────────────────────┬──────────┘
        │ ProjectReference   │ ProjectReference                 │ (none —
        ▼                    ▼                                  │  standalone)
┌──────────────────┐  ┌──────────────────────┐           ┌─────▼──────────┐
│ LevelApp.Core    │  │LevelApp.Instruments   │           │LevelApp.Updater│
│  Interfaces/     │  │  .Manual             │           │  Program.cs    │
│  Models/         │  │  .BLE  (infra only)  │           │(net8.0, no UI) │
│  Geometry/       │  │  .UsbHid (infra only)│           └────────────────┘
│  Instruments/    │  └──────────────────────┘
│  Serialization/  │      all reference LevelApp.Core
│  AppVersion.cs   │
└──────────────────┘
        ↑
LevelApp.Tests (references Core + all Instrument projects)
```

## Component Responsibilities

| Component | Responsibility | Key Path |
|-----------|----------------|----------|
| `LevelApp.Core` | Domain models, all interfaces, geometry algorithms, JSON serialization. Zero UI dependencies. | `LevelApp.Core/` |
| `LevelApp.App` | WinUI 3 shell — Views, ViewModels, Services, Navigation, DI composition root | `LevelApp.App/` |
| `LevelApp.Instruments.Manual` | Manual-entry instrument plugin. Sole registered `IInstrumentPlugin` in current DI container. | `LevelApp.Instruments.Manual/` |
| `LevelApp.Instruments.BLE` | BLE transport infrastructure (abstract base, scanner, connection manager). Not registered as plugin. | `LevelApp.Instruments.BLE/` |
| `LevelApp.Instruments.UsbHid` | USB HID transport infrastructure + full STM32 DFU subsystem. Not registered as plugin. | `LevelApp.Instruments.UsbHid/` |
| `LevelApp.Tests` | xUnit test project. References Core + all instrument projects. | `LevelApp.Tests/` |
| `LevelApp.Updater` | Standalone self-contained exe. Copy-to-temp update pattern. No shared project reference to App. | `LevelApp.Updater/` |

## Pattern Overview

**Overall:** Layered MVVM with interface-driven DI, strategy + factory patterns for domain algorithms, and a plugin architecture for instruments.

**Key Characteristics:**
- `LevelApp.Core` has zero UI dependencies — fully testable without a running WinUI app
- All service and ViewModel wiring goes through `Microsoft.Extensions.DependencyInjection` registered in `App.xaml.cs`
- ViewModels trigger navigation via `INavigationService` / `PageKey` enum — no compile-time dependency on View types
- Instrument support is additive: add a project, subclass a base, register one `IInstrumentPlugin` in `App.xaml.cs`
- Static factory classes (`StrategyFactory`, `CalculatorFactory`) centralise instantiation of domain strategies and calculators

## Layers

**Core Domain Layer (`LevelApp.Core`):**
- Purpose: All business logic, data models, interfaces, geometry algorithms, and serialization
- Location: `LevelApp.Core/`
- Contains: Interfaces, Models, Geometry strategies and calculators, Instrument enums/value types, DeviceRegistry, Serialization helpers
- Depends on: Nothing (net8.0, no Windows or UI APIs)
- Used by: `LevelApp.App`, all `LevelApp.Instruments.*`, `LevelApp.Tests`

**Instrument Infrastructure Layer (`LevelApp.Instruments.*`):**
- Purpose: Transport-specific hardware abstraction (BLE, USB HID) and the manual-entry plugin
- Location: `LevelApp.Instruments.Manual/`, `LevelApp.Instruments.BLE/`, `LevelApp.Instruments.UsbHid/`
- Contains: `ITransport` implementations, `IDeviceScanner` implementations, abstract `IInstrumentProvider` base classes, STM32 DFU subsystem
- Depends on: `LevelApp.Core`; BLE and UsbHid additionally target `net8.0-windows10.0.19041.0` for WinRT APIs
- Used by: `LevelApp.App` (only Manual is referenced), `LevelApp.Tests`

**Application Shell Layer (`LevelApp.App`):**
- Purpose: WinUI 3 UI, MVVM wiring, DI composition root, application services
- Location: `LevelApp.App/`
- Contains: Views (XAML + code-behind), ViewModels, Services, Navigation, DisplayModules, Styles, Strings (`.resw`)
- Depends on: `LevelApp.Core`, `LevelApp.Instruments.Manual`
- Used by: Nothing (top-level executable)

**Test Layer (`LevelApp.Tests`):**
- Purpose: Unit and integration tests for Core algorithms, instrument infrastructure, and replay testing
- Location: `LevelApp.Tests/`
- Contains: Strategy tests, calculator tests, instrument provider tests, plugin architecture tests, DFU session tests, replay runner
- Depends on: All four projects (`Core`, `Manual`, `BLE`, `UsbHid`)

## DI Container Wiring

The DI container is built and stored as `App.Services` (a `public static IServiceProvider`) in `LevelApp.App/App.xaml.cs`. All registrations happen in `BuildServiceProvider()`.

```csharp
// Singletons
services.AddSingleton<IWindowContext, WindowContext>();
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<IThemeService, ThemeService>();
services.AddSingleton<INavigationService, NavigationService>();
services.AddSingleton<ILocalisationService, LocalisationService>();
services.AddSingleton<IInstrumentPlugin, ManualEntryPlugin>();   // one per plugin type
services.AddSingleton<IDeviceRegistry>(_ => new DeviceRegistry(path));
services.AddSingleton<IProjectFileService, ProjectFileService>();
services.AddSingleton<IActivityLogger, ActivityLogger>();
services.AddSingleton<IUpdateService, UpdateService>();
services.AddSingleton<MainViewModel>();

// Transients (fresh instance per navigation)
services.AddTransient<ParallelWaysCalculator>();
services.AddTransient<ProjectSetupViewModel>();
services.AddTransient<MeasurementViewModel>();
services.AddTransient<ResultsViewModel>();
services.AddTransient<CorrectionViewModel>();
services.AddTransient<InstrumentsViewModel>();
```

**Critical constraint:** `MainWindow` is created with `new MainWindow()` in `App.OnLaunched` — it is NOT resolved from DI. All `App.Services.GetRequiredService<>()` calls in `MainWindow.cs` are intentionally concentrated in the constructor block with a comment documenting this. XAML code-behind pages (created by `Frame.Navigate`, not DI) also carry explanatory comments at each call site.

**Rule:** Never add `App.Services.GetRequiredService<>()` without a comment explaining why constructor injection is unavailable.

## MVVM Structure

```text
View (XAML + minimal code-behind)
  ├── x:Bind → ViewModel properties and commands
  ├── OnNavigatedTo → calls ViewModel.Initialize(args)
  └── ActualThemeChanged → triggers canvas re-render

ViewModel (inherits ViewModelBase → ObservableObject via CommunityToolkit.Mvvm)
  ├── [ObservableProperty] → source-generated INotifyPropertyChanged
  ├── [RelayCommand] → source-generated ICommand
  └── Constructor injection of: INavigationService, MainViewModel,
       ILocalisationService, IInstrumentPlugin list, IDeviceRegistry, etc.

MainViewModel (singleton)
  └── Owns: ActiveProject, CurrentFilePath, IsDirty, WindowTitle
      Injected into all page ViewModels so they share project state
```

**`ViewModelBase`:** `LevelApp.App/ViewModels/ViewModelBase.cs` — inherits `ObservableObject`; all page ViewModels inherit from it.

**`MainViewModel`** (`LevelApp.App/ViewModels/MainViewModel.cs`) is the singleton shell ViewModel. It owns the active `Project` object and dirty flag. Page ViewModels call `_mainViewModel.SetActiveProject()` and `_mainViewModel.MarkDirty()`. Injected dependencies: `INavigationService`, `IProjectFileService`, `IActivityLogger`, `IWindowContext`.

## Navigation Pattern

Navigation is abstracted behind `INavigationService` / `PageKey` so ViewModels have no compile-time dependency on View types.

```csharp
// In ViewModels — no reference to concrete page types:
_navigation.NavigateTo(PageKey.Results, new ResultsArgs(project, session));

// NavigationService maps PageKey to concrete Page type:
private static readonly Dictionary<PageKey, Type> PageMap = new()
{
    [PageKey.ProjectSetup] = typeof(ProjectSetupView),
    [PageKey.Measurement]  = typeof(MeasurementView),
    [PageKey.Results]      = typeof(ResultsView),
    [PageKey.Correction]   = typeof(CorrectionView),
    [PageKey.Instruments]  = typeof(InstrumentsPage)
};
```

- `INavigationService` interface: `LevelApp.App/Navigation/INavigationService.cs`
- `NavigationService` implementation: `LevelApp.App/Navigation/NavigationService.cs`
- `PageKey` enum: `LevelApp.App/Navigation/PageKey.cs`
- Navigation args (typed records): `LevelApp.App/Navigation/MeasurementArgs.cs`, `ResultsArgs.cs`, `CorrectionArgs.cs`
- `NavigationService.Attach(Frame)` is called once from `MainWindow` constructor

## Data Flow

### Primary Measurement Path

1. **ProjectSetupViewModel** (`LevelApp.App/ViewModels/ProjectSetupViewModel.cs`) — operator defines object geometry, selects strategy; calls `_navigation.NavigateTo(PageKey.Measurement, new MeasurementArgs(project, session))`
2. **`MeasurementView.OnNavigatedTo`** → calls `MeasurementViewModel.Initialize(args)`; ViewModel resolves `IInstrumentProvider` from `IInstrumentPlugin` list via `IDeviceRegistry`
3. **`MeasurementViewModel`** (`LevelApp.App/ViewModels/MeasurementViewModel.cs`) — steps through `IMeasurementStrategy.GenerateSteps()`, calls `_provider.GetReadingAsync()` per step, stores reading on `MeasurementStep`
4. **On completion** — calls `CalculatorFactory.Create(methodId, strategy)` then `calculator.Calculate(steps, definition, parameters)` on background thread; result stored as `SurfaceResult` on `MeasurementRound`; navigates to Results
5. **`ResultsViewModel`** (`LevelApp.App/ViewModels/ResultsViewModel.cs`) — displays `SurfaceResult` via `SurfacePlot3DDisplay`, `MeasurementsGridRenderer`; exposes correction workflow commands
6. **`CorrectionViewModel`** (`LevelApp.App/ViewModels/CorrectionViewModel.cs`) — guided re-measurement of flagged steps only; stores new readings as `CorrectionRound`; re-runs calculator; navigates back to Results

### Parallel Ways Path

`MeasurementViewModel` detects `ObjectDefinition.GeometryModuleId == "ParallelWays"` and dispatches to the injected `ParallelWaysCalculator` (registered Transient in DI) instead of using `CalculatorFactory`. Results are stored as `ParallelWaysResult` on `MeasurementRound` (separate field from `SurfaceResult`).

### Persistence Path

1. `MainViewModel` calls `IProjectFileService.SaveAsync(project, path)` or `LoadAsync(path)`
2. `ProjectFileService` (`LevelApp.App/Services/ProjectFileService.cs`) invokes Win32 COM file dialogs (`IFileOpenDialog`/`IFileSaveDialog`) for path selection (bypasses WinRT pickers due to unreliable `SetFolder` behavior)
3. Serialization by `ProjectSerializer` (`LevelApp.Core/Serialization/ProjectSerializer.cs`) using `System.Text.Json` + `ObjectValueConverter` + `JsonStringEnumConverter`
4. File written as `.levelproj` (indented JSON, camelCase, schema version `"1.1"`)

### Update Path

1. `MainWindow` triggers `UpdateService.CheckForUpdateAsync()` after `RootFrame.Loaded`
2. `UpdateService` (`LevelApp.App/Services/UpdateService.cs`) polls GitHub Releases API; `HttpClient.Timeout = 10s`
3. If update found, `UpdateDialog` is shown; downloads zip to `%TEMP%`
4. `UpdateDialog.xaml.cs` strips trailing backslash from `AppContext.BaseDirectory` then launches `LevelApp.Updater.exe` with four positional args defined by `UpdaterContract.cs` constants
5. `LevelApp.Updater/Program.cs` uses copy-to-temp pattern, waits for app exit, extracts zip over install folder, relaunches app

## Instrument Plugin Architecture

Three-tier hierarchy defined entirely in `LevelApp.Core/Interfaces/`:

```text
IInstrumentPlugin               (root — one per instrument type)
  ├── ITransport[]              (transport descriptor: "manual", "ble", "usb-hid")
  ├── IDeviceScanner[]          (yields DeviceCandidate via IAsyncEnumerable)
  └── IInstrumentProvider       (drives measurement loop per connected device)
        IFirmwareUpdater?       (optional DFU; null = not supported)
        ICalibrationWorkflow?   (optional calibration UI; null = not supported)
```

**Adding a new hardware plugin:**
1. Create a new project referencing `LevelApp.Instruments.BLE` or `LevelApp.Instruments.UsbHid`
2. Subclass `BleInstrumentProviderBase` or `UsbHidInstrumentProviderBase`
3. Implement `IInstrumentPlugin`
4. Register `services.AddSingleton<IInstrumentPlugin, YourPlugin>()` in `App.xaml.cs`

**Currently registered plugins:**

| Plugin | File | PluginId | FirmwareUpdater | CalibrationWorkflow |
|--------|------|----------|-----------------|---------------------|
| `ManualEntryPlugin` | `LevelApp.Instruments.Manual/ManualEntryPlugin.cs` | `"manual-entry"` | `null` | `null` |

`IFirmwareUpdater` and `ICalibrationWorkflow` are defined in `LevelApp.Core/Interfaces/` but have no concrete implementations. Returning `null` from plugin factory methods is the correct, intentional signal that the capability is absent.

**Infrastructure-only projects** (`LevelApp.Instruments.BLE`, `LevelApp.Instruments.UsbHid`) are compiled and tested but not registered as plugins — they contain no instrument-specific code.

## Strategy and Calculator Pattern

Both geometry strategies and surface calculators use a static factory + interface pattern defined in `LevelApp.Core/Geometry/`.

```csharp
// LevelApp.Core/Geometry/StrategyFactory.cs
// Adding a strategy = one new class + one line here
public static IMeasurementStrategy Create(string strategyId) => strategyId switch
{
    "UnionJack"    => new UnionJackStrategy(),
    "ParallelWays" => new ParallelWaysStrategy(),
    _              => new FullGridStrategy()
};

// LevelApp.Core/Geometry/CalculatorFactory.cs
// Adding a calculator = one new class + one line here
public static ISurfaceCalculator Create(string methodId, IMeasurementStrategy strategy) =>
    methodId == "SequentialIntegration"
        ? new SequentialIntegrationCalculator(strategy)
        : new LeastSquaresCalculator(strategy);
```

**`IMeasurementStrategy`** (`LevelApp.Core/Interfaces/IMeasurementStrategy.cs`) — generates ordered step list, provides node positions for rendering. Implementations: `FullGridStrategy`, `UnionJackStrategy`, `ParallelWaysStrategy` in `LevelApp.Core/Geometry/`.

**`ISurfaceCalculator`** (`LevelApp.Core/Interfaces/ISurfaceCalculator.cs`) — runs calculation, returns `SurfaceResult`. Implementations: `LeastSquaresCalculator`, `SequentialIntegrationCalculator` in `LevelApp.Core/Geometry/Calculators/`.

**`ParallelWaysCalculator`** (`LevelApp.Core/Geometry/ParallelWays/ParallelWaysCalculator.cs`) — standalone class, NOT `ISurfaceCalculator`. Registered as Transient in DI and injected into `MeasurementViewModel` directly.

## Key Abstractions

**`IWindowContext` / `WindowContext`** (`LevelApp.App/Services/IWindowContext.cs`, `WindowContext.cs`):
- Provides `XamlRoot?` and `Hwnd` to ViewModels that need to show `ContentDialog`
- Registered as singleton; `MainWindow` sets `Hwnd` and `XamlRoot` after construction via the `(WindowContext)` cast
- Prevents post-construction property assignments on ViewModels; keeps them testable

**`IDeviceRegistry` / `DeviceRegistry`** (`LevelApp.Core/Interfaces/IDeviceRegistry.cs`, `LevelApp.Core/Instruments/DeviceRegistry.cs`):
- Persists `KnownDevice` records to `%LOCALAPPDATA%\LevelApp\devices.json`
- Exposes `LoadError` (non-null if `devices.json` was corrupt on startup — file backed up to `.corrupt`, warning shown in InstrumentsPage `InfoBar`)
- Registered as singleton with explicit factory lambda in `App.xaml.cs`

**`UpdaterContract`** (`LevelApp.App/Services/UpdaterContract.cs`, `LevelApp.Updater/UpdaterContract.cs`):
- Named constants for the cross-process argument contract between `UpdateDialog` and `LevelApp.Updater.exe`
- Duplicated verbatim in both projects with a sync comment (cannot share — `LevelApp.Updater` targets `net8.0` without Windows TFM and cannot reference `LevelApp.App`)

## Display Modules

Four static renderer classes in `LevelApp.App/DisplayModules/`. Each receives data and returns a rendered `UIElement` placed by the caller's View.

| Module | File | Input |
|--------|------|-------|
| `SurfacePlot3DDisplay` | `LevelApp.App/DisplayModules/SurfacePlot3D/SurfacePlot3DDisplay.cs` | `SurfaceResult`, strategy, definition, steps |
| `MeasurementsGridRenderer` | `LevelApp.App/DisplayModules/MeasurementsGrid/MeasurementsGridRenderer.cs` | Steps, result, strategy |
| `StrategyPreviewRenderer` | `LevelApp.App/DisplayModules/StrategyPreview/StrategyPreviewRenderer.cs` | Strategy, definition |
| `ParallelWaysDisplay` | `LevelApp.App/DisplayModules/ParallelWaysDisplay/ParallelWaysDisplay.cs` | `ParallelWaysResult`, strategy parameters |

All four resolve colors via `ThemeHelper` (`LevelApp.App/Helpers/ThemeHelper.cs`) — `GetColor`, `GetBrush`, `GetPlotRamp`, `InterpolateRamp`. Views subscribe to `ActualThemeChanged` and re-render on theme switch.

**Active technical debt:** These four modules share no common interface. A fifth renderer must NOT be added as a static class — define `IDisplayModule` first and migrate existing modules.

## Data Model Hierarchy

Root model is `Project` (`LevelApp.Core/Models/Project.cs`). Full tree:

```text
Project                               (LevelApp.Core/Models/Project.cs)
├── ObjectDefinition                  (Models/ObjectDefinition.cs)
│   ├── GeometryModuleId              ("SurfacePlate" or "ParallelWays")
│   └── Parameters                   (Dictionary<string, object>)
└── Measurements[]
    └── MeasurementSession            (Models/MeasurementSession.cs)
        ├── InitialRound (MeasurementRound)  (Models/MeasurementRound.cs)
        │   ├── Steps[] (MeasurementStep)    (Models/MeasurementStep.cs)
        │   ├── Result (SurfaceResult?)      (Models/SurfaceResult.cs)
        │   └── ParallelWaysResult?          (Models/ParallelWaysResult.cs)
        └── Corrections[]
            └── CorrectionRound              (Models/CorrectionRound.cs)
                ├── ReplacedSteps[]
                └── Result (SurfaceResult?)
```

**Key rule:** Raw readings and all intermediate results are always preserved. Nothing is overwritten. `MeasurementRound.MergeWithReplacements` (static helper) merges correction replacements into the original step list for recalculation.

## Error Handling

**Strategy:** Errors bubble up through typed exceptions. The `DeviceRegistry` backs up corrupt JSON to `.corrupt` and sets `LoadError`. `UpdateService` catches all network exceptions and returns `null`. Unhandled exceptions are caught in `App.OnLaunched` hooks and written to the activity log (`%LOCALAPPDATA%\LevelApp\Logs\`).

**Patterns:**
- Domain logic throws standard .NET exceptions (e.g., `NotSupportedException` in `ProjectSerializer` for unrecognised schema versions)
- Services that cannot fail silently return `null` or a `LoadError` property rather than throwing to the caller
- All `HttpClient` instances must have explicit `Timeout` set — enforced rule since WP0.19

## Cross-Cutting Concerns

**Logging:** `IActivityLogger` / `ActivityLogger` (`LevelApp.App/Services/ActivityLogger.cs`) writes `.jsonl` + `.instrument` files to `%LOCALAPPDATA%\LevelApp\Logs\`. Toggleable via `ISettingsService.ActivityLoggingEnabled`. Prunes files older than 14 days on startup.

**Localisation:** `ILocalisationService` / `LocalisationService` wrapping `Windows.ApplicationModel.Resources.ResourceLoader`. `.resw` files at `LevelApp.App/Strings/en-US/Resources.resw` and `de-DE/Resources.resw` (195 keys each). XAML uses `x:Uid`; code uses `_loc.Get(key)`.

**Theming:** `IThemeService` / `ThemeService` singleton. Color tokens in `LevelApp.App/Styles/ThemeColors.xaml` (`ThemeDictionaries` — Light + Dark). `ThemeHelper` provides `GetColor`/`GetBrush`/`GetPlotRamp`/`InterpolateRamp` for all renderers.

**Versioning:** `LevelApp.Core/AppVersion.cs` is the single source of truth. Version strings must never be hardcoded elsewhere. `.csproj` `<Version>`, `<AssemblyVersion>`, `<FileVersion>` fields are kept in sync manually.

## Architectural Constraints

- **UI thread:** WinUI 3 requires UI updates on the dispatcher thread. Calculators run on background threads; results are marshalled back.
- **Global state:** `App.Services` (`public static IServiceProvider`) is the only module-level singleton. Its use is restricted to documented composition-root sites.
- **Core purity:** `LevelApp.Core` must never reference any Windows, WinRT, or WinUI APIs. It targets plain `net8.0`.
- **No circular imports:** `Core` → nothing; `Instruments.*` → `Core`; `App` → `Core` + `Instruments.Manual`; `Tests` → all.
- **Unpackaged app:** No MSIX packaging. `ApplicationData.Current.LocalFolder` unavailable. All local paths use `Environment.SpecialFolder.LocalApplicationData`. WinRT USB APIs require packaging — hence P/Invoke to `WinUsb.dll` for DFU in `LevelApp.Instruments.UsbHid`.

## Anti-Patterns

### Adding a fifth static display module

**What happens:** A new static class is added to `DisplayModules/` alongside the existing four.
**Why it's wrong:** Callers must edit View code-behind directly; no polymorphic dispatch; untestable.
**Do this instead:** Define `IDisplayModule` in `LevelApp.Core` or `LevelApp.App`, migrate existing four modules, then implement the new one against the interface.

### Uncommented `App.Services.GetRequiredService<>()` in new code

**What happens:** A ViewModel or service pulls a dependency from `App.Services` without explanation.
**Why it's wrong:** Obscures the DI graph; breaks testability; looks like legitimate constructor injection when it is not.
**Do this instead:** Use constructor injection. If at a genuine composition-root site (e.g., `MainWindow` constructor or XAML code-behind created by `Frame.Navigate`), add a comment explaining why constructor injection is unavailable at that site.

### Post-construction property assignment on ViewModels for UI handles

**What happens:** A ViewModel exposes a settable property for `XamlRoot` or `Hwnd` that a View sets after construction.
**Why it's wrong:** Temporal coupling; ViewModel is not testable without a live window.
**Do this instead:** Inject `IWindowContext` via constructor. `WindowContext` (`LevelApp.App/Services/WindowContext.cs`) is the designated pattern.

---

*Architecture analysis: 2026-06-02*
