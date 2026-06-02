# Codebase Structure

**Analysis Date:** 2026-06-02

## Directory Layout

```
LevelApp/                                    # Solution root
├── LevelApp.slnx                            # Solution file (new VS format)
│
├── LevelApp.Core/                           # UI-free domain library (net8.0)
│   ├── AppVersion.cs                        # Single source of truth: Major.Minor.Patch
│   ├── Interfaces/                          # All Core contracts
│   │   ├── IMeasurementStrategy.cs
│   │   ├── ISurfaceCalculator.cs
│   │   ├── IActivityLogger.cs
│   │   ├── IInstrumentProvider.cs
│   │   ├── ITransport.cs
│   │   ├── IDeviceScanner.cs
│   │   ├── IDeviceRegistry.cs
│   │   ├── IFirmwareUpdater.cs
│   │   ├── ICalibrationWorkflow.cs
│   │   └── IInstrumentPlugin.cs
│   ├── Models/                              # Project data model (POCO classes)
│   │   ├── Project.cs
│   │   ├── ObjectDefinition.cs
│   │   ├── MeasurementSession.cs
│   │   ├── MeasurementRound.cs
│   │   ├── MeasurementStep.cs
│   │   ├── CorrectionRound.cs
│   │   ├── SurfaceResult.cs
│   │   ├── CalculationParameters.cs
│   │   ├── InstrumentReading.cs
│   │   ├── RailDefinition.cs
│   │   ├── ParallelWaysParameters.cs
│   │   ├── ParallelWaysTask.cs
│   │   ├── ParallelWaysStrategyParameters.cs
│   │   └── ParallelWaysResult.cs
│   ├── Instruments/                         # Enums, value types, DeviceRegistry impl
│   │   ├── InstrumentConnectionState.cs
│   │   ├── InstrumentCapabilities.cs
│   │   ├── TransportCapabilities.cs
│   │   ├── TransportRequirement.cs
│   │   ├── KnownDevice.cs
│   │   ├── FirmwareInfo.cs
│   │   ├── DeviceCandidate.cs
│   │   └── DeviceRegistry.cs
│   ├── Geometry/                            # Strategies and calculators
│   │   ├── StrategyFactory.cs               # Create(strategyId) → IMeasurementStrategy
│   │   ├── CalculatorFactory.cs             # Create(methodId, strategy) → ISurfaceCalculator
│   │   ├── Calculators/
│   │   │   ├── LeastSquaresCalculator.cs
│   │   │   ├── SequentialIntegrationCalculator.cs
│   │   │   └── ClosureErrorCalculator.cs
│   │   ├── SurfacePlate/
│   │   │   └── Strategies/
│   │   │       ├── FullGridStrategy.cs
│   │   │       ├── UnionJackStrategy.cs
│   │   │       └── UnionJackRings.cs
│   │   └── ParallelWays/
│   │       ├── ParallelWaysCalculator.cs    # Standalone — not ISurfaceCalculator
│   │       └── Strategies/
│   │           └── ParallelWaysStrategy.cs
│   └── Serialization/
│       ├── ProjectSerializer.cs
│       ├── ObjectValueConverter.cs
│       └── OrientationConverter.cs
│
├── LevelApp.Instruments.Manual/             # Manual-entry plugin (net8.0)
│   ├── ManualTransport.cs                   # ITransport, TransportId="manual"
│   ├── ManualEntryScanner.cs                # IDeviceScanner (yields nothing)
│   ├── ManualEntryProvider.cs               # IInstrumentProvider (always Connected)
│   └── ManualEntryPlugin.cs                 # IInstrumentPlugin root; registered in DI
│
├── LevelApp.Instruments.BLE/                # BLE infrastructure (net8.0-windows10.0.19041.0)
│   ├── BleTransport.cs                      # ITransport, TransportId="ble"
│   ├── BleDeviceScanner.cs                  # IDeviceScanner via BluetoothLEAdvertisementWatcher
│   ├── BleInstrumentProviderBase.cs         # Abstract IInstrumentProvider; backoff reconnect
│   └── Internal/
│       └── BleConnectionManager.cs          # BluetoothLEDevice + GattSession lifetime
│
├── LevelApp.Instruments.UsbHid/             # USB HID infrastructure + DFU (net8.0-windows10.0.19041.0)
│   ├── UsbHidTransport.cs                   # ITransport, TransportId="usb-hid"
│   ├── UsbHidDeviceScanner.cs               # IDeviceScanner; FindAllAsync + DeviceWatcher
│   ├── UsbHidInstrumentProviderBase.cs      # Abstract IInstrumentProvider; HidDevice
│   └── Dfu/
│       ├── DfuConnectionDetector.cs         # Waits for WinUSB device with DFU PID
│       ├── DfuSession.cs                    # STM32 DFU download via P/Invoke WinUsb.dll
│       └── Internal/
│           ├── IUsbControlTransport.cs      # Testable abstraction over USB control transfers
│           └── WinUsbControlTransport.cs    # P/Invoke: WinUsb_Initialize, ControlTransfer, Free
│
├── LevelApp.App/                            # WinUI 3 executable (net8.0-windows10.0.19041.0)
│   ├── App.xaml / App.xaml.cs              # DI container setup; OnLaunched; unhandled exception hooks
│   ├── MainWindow.xaml / .cs               # Shell window; menu bar; IWindowContext setup
│   ├── app.manifest                        # Windows app manifest
│   ├── Assets/
│   │   └── levelapp.ico                    # Window and taskbar icon
│   ├── Helpers/
│   │   └── ThemeHelper.cs                  # GetColor, GetBrush, GetPlotRamp, InterpolateRamp
│   ├── Strings/
│   │   ├── en-US/Resources.resw            # 195 English UI string keys
│   │   └── de-DE/Resources.resw            # 195 German UI string keys
│   ├── Styles/
│   │   ├── ThemeColors.xaml                # ThemeDictionaries: all colour tokens
│   │   ├── TextStyles.xaml                 # Named TextBlock styles
│   │   ├── ControlStyles.xaml              # Button, CardStyle, CompactCardStyle
│   │   └── HelpButtonStyle.xaml            # ⓘ info button style
│   ├── Navigation/
│   │   ├── PageKey.cs                      # Enum: ProjectSetup, Measurement, Results, Correction, Instruments
│   │   ├── INavigationService.cs
│   │   ├── NavigationService.cs            # Frame-based; PageKey→Type mapping
│   │   ├── MeasurementArgs.cs
│   │   ├── ResultsArgs.cs
│   │   └── CorrectionArgs.cs
│   ├── Services/
│   │   ├── IProjectFileService.cs / ProjectFileService.cs   # Win32 COM file dialogs + JSON I/O
│   │   ├── ISettingsService.cs / SettingsService.cs         # %LOCALAPPDATA%\LevelApp\settings.json
│   │   ├── ActivityLogger.cs                                # .jsonl + .instrument to Logs/
│   │   ├── IThemeService.cs / ThemeService.cs               # Apply(ElementTheme), SetTarget
│   │   ├── ILocalisationService.cs / LocalisationService.cs # ResourceLoader wrapper
│   │   ├── IUpdateService.cs / UpdateService.cs             # GitHub Releases API; Timeout=10s
│   │   ├── IWindowContext.cs / WindowContext.cs             # XamlRoot?, Hwnd for dialogs
│   │   └── UpdaterContract.cs                               # Cross-process argument constants
│   ├── Converters/
│   │   └── BoolToVisibilityConverter.cs
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs                # Inherits ObservableObject
│   │   ├── MainViewModel.cs                # Singleton; owns Project state, dirty flag, title
│   │   ├── ProjectSetupViewModel.cs
│   │   ├── MeasurementViewModel.cs         # Injects plugins + registry; connection status
│   │   ├── ResultsViewModel.cs
│   │   ├── CorrectionViewModel.cs
│   │   ├── FlaggedStepItem.cs              # Display DTO
│   │   ├── InstrumentsViewModel.cs         # Builds tab VM list; RegistryWarning
│   │   ├── InstrumentPluginTabViewModel.cs # Per-plugin tab; scan/calibrate/update commands
│   │   └── KnownDeviceViewModel.cs         # Display wrapper for KnownDevice
│   ├── Views/
│   │   ├── ProjectSetupView.xaml / .cs
│   │   ├── MeasurementView.xaml / .cs      # ConnectionStatusBar InfoBar
│   │   ├── ResultsView.xaml / .cs
│   │   ├── CorrectionView.xaml / .cs
│   │   ├── InstrumentsPage.xaml / .cs      # TabView; one tab per IInstrumentPlugin
│   │   ├── InstrumentPluginTabView.xaml / .cs  # UserControl: device list + buttons
│   │   └── Dialogs/
│   │       ├── PreferencesDialog.xaml / .cs
│   │       ├── NewMeasurementDialog.xaml / .cs
│   │       ├── RecalculateDialog.xaml / .cs
│   │       ├── AboutDialog.xaml / .cs
│   │       ├── UpdateDialog.xaml / .cs     # Launches LevelApp.Updater
│   │       ├── ScanForDevicesDialog.xaml / .cs
│   │       └── FirmwareUpdateDialog.xaml / .cs
│   └── DisplayModules/                     # Static canvas renderer classes
│       ├── SurfacePlot3D/
│       │   └── SurfacePlot3DDisplay.cs
│       ├── MeasurementsGrid/
│       │   └── MeasurementsGridRenderer.cs
│       ├── StrategyPreview/
│       │   └── StrategyPreviewRenderer.cs
│       └── ParallelWaysDisplay/
│           └── ParallelWaysDisplay.cs
│
├── LevelApp.Tests/                          # xUnit tests (net8.0-windows10.0.19041.0)
│   ├── FullGridStrategyTests.cs
│   ├── UnionJackStrategyTests.cs
│   ├── SurfacePlateCalculatorTests.cs
│   ├── SequentialIntegrationCalculatorTests.cs
│   ├── CorrectionRoundTests.cs
│   ├── ParallelWaysStrategyTests.cs
│   ├── ParallelWaysCalculatorTests.cs
│   ├── ProjectReplayTests.cs                # [Theory] over docs/sampleProjects/*.levelproj
│   ├── InstrumentProviderTests.cs
│   ├── PluginArchitectureTests.cs
│   ├── UpdateServiceTests.cs
│   ├── DeviceRegistryTests.cs
│   ├── BLE/
│   │   ├── BleTransportTests.cs
│   │   └── BleInstrumentProviderBaseTests.cs
│   ├── UsbHid/
│   │   ├── UsbHidTransportTests.cs
│   │   ├── UsbHidDeviceScannerTests.cs
│   │   └── DfuSessionTests.cs
│   ├── Replay/
│   │   ├── IReplayTarget.cs
│   │   ├── EndOfRecordingException.cs
│   │   ├── RecordedInstrumentProvider.cs
│   │   ├── ActivityReplayRunner.cs
│   │   └── ReplayTests.cs                  # [Theory] over TestLogs/*.jsonl
│   └── TestLogs/
│       └── .gitkeep                        # Place session bundles here for replay tests
│
├── LevelApp.Updater/                        # Standalone updater (net8.0, no UI TFM)
│   ├── Program.cs                          # Copy-to-temp extract + relaunch logic
│   └── UpdaterContract.cs                  # Argument-position constants (sync with App copy)
│
└── docs/
    ├── architecture.md                      # Full design reference
    ├── levelproj.md                         # .levelproj JSON format reference
    ├── workpackages/                        # One .md per work package
    └── sampleProjects/                      # .levelproj files used by ProjectReplayTests
```

