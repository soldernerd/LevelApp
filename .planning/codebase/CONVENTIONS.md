# Coding Conventions

**Analysis Date:** 2026-05-31

## Naming Patterns

**Files:**
- One class per file, filename matches class name exactly (e.g., `LeastSquaresCalculator.cs`)
- Interface files prefixed with `I` (e.g., `ISettingsService.cs`, `IInstrumentPlugin.cs`)
- ViewModel files suffixed `ViewModel` (e.g., `MeasurementViewModel.cs`, `MainViewModel.cs`)
- View files suffixed `View` or `Page` (e.g., `MeasurementView.xaml`, `InstrumentsPage.xaml`)
- Dialog files in `Views/Dialogs/` suffixed `Dialog` (e.g., `AboutDialog.xaml`)
- Test files suffixed `Tests` (e.g., `SurfacePlateCalculatorTests.cs`, `BleTransportTests.cs`)
- Strategy files suffixed `Strategy` (e.g., `FullGridStrategy.cs`, `UnionJackStrategy.cs`)
- Calculator files suffixed `Calculator` (e.g., `LeastSquaresCalculator.cs`, `ParallelWaysCalculator.cs`)

**Classes:**
- PascalCase for all type names
- `sealed` used liberally on concrete classes (e.g., `sealed class MeasurementViewModel`, `sealed class BleTransportTests`)
- `partial` used on ViewModels that use CommunityToolkit.Mvvm source generation
- Abstract base classes use `Base` suffix (e.g., `ViewModelBase`)
- Factory classes suffixed `Factory` (e.g., `StrategyFactory`, `CalculatorFactory`)

**Interfaces:**
- `I` prefix always (e.g., `INavigationService`, `IInstrumentPlugin`, `ISurfaceCalculator`)
- Describe capability, not implementation (e.g., `ICalibrationWorkflow`, not `CalibrationManager`)
- Optional capabilities returned as nullable from interfaces (e.g., `ICalibrationWorkflow? CreateCalibrationWorkflow(...)`)

**Methods:**
- PascalCase (e.g., `GenerateSteps`, `Calculate`, `RegisterDevice`)
- Command methods use imperative form (e.g., `SaveProject`, `AcceptReading`)
- CommunityToolkit.Mvvm `[RelayCommand]`-decorated methods use imperative names; toolkit generates `SaveProjectCommand` property automatically
- Private event handlers prefixed `On` (e.g., `OnViewModelPropertyChanged`, `OnConnectionStateChanged`, `OnActualThemeChanged`)
- Helper factory methods are `static` and named descriptively (e.g., `BuildRound`, `ZeroHeights`, `Def` in tests)

**Properties:**
- PascalCase auto-properties (e.g., `public Project? ActiveProject { get; private set; }`)
- Observable backing fields use `_camelCase` prefix (e.g., `private bool _isDirty`)
- CommunityToolkit `[ObservableProperty]` on backing fields generates the PascalCase public property
- UI-context properties (hwnd, XamlRoot) declared `internal` on ViewModels

**Constants:**
- PascalCase for public constants (e.g., `AppVersion.Major`, `AppVersion.Full`)
- `private const double` for rendering magic numbers (e.g., `NodeRadius`, `ArrowLen`, `CanvasPad`)

**Namespaces:**
- Mirror the directory tree: `LevelApp.App.ViewModels`, `LevelApp.Core.Geometry.Calculators`, `LevelApp.Tests.BLE`
- Test sub-namespaces group by feature area (e.g., `LevelApp.Tests.BLE`, `LevelApp.Tests.UsbHid`, `LevelApp.Tests.Replay`)

## Code Organization Patterns

**MVVM strictly enforced:**
- `LevelApp.Core` — no UI dependencies (no WinUI, no Microsoft.UI.Xaml imports)
- `LevelApp.App` — Views contain only rendering/navigation lifecycle code; all logic lives in ViewModels
- ViewModels injected via `Microsoft.Extensions.DependencyInjection`; views call `App.Services.GetRequiredService<T>()` in constructors
- Views bind to `ViewModel` property using `x:Bind` (compile-time binding, not `Binding`)

**Interface-first services:**
- Every App service has a corresponding interface: `ISettingsService`/`SettingsService`, `IProjectFileService`/`ProjectFileService`, `INavigationService`/`NavigationService`, etc.
- Core interfaces in `LevelApp.Core/Interfaces/` (e.g., `IInstrumentPlugin`, `ISurfaceCalculator`, `ITransport`)
- App service interfaces in `LevelApp.App/Services/` (e.g., `ISettingsService`, `ILocalisationService`)

**Plugin architecture:**
- Instrument plugins live in separate projects (`LevelApp.Instruments.Manual`, `LevelApp.Instruments.BLE`, `LevelApp.Instruments.UsbHid`)
- All plugins implement `IInstrumentPlugin` from `LevelApp.Core`
- Optional capabilities returned as nullable from plugin interface (e.g., `IFirmwareUpdater? CreateFirmwareUpdater(...)`)

