# Coding Conventions

**Analysis Date:** 2026-06-02

LevelApp follows standard C# / .NET 8 conventions reinforced by its MVVM architecture and strict Core/App layer separation. The rules below are observed consistently across the codebase and must be followed when adding new code.

---

## Naming Patterns

**Files:**
- One public type per file; filename matches the type name exactly.
- ViewModel files: `{Name}ViewModel.cs` — e.g., `LevelApp.App/ViewModels/MeasurementViewModel.cs`
- Service files: `{Name}Service.cs` paired with `I{Name}Service.cs` — e.g., `LevelApp.App/Services/ProjectFileService.cs` / `LevelApp.App/Services/IProjectFileService.cs`
- View files: `{Name}View.xaml` / `{Name}View.xaml.cs`; dialogs in `LevelApp.App/Views/Dialogs/{Name}Dialog.xaml`
- Test files: `{SubjectUnderTest}Tests.cs` — e.g., `LevelApp.Tests/FullGridStrategyTests.cs`
- Transport-specific test subdirectories: `LevelApp.Tests/BLE/`, `LevelApp.Tests/UsbHid/`, `LevelApp.Tests/Replay/`

**Interfaces:**
- Always prefixed with `I` — e.g., `IInstrumentPlugin`, `IActivityLogger`, `INavigationService`
- Named after the capability, not the implementation: `IDeviceRegistry` (not `IDeviceRegistryService`)
- All Core contracts live in `LevelApp.Core/Interfaces/`

**Classes:**
- PascalCase throughout
- Abstract base classes use suffix `Base` — e.g., `BleInstrumentProviderBase`, `UsbHidInstrumentProviderBase`
- `ViewModelBase` (`LevelApp.App/ViewModels/ViewModelBase.cs`) is the root for all ViewModels

**Methods:**
- PascalCase for public and protected members
- Async methods always end with `Async` — e.g., `ConnectAsync`, `SaveAsAsync`, `FlashAsync`
- Private helpers also use PascalCase — e.g., `RunOnStaThread`, `ConfigureFileTypes`

**Fields and locals:**
- Private instance fields: `_camelCase` with leading underscore — e.g., `_navigation`, `_mainViewModel`, `_lock`
- `[ObservableProperty]`-annotated backing fields: `_camelCase` — the CommunityToolkit generator produces the public PascalCase property automatically
- Local variables: `camelCase`
- Compile-time constants: PascalCase — e.g., `const int LogRetentionDays = 14`
- Win32 P/Invoke constants that mirror their native names: SCREAMING_SNAKE_CASE — e.g., `CLSCTX_INPROC_SERVER`, `SIGDN_FILESYSPATH`

**Enums:**
- Type name and member names both PascalCase — e.g., `Orientation.North`, `PassPhase.Forward`, `SolverMode.GlobalLeastSquares`
- Enum values stored as strings in `ObjectDefinition.Parameters` (use `.ToString()` when writing, `Enum.Parse` when reading)

**Test methods:**
- `MethodName_Condition_ExpectedOutcome` — e.g., `FlatSurface_ZeroReadings_ProducesZeroHeightsAndFlatness`, `ConnectAsync_Sets_Connected_On_Success`
- Underscores used **only** in test method names, not in production code

---

## MVVM Rules

All ViewModels extend `ViewModelBase` (`LevelApp.App/ViewModels/ViewModelBase.cs`), which itself extends `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`.

**Source generation is the standard way to declare observable state:**
```csharp
// DO: use CommunityToolkit source generation
[ObservableProperty] private string _label = string.Empty;
[ObservableProperty] private double _lengthMm = 1000.0;

// To notify derived computed properties on change:
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsBridge))]
[NotifyPropertyChangedFor(nameof(IsAlongRail))]
private int _taskTypeIndex = 0;
```

**Commands use `[RelayCommand]` from `CommunityToolkit.Mvvm.Input`.**

**No business logic in Views or code-behind.** Views may contain only:
- Navigation plumbing (`Frame.Navigate`, `OnNavigatedTo`)
- `App.Services.GetRequiredService<T>()` calls that are unavoidable because `Frame.Navigate` bypasses DI — these must carry an explanatory comment

**ViewModels receive navigation data via `Initialize(args)` called from `OnNavigatedTo`** in the view code-behind, not via constructor parameters. Navigation arg types live in `LevelApp.App/Navigation/` — e.g., `MeasurementArgs`, `ResultsArgs`, `CorrectionArgs`.

**`App.Services` static service locator** is acknowledged debt. Do not add new call sites without a comment explaining why constructor injection is not available.

---

## Layer Separation