## Directory Purposes

**`LevelApp.Core/Interfaces/`:**
- Purpose: All domain contracts — geometry, instrument, logging
- All new interfaces for domain capabilities belong here
- Key files: all `I*.cs` files listed above

**`LevelApp.Core/Models/`:**
- Purpose: POCO data model classes; no logic except the `MergeWithReplacements` static helper on `MeasurementRound`
- All new project data fields belong in existing or new model files here

**`LevelApp.Core/Geometry/`:**
- Purpose: Strategy and calculator implementations; the two factory classes
- Sub-structure: `Calculators/` for `ISurfaceCalculator` impls; `SurfacePlate/Strategies/` and `ParallelWays/Strategies/` for `IMeasurementStrategy` impls; `ParallelWays/` for the standalone calculator
- Adding a new strategy or calculator: create the class here + one line in `StrategyFactory.cs` or `CalculatorFactory.cs`

**`LevelApp.Core/Instruments/`:**
- Purpose: Instrument-related enums, value-type records (`KnownDevice`, `FirmwareInfo`, `DeviceCandidate`), and `DeviceRegistry` implementation
- Key: `DeviceRegistry.cs` is a `LevelApp.Core` class (no UI) registered as `IDeviceRegistry` in App's DI

**`LevelApp.App/Services/`:**
- Purpose: All application-level services with their interfaces
- Every service in this directory has a corresponding `I*.cs` interface
- Persistence: `settings.json` → `SettingsService`; `devices.json` → `DeviceRegistry` (Core); file I/O → `ProjectFileService`

