# Concerns

## Architecture & Design

### BLE and USB HID plugins not registered in DI
`App.xaml.cs` only registers `ManualEntryPlugin`. `LevelApp.Instruments.BLE` and `LevelApp.Instruments.UsbHid` projects exist and are built, but are not wired into the DI container as `IInstrumentPlugin` registrations. This means BLE/USB devices appear in the `InstrumentsPage` only if the App project explicitly adds them — a silent omission that could confuse future developers.

### `App.Services` is a static service locator
`App.Services` is a `public static IServiceProvider` accessed globally. ViewModels that can't receive DI through constructors fall back to `App.Services.GetRequiredService<T>()`. This bypasses DI's testability guarantees and makes dependencies invisible from signatures.

### `MainViewModel` carries UI handles (`XamlRoot`, `Hwnd`)
Setting `XamlRoot` and `Hwnd` from `MainWindow` code-behind after construction is a code smell — it ties the ViewModel to WinUI lifecycle ordering and makes it untestable without a real window.

### No geometry module abstraction above `StrategyFactory` / `CalculatorFactory`
The two factories are static-style classes. Adding a third geometry domain (e.g., machine bed) would require touching both factories. No `IGeometryModule` registration pattern exists to make this extensible.

## Error Handling

### `DeviceRegistry.Load` silently swallows `JsonException`
A corrupt `devices.json` file causes silent data loss (registry starts empty). The user gets no feedback and loses all remembered devices. Should at least log the error and optionally offer to reset.

### `UpdateService` catches all exceptions silently
The entire `CheckForUpdateAsync` body is wrapped in a bare `catch { return null; }`. This is intentional (startup resilience) but hides network configuration errors and SSL failures that might be worth diagnosing.

### `UpdateService` uses a static `HttpClient`
The singleton `HttpClient` has no timeout configured. A hung GitHub API call during startup will block indefinitely. Should set `Timeout` explicitly (e.g., 10 s).

## Testability

### No tests for ViewModels
All ViewModel logic is untested. `MainViewModel`, `MeasurementViewModel`, and `CorrectionViewModel` contain non-trivial state machines. Only `LevelApp.Core` geometry logic has meaningful test coverage.

### `ActivityLogger` is not tested
The replay infrastructure (`ReplayTests`, `ActivityReplayRunner`) depends on the JSONL format produced by `ActivityLogger`, but `ActivityLogger` itself has no unit tests verifying its serialization format.

### `ICalibrationWorkflow` interface defined but unused
`LevelApp.Core/Interfaces/ICalibrationWorkflow.cs` exists with no implementation. Residual interface from a planned feature.

## Technical Debt

### Parallel display modules have no shared rendering contract
`MeasurementsGridRenderer`, `StrategyPreviewRenderer`, `SurfacePlot3DDisplay`, and `ParallelWaysDisplay` each implement Win2D drawing independently with no shared base class or interface. Copy-paste is likely as more modules are added.

### `LevelApp.Instruments.Manual` has duplicate provider/plugin split
`ManualEntryPlugin.cs` and `ManualEntryProvider.cs` exist as separate files with overlapping responsibilities. The split is consistent with the plugin architecture but adds indirection for the simplest case.

### `IGeometryCalculator` / `IGeometryModule` / `IResultDisplay` interfaces exist in the old location
`LevelApp.Core/Interfaces/` still contains `IGeometryCalculator.cs`, `IGeometryModule.cs`, and `IResultDisplay.cs` from an older architecture. These appear unused in the current codebase and should be removed if confirmed dead.

## Future Complexity Risks

### No instrument plugin hot-loading
Plugins are registered at startup in `App.xaml.cs`. Adding a new hardware instrument requires a code change and recompile. A future plugin discovery mechanism (e.g., scanning a plugins folder) would require significant refactoring.

### `LevelApp.Updater` is a separate executable but not integrated
The standalone `LevelApp.Updater/Program.cs` exists for applying updates, but the handshake between `UpdateService` (download + launch) and the Updater process is not visible from the App codebase. This cross-process protocol is implicit and fragile to version drift.

### Localisation is partial
`ILocalisationService` / `LocalisationService` exist but string coverage is unknown. If strings are hardcoded in XAML alongside localised ones, internationalisation will be inconsistent.