**Dependency injection:**
- Constructor injection universally used; no service locator anti-pattern inside ViewModels
- Exception: Views call `App.Services.GetRequiredService<T>()` (acceptable as DI entry point in views)

**Section separators in code:**
- Long files use `// ── Section Name ──────────────────────────────────────────────────────────` comment blocks to divide logical sections (visible in `MainViewModel.cs`, `LeastSquaresCalculator.cs`, test files)

## C# Coding Style

**Nullable reference types:** Enabled (`<Nullable>enable</Nullable>`) across all projects. Use `?` for nullable references and guard with `is null` checks.

**Implicit usings:** Enabled (`<ImplicitUsings>enable</ImplicitUsings>`). Only explicitly `using` statements needed beyond the default set appear at the top of files.

**File-scoped namespaces:** Used throughout (e.g., `namespace LevelApp.Core.Models;`)

**Primary constructors:** Not used; traditional constructor bodies used instead.

**Pattern matching:**
- `switch` expressions preferred over `if`/`else if` chains (e.g., orientation dispatch in `MeasurementView.xaml.cs`)
- Null checks using `is null` / `is not null`
- `is` pattern for type checks and deconstruction

**Target-typed `new`:** Used for object initializers (e.g., `new() { ... }`)

**Collections:**
- `IReadOnlyList<T>` for read-only list parameters/properties
- `List<T>` for mutable private state
- `[]` empty collection literal preferred (C# 12) over `new List<T>()`

**`var`:** Used only when type is obvious from the right-hand side; explicit types preferred for clarity

**XML documentation:**
- `<summary>` blocks on interfaces and public API classes
- Inline comments explain non-obvious decisions; not used for obvious code
- Section-divider comment style: `// ── Label ─────` (em-dash + hyphens, right-padded to ~80 chars)

**Error handling:**
- `ArgumentException` for invalid inputs (empty collections, out-of-range)
- `InvalidOperationException` for precondition violations (e.g., unset readings)
- No swallowed exceptions; calculators throw on bad state

**Unsafe code:**
- `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in `LevelApp.App.csproj` (required for USB HID HID report interop)

## XAML Conventions

**Binding mode:**
- `x:Bind` (compiled binding) used exclusively — never `{Binding}`
- `Mode=OneWay` for ViewModel-driven properties; `Mode=TwoWay` for input controls
- `Mode=OneTime` when the value never changes after load

**Layout:**
- `RowDefinitions`/`ColumnDefinitions` declared inline on `Grid`
- `Grid.Row` / `Grid.Column` attached properties on each child
- `Padding` and `Margin` use shorthand (e.g., `Padding="24,16,24,12"`)

**Styles:**
- Named styles in `LevelApp.App/Styles/` (`ControlStyles.xaml`, `TextStyles.xaml`, `ThemeColors.xaml`)
- Reference via `{StaticResource}` (e.g., `Style="{StaticResource SubtitleTextBlockStyle}"`)
- Theme-aware colours in `ThemeColors.xaml`; resolved at draw time via `ThemeHelper.GetColor()`

**Code-behind restrictions:**
- Only navigation lifecycle (`OnNavigatedTo`, `OnNavigatedFrom`), canvas drawing, and event routing
- No business logic; all decisions delegated to ViewModel
- Canvas-heavy views (`MeasurementView`) have rendering helpers as `private` methods

**Comments in XAML:**
- Section-divider comment style: `<!-- ── Section Name ─────────────────────────────── -->`

## Versioning Conventions

**Single source of truth:** `LevelApp.Core/AppVersion.cs`

```csharp
public static class AppVersion
{
    public const int Major = 0;
    public const int Minor = 18;
    public const int Patch = 0;

    public static string Full    => $"{Major}.{Minor}.{Patch}";
    public static string Display => $"v{Full}";
}
```

- Never hardcode version strings elsewhere — not in XAML, not in code-behind, not in `.csproj` comments
- `.csproj` `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` kept in sync with `AppVersion.cs`
- Patch increments after every code change; Minor increments (Patch resets) when a work package completes
- `AppVersion.cs` and all `.csproj` files bumped before committing

## Git Commit Message Format

```
[vX.Y.Z] Short imperative description
```

Examples:
```
[v0.18.0] WP0.18: LevelApp.Instruments.UsbHid — USB HID transport + DFU subsystem
[v0.18.1] Update architecture.md to reflect WP0.14–WP0.18
```

- Always starts with `[vX.Y.Z]` matching `AppVersion.cs` at commit time
- Work package commits include the WP identifier (e.g., `WP0.18`)
- Imperative mood, present tense
- All commits on `master` branch only; no feature branches

---

*Convention analysis: 2026-05-31*