**`LevelApp.App/ViewModels/`:**
- Purpose: One ViewModel per navigable page + `MainViewModel` (shell singleton) + display-only DTOs
- All page ViewModels inherit `ViewModelBase` and are registered as Transient in DI
- `MainViewModel` is Singleton; injected into page ViewModels so they share project state

**`LevelApp.App/Views/`:**
- Purpose: XAML Views and their code-behind files
- Code-behind is minimal — only navigation `OnNavigatedTo` init, `ActualThemeChanged` re-render, and dialog instantiation
- Business logic must never live in code-behind

**`LevelApp.App/Views/Dialogs/`:**
- Purpose: `ContentDialog` XAML files for modal UI (preferences, about, scan for devices, firmware update, update, new measurement, recalculate)

**`LevelApp.App/DisplayModules/`:**
- Purpose: Static Canvas renderer classes
- One subdirectory per module; each contains a single `.cs` file
- Key constraint: do NOT add a fifth module without first defining `IDisplayModule`

**`LevelApp.App/Styles/`:**
- Purpose: XAML `ResourceDictionary` files merged in `App.xaml`
- `ThemeColors.xaml` is the authoritative source for all color tokens

**`LevelApp.App/Strings/`:**
- Purpose: `.resw` localisation resource files
- `en-US/Resources.resw` is the primary file; `de-DE/Resources.resw` mirrors it

