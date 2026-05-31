# Structure

## Solution Layout

```
LevelApp/
├── LevelApp.sln
│
├── LevelApp.Core/                        # Pure domain logic — no UI, no hardware deps
│   ├── AppVersion.cs                     # Single source of truth for version numbers
│   ├── Interfaces/                       # All core contracts (IInstrumentPlugin, ITransport, etc.)
│   ├── Models/                           # Domain models (Project, MeasurementSession, etc.)
│   ├── Geometry/
│   │   ├── SurfacePlate/
│   │   │   ├── Strategies/               # IMeasurementStrategy implementations
│   │   │   └── (SurfacePlateCalculator moved to Calculators/)
│   │   ├── ParallelWays/
│   │   │   ├── Strategies/               # ParallelWaysStrategy
│   │   │   └── ParallelWaysCalculator.cs
│   │   ├── Calculators/                  # SequentialIntegrationCalculator, LeastSquaresCalculator, ClosureErrorCalculator
│   │   ├── CalculatorFactory.cs
│   │   └── StrategyFactory.cs
│   ├── Instruments/                      # Instrument abstractions (DeviceRegistry, KnownDevice, etc.)
│   └── Serialization/                    # JSON converters for .levelproj format
│
├── LevelApp.Instruments.Manual/          # Manual keyboard-entry instrument plugin
│   ├── ManualEntryPlugin.cs              # IInstrumentPlugin registration entry point
│   ├── ManualEntryProvider.cs            # IInstrumentProvider
│   ├── ManualEntryScanner.cs             # IDeviceScanner
│   └── ManualTransport.cs               # ITransport
│
├── LevelApp.Instruments.BLE/             # Bluetooth LE transport + plugin base
│   ├── BleInstrumentProviderBase.cs      # Abstract base for BLE instrument providers
│   ├── BleDeviceScanner.cs              # IDeviceScanner via Windows BLE APIs
│   ├── BleTransport.cs                  # ITransport over GATT
│   └── Internal/
│       └── BleConnectionManager.cs
│
├── LevelApp.Instruments.UsbHid/          # USB HID transport + DFU subsystem
│   ├── UsbHidInstrumentProviderBase.cs
│   ├── UsbHidDeviceScanner.cs
│   ├── UsbHidTransport.cs               # ITransport over HID
│   └── Dfu/
│       ├── DfuSession.cs                # Firmware update state machine
│       ├── DfuConnectionDetector.cs
│       └── Internal/
│           ├── IUsbControlTransport.cs
│           └── WinUsbControlTransport.cs
│
├── LevelApp.App/                         # WinUI 3 application
│   ├── App.xaml / App.xaml.cs           # Application entry point, DI wiring
│   ├── MainWindow.xaml                  # Shell window with navigation frame
│   ├── Views/
│   │   ├── ProjectSetupView.xaml        # Step 1: configure project
│   │   ├── MeasurementView.xaml         # Step 2: guided measurement
│   │   ├── CorrectionView.xaml          # Step 3: suspect reading correction
│   │   ├── ResultsView.xaml             # Step 4: results display
│   │   ├── InstrumentsPage.xaml         # Instrument management page
│   │   ├── InstrumentPluginTabView.xaml # Per-plugin tab in InstrumentsPage
│   │   └── Dialogs/
│   │       ├── AboutDialog.xaml
│   │       ├── FirmwareUpdateDialog.xaml
│   │       ├── NewMeasurementDialog.xaml
│   │       ├── PreferencesDialog.xaml
│   │       ├── RecalculateDialog.xaml
│   │       ├── ScanForDevicesDialog.xaml
│   │       └── UpdateDialog.xaml
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs             # INotifyPropertyChanged base
│   │   ├── MainViewModel.cs             # Top-level navigation + project state
│   │   ├── ProjectSetupViewModel.cs
│   │   ├── MeasurementViewModel.cs
│   │   ├── CorrectionViewModel.cs
│   │   ├── ResultsViewModel.cs
│   │   ├── InstrumentsViewModel.cs      # Instrument management
│   │   ├── InstrumentPluginTabViewModel.cs
│   │   ├── KnownDeviceViewModel.cs
│   │   └── FlaggedStepItem.cs           # Display model for suspect readings
│   ├── Services/
│   │   ├── IProjectFileService.cs / ProjectFileService.cs  # .levelproj open/save
│   │   ├── ISettingsService.cs / SettingsService.cs        # App settings persistence
│   │   ├── IThemeService.cs / ThemeService.cs              # Light/dark theme
│   │   ├── ILocalisationService.cs / LocalisationService.cs
│   │   ├── IUpdateService.cs / UpdateService.cs            # GitHub release checking
│   │   └── ActivityLogger.cs                               # JSONL activity log for replay tests
│   ├── Navigation/
│   │   ├── INavigationService.cs / NavigationService.cs
│   │   ├── PageKey.cs                   # Enum of navigable pages
│   │   └── *Args.cs                     # Typed navigation arguments
│   ├── DisplayModules/
│   │   ├── MeasurementsGrid/            # Win2D grid renderer
│   │   ├── StrategyPreview/             # Strategy diagram renderer
│   │   ├── SurfacePlot3D/               # 3D surface plot
│   │   └── ParallelWaysDisplay/         # Parallel ways visualization
│   ├── Converters/                      # XAML value converters
│   ├── Helpers/
│   │   └── ThemeHelper.cs
│   └── Styles/
│       ├── ThemeColors.xaml
│       ├── TextStyles.xaml
│       ├── ControlStyles.xaml
│       └── HelpButtonStyle.xaml
│
├── LevelApp.Updater/                     # Standalone updater executable
│   └── Program.cs
│
└── LevelApp.Tests/                       # xUnit test project
    ├── BLE/
    ├── UsbHid/
    ├── Replay/                           # ActivityReplayRunner infrastructure
    └── TestLogs/                         # .jsonl activity log files
```

## Key Files

| File | Purpose |
|------|---------|
| `LevelApp.Core/AppVersion.cs` | **Only** place to edit the version number |
| `LevelApp.Core/Interfaces/IInstrumentPlugin.cs` | Contract all instrument plugins must implement |
| `LevelApp.Core/Interfaces/ITransport.cs` | Hardware transport abstraction |
| `LevelApp.Core/Instruments/DeviceRegistry.cs` | Plugin registration and device lookup |
| `LevelApp.App/App.xaml.cs` | DI container configuration, plugin registration |
| `LevelApp.App/Services/ActivityLogger.cs` | Writes JSONL logs consumed by ReplayTests |

## Where to Add New Code

| What you're adding | Where it goes |
|--------------------|--------------|
| New measurement strategy | `LevelApp.Core/Geometry/<Module>/Strategies/` + register in `StrategyFactory` |
| New geometry calculator | `LevelApp.Core/Geometry/Calculators/` + register in `CalculatorFactory` |
| New instrument plugin (new hardware) | New `LevelApp.Instruments.<Name>/` project implementing `IInstrumentPlugin` |
| New app service | Interface in `LevelApp.App/Services/I<Name>Service.cs`, impl alongside, register in `App.xaml.cs` |
| New view/page | `LevelApp.App/Views/` (XAML + code-behind), VM in `ViewModels/`, add `PageKey` enum value |
| New dialog | `LevelApp.App/Views/Dialogs/` |
| New domain model | `LevelApp.Core/Models/` |
| New core interface | `LevelApp.Core/Interfaces/` |
| New XAML resource | `LevelApp.App/Styles/` or merge into existing resource dictionary |