- `LevelApp.Core` must have zero UI dependencies. All interfaces, models, geometry calculators, and serialization live here and are fully unit-testable.
- `LevelApp.App` depends on Core and may use WinUI 3 types freely.
- Instrument projects (`LevelApp.Instruments.Manual`, `LevelApp.Instruments.BLE`, `LevelApp.Instruments.UsbHid`) depend on Core only.
- When a Core interface must return a UI element (e.g., `IInstrumentPlugin.CreateDeviceManagementView`), the return type is `object?`. The App layer casts to `UIElement`. This keeps the interface in Core without importing WinUI.

---

## Interface-First Design

Define interfaces before implementing them. It is explicitly correct for optional capability methods to return `null` when not yet implemented:

```csharp
// DO: returning null is the correct signal that a capability is absent for this plugin
public ICalibrationWorkflow? CreateCalibrationWorkflow(KnownDevice device) => null;
public IFirmwareUpdater?     CreateFirmwareUpdater(KnownDevice device)     => null;
```

Do NOT stub these with placeholder throws or empty non-null implementations.

---

## Dependency Injection

- All new services require a corresponding interface registered in `LevelApp.App/App.xaml.cs`.
- Use constructor injection everywhere the DI container controls object creation.
- `IEnumerable<IInstrumentPlugin>` is registered as a collection; all registered plugins are injected together into consumers like `MeasurementViewModel`.
- `IWindowContext` → `WindowContext` (internal singleton) provides `XamlRoot?` and `Hwnd` to `MainViewModel` without post-construction property assignment.

---

## Import Organization

**Order (observed throughout the codebase):**
1. `System.*` namespaces
2. Third-party packages (`CommunityToolkit.Mvvm.*`, `Microsoft.UI.*`)
3. Project namespaces (`LevelApp.Core.*`, `LevelApp.App.*`, `LevelApp.Instruments.*`)

**Using aliases** are used sparingly to resolve genuine name conflicts:
```csharp
using UJRings = LevelApp.Core.Geometry.SurfacePlate.Strategies.UnionJackRings;
using InstrumentPlugin = LevelApp.Core.Interfaces.IInstrumentPlugin;
```

---

## Section Separators

Long files use ASCII banner comments to delineate logical sections. This pattern appears in ViewModels, Services, and test files:

```csharp
// ── Initialisation ────────────────────────────────────────────────────────────

// ── COM dialog helpers ────────────────────────────────────────────────────────

// ── P/Invoke ──────────────────────────────────────────────────────────────────
```

Use this pattern whenever a file has more than two conceptual sections.

---

## Error Handling

**Services that call external resources (network, file system) catch broadly and return null or a sentinel:**
```csharp
// Pattern in UpdateService.CheckForUpdateAsync — any network error must not prevent startup
try { ... }
catch { return null; }
```

**`HttpClient` must always have an explicit `Timeout`:**
```csharp
// DO:
var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
// DON'T: leave Timeout unset (default is infinite — acknowledged debt)
```

**Precondition failures in Core geometry use standard BCL exceptions:**
- `ArgumentException` for invalid input — e.g., `FullGridStrategy.GenerateSteps` on a grid smaller than 2×2
- `InvalidOperationException` for bad state — e.g., calling a calculator with a null reading, or accessing a result before it exists
- `ArgumentOutOfRangeException` for out-of-range numeric parameters — e.g., `DfuSession` constructor with `pageSize: 0`
- `ObjectDisposedException` from disposable types after `Dispose()` — e.g., `DfuSession.FlashAsync`

---

## XML Documentation Comments

Public types and members carry `<summary>` XML doc comments. The convention is complete prose sentences that explain purpose, not just rephrase the name:

```csharp
/// <summary>
/// Presents native Win32 IFileOpenDialog / IFileSaveDialog pickers (bypassing the
/// WinRT FileOpenPicker / FileSavePicker wrappers) and delegates to
/// <see cref="ProjectSerializer"/> for JSON I/O.
/// </summary>
```

Implementation-level "why" notes use `//` inline comments above the relevant line.

---

## Versioning Convention

Version lives **only** in `LevelApp.Core/AppVersion.cs`. Never hardcode version strings anywhere else — not in XAML, not in comments, not in string literals. Use `AppVersion.Full` (`"0.19.0"`) or `AppVersion.Display` (`"v0.19.0"`) wherever a version string is needed at runtime.

The `.csproj` `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` fields are kept in sync with `AppVersion.cs` manually.

---

## Known Inconsistencies / Acknowledged Debt

- **Static renderer classes** (`SurfacePlot3DDisplay`, `MeasurementsGridRenderer`, `StrategyPreviewRenderer`, `ParallelWaysDisplay` in `LevelApp.App/DisplayModules/`) have no shared interface. Do not add a fifth static renderer — define `IDisplayModule` first.
- **`App.Services` call sites** in `LevelApp.App/MainWindow.xaml.cs` and XAML code-behind pages are deliberate exceptions to DI injection; they carry comments explaining the exception. Do not add new ones without a comment.

---

*Convention analysis: 2026-06-02*