**`LevelApp.Tests/Replay/`:**
- Purpose: Infrastructure for session replay testing against activity log files (`.jsonl` + `.instrument`)
- `TestLogs/` is committed empty; populate manually for replay tests

## Key File Locations

**Entry Points:**
- `LevelApp.App/App.xaml.cs` — DI composition root; `OnLaunched`; exception hooks
- `LevelApp.App/MainWindow.xaml.cs` — Window composition root; service wiring; navigation start
- `LevelApp.Updater/Program.cs` — Standalone updater entry point

**Versioning:**
- `LevelApp.Core/AppVersion.cs` — Only place version numbers live
- `LevelApp.App/LevelApp.App.csproj` — `<Version>`, `<AssemblyVersion>`, `<FileVersion>` must match `AppVersion.cs`

**Core Contracts:**
- `LevelApp.Core/Interfaces/` — All interface definitions
- `LevelApp.Core/Geometry/StrategyFactory.cs` — Strategy registration
- `LevelApp.Core/Geometry/CalculatorFactory.cs` — Calculator registration

**DI Registration:**
- `LevelApp.App/App.xaml.cs` — `BuildServiceProvider()` method

**Persistence:**
- `LevelApp.Core/Serialization/ProjectSerializer.cs` — JSON serialize/deserialize
- `LevelApp.App/Services/ProjectFileService.cs` — File I/O with Win32 COM dialogs
- `LevelApp.App/Services/SettingsService.cs` — User preferences → `%LOCALAPPDATA%\LevelApp\settings.json`
- `LevelApp.Core/Instruments/DeviceRegistry.cs` — Known devices → `%LOCALAPPDATA%\LevelApp\devices.json`

**Update Contract:**
- `LevelApp.App/Services/UpdaterContract.cs` — Must stay in sync with:
- `LevelApp.Updater/UpdaterContract.cs` — (verbatim duplicate; sync comment in both files)

## Naming Conventions

**Projects:**
- Pattern: `LevelApp.<Layer>` or `LevelApp.<Layer>.<Transport>`
- Examples: `LevelApp.Core`, `LevelApp.App`, `LevelApp.Instruments.Manual`, `LevelApp.Instruments.BLE`

**Namespaces:**
- Match project name: `LevelApp.Core.Models`, `LevelApp.App.ViewModels`, `LevelApp.App.Services`
- Sub-namespaces follow folder names: `LevelApp.Core.Geometry.Calculators`, `LevelApp.App.Views.Dialogs`

**Files:**
- Interfaces: `I` prefix — `INavigationService.cs`, `IMeasurementStrategy.cs`
- ViewModels: `*ViewModel.cs` suffix — `MeasurementViewModel.cs`, `MainViewModel.cs`
- Views: `*View.xaml` or `*Page.xaml` or `*Dialog.xaml`
- Services: `*Service.cs` — `ProjectFileService.cs`, `ThemeService.cs`
- Factories: `*Factory.cs` — `StrategyFactory.cs`, `CalculatorFactory.cs`
- Display modules: `*Display.cs` or `*Renderer.cs`

**Classes:**
- Plugins: `*Plugin.cs` — `ManualEntryPlugin.cs`
- Providers: `*Provider.cs` or `*ProviderBase.cs` — `ManualEntryProvider.cs`, `BleInstrumentProviderBase.cs`
- Scanners: `*Scanner.cs` — `ManualEntryScanner.cs`, `BleDeviceScanner.cs`
- Transports: `*Transport.cs` — `ManualTransport.cs`, `BleTransport.cs`
- Args/records: `*Args.cs` or descriptive name — `MeasurementArgs.cs`, `KnownDevice.cs`

**Navigation args:** Named records in `LevelApp.App/Navigation/` — `MeasurementArgs`, `ResultsArgs`, `CorrectionArgs`

## Where to Add New Code

**New geometry strategy (e.g., straightness):**
- Implement `IMeasurementStrategy` in `LevelApp.Core/Geometry/<ModuleName>/Strategies/<Name>Strategy.cs`
- Register: one line in `LevelApp.Core/Geometry/StrategyFactory.cs`
- Tests: `LevelApp.Tests/<Name>StrategyTests.cs`

**New surface calculator algorithm:**
- Implement `ISurfaceCalculator` in `LevelApp.Core/Geometry/Calculators/<Name>Calculator.cs`
- Register: one line in `LevelApp.Core/Geometry/CalculatorFactory.cs`
- Tests: `LevelApp.Tests/SurfacePlateCalculatorTests.cs` or a new `<Name>CalculatorTests.cs`

**New application service:**
- Interface: `LevelApp.App/Services/I<Name>Service.cs`
- Implementation: `LevelApp.App/Services/<Name>Service.cs`
- Register as singleton in `App.xaml.cs` `BuildServiceProvider()`
- Inject via constructor into ViewModels that need it

**New page/view:**
- ViewModel: `LevelApp.App/ViewModels/<Name>ViewModel.cs` (inherit `ViewModelBase`)
- View: `LevelApp.App/Views/<Name>View.xaml` + `<Name>View.xaml.cs`
- Add to `PageKey` enum: `LevelApp.App/Navigation/PageKey.cs`
- Add to `NavigationService` page map: `LevelApp.App/Navigation/NavigationService.cs`
- Register as Transient ViewModel in `App.xaml.cs`

**New dialog:**
- `LevelApp.App/Views/Dialogs/<Name>Dialog.xaml` + `.cs`
- Instantiate in the View code-behind (inject dependencies via constructor, not `App.Services`)

**New display module (renderer):**
- STOP — define `IDisplayModule` first (see technical debt)
- After interface exists: `LevelApp.App/DisplayModules/<Name>/<Name>Display.cs`

**New instrument hardware plugin:**
- New project (e.g., `LevelApp.Instruments.<Name>`) referencing appropriate transport project
- Implement: `<Name>Plugin.cs` (IInstrumentPlugin), `<Name>Provider.cs` (subclass transport base)
- Register: `services.AddSingleton<IInstrumentPlugin, <Name>Plugin>()` in `App.xaml.cs`
- Tests: `LevelApp.Tests/<Transport>/<Name>Tests.cs`

**New Core model field:**
- Add property to appropriate class in `LevelApp.Core/Models/`
- If it changes the `.levelproj` format: increment `ProjectSerializer.CurrentSchemaVersion` and update `docs/levelproj.md`

## Special Directories

**`.planning/codebase/`:**
- Purpose: Auto-generated codebase analysis documents for GSD tooling
- Generated: Yes (by Claude agents)
- Committed: Yes

**`docs/sampleProjects/`:**
- Purpose: `.levelproj` files used by `ProjectReplayTests` as `[Theory]` data
- Generated: No (hand-crafted)
- Committed: Yes

**`docs/workpackages/`:**
- Purpose: Work package specification files (`WP0.XX-<name>.md`)
- Generated: No
- Committed: Yes

**`LevelApp.Tests/TestLogs/`:**
- Purpose: Session log bundles (`.jsonl` + `.instrument`) for replay tests
- Generated: No (populated manually from real sessions)
- Committed: Only `.gitkeep` is committed; actual log files are added manually

**`.github/workflows/`:**
- Purpose: GitHub Actions CI pipeline (`ci.yml`) — build → test → publish → package → release
- Generated: No
- Committed: Yes

---

*Structure analysis: 2026-06-02*
