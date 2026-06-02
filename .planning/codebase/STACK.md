# Technology Stack

**Analysis Date:** 2026-06-02

LevelApp is a Windows-native desktop application. The stack is deliberately minimal — no web frameworks, no cross-platform abstractions. Every choice optimises for WinUI 3 ecosystem fit and zero-UI-dependency testability of the core logic.

---

## Languages

**Primary:**
- C# (.NET 8) — all projects; nullable reference types enabled everywhere; implicit usings enabled

**Secondary:**
- XAML — WinUI 3 UI markup (`LevelApp.App/**/*.xaml`)
- XML — `.resw` resource files for localisation (`LevelApp.App/Strings/`)

No scripting languages. CI build tooling is PowerShell (GitHub Actions runner).

---

## Runtime

**Environment:**
- .NET 8 (`net8.0` for `LevelApp.Core`, `LevelApp.Instruments.Manual`, and `LevelApp.Updater`)
- `net8.0-windows10.0.19041.0` for `LevelApp.App`, `LevelApp.Tests`, `LevelApp.Instruments.BLE`, `LevelApp.Instruments.UsbHid`
- Windows-targeted TFM is required by BLE/USB HID projects to access WinRT APIs built into the Windows SDK — no `Microsoft.Windows.SDK.Contracts` NuGet is used (incompatible with .NET 5+)
- Minimum Windows version: `10.0.17763.0` (Windows 10 October 2018 Update / build 1809)

**Architecture targets:**
- `LevelApp.App`: `x86`, `x64`, `arm64` (`<Platforms>` + `<RuntimeIdentifiers>` in `.csproj`)
- CI publish: `win-x64` self-contained only

**Package Manager:**
- NuGet (managed via `<PackageReference>` in `.csproj` files)
- No lock file (`packages.lock.json` not committed)
- Solution file: `LevelApp.slnx` (new Visual Studio XML-based solution format, not `.sln`)

---

## Frameworks

**UI:**
- WinUI 3 / Windows App SDK `1.8.260317003` — modern Windows-native UI, Fluent Design, XAML (`LevelApp.App/LevelApp.App.csproj`)
- App is **unpackaged** (`WindowsPackageType=None`, no MSIX), deployed as a plain folder of files
- `WindowsAppSdkSelfContained=true` bundles WinAppSDK native DLLs into the publish output
- XAML compiled to `.xbf` at build time; a custom `CopyWinUIAssetsToPublish` MSBuild target copies `.xbf` files and the PRI resource file into the publish folder (skipped by `dotnet publish` by default for unpackaged apps)

**MVVM helpers:**
- CommunityToolkit.Mvvm `8.3.2` — source-generated `[ObservableProperty]`, `[RelayCommand]`, `ObservableObject` (`LevelApp.App/LevelApp.App.csproj`)
- All ViewModels inherit `ViewModelBase` which inherits `ObservableObject` (`LevelApp.App/ViewModels/ViewModelBase.cs`)

**Dependency injection:**
- Microsoft.Extensions.DependencyInjection `8.0.1` — container built in `App.xaml.cs` (`LevelApp.App/LevelApp.App.csproj`)
- ViewModels and services resolved via constructor injection
- `App.Services` static locator retained only at genuine composition-root call sites (XAML code-behind pages created by `Frame.Navigate`, `MainWindow` constructor block) — every such site carries an explanatory comment

**Build SDK:**
- Microsoft.Windows.SDK.BuildTools `10.0.26100.4654` — required for WinUI 3 XAML compilation and PRI resource generation (`LevelApp.App/LevelApp.App.csproj`)

---

## Testing Frameworks

- xunit `2.5.3` — test runner and assertion library (`LevelApp.Tests/LevelApp.Tests.csproj`)
- xunit.runner.visualstudio `2.5.3` — Visual Studio Test Explorer integration
- Microsoft.NET.Test.Sdk `17.8.0` — test host
- coverlet.collector `6.0.0` — coverage instrumentation (collected by CI pipeline)
- Test project targets `net8.0-windows10.0.19041.0` to be able to reference `LevelApp.Instruments.BLE` and `LevelApp.Instruments.UsbHid`
- `[assembly: InternalsVisibleTo("LevelApp.Tests")]` declared in `LevelApp.Instruments.UsbHid.csproj` — exposes `IUsbControlTransport` and `DfuSession(IUsbControlTransport)` for mock injection without real hardware

---

## Key Dependencies

**Critical (runtime):**
- `Microsoft.WindowsAppSDK` `1.8.260317003` — WinUI 3 XAML runtime, window management, WinRT wrappers (`LevelApp.App`)
- `CommunityToolkit.Mvvm` `8.3.2` — source-generated ViewModel/command boilerplate (`LevelApp.App`)
- `Microsoft.Extensions.DependencyInjection` `8.0.1` — DI container (`LevelApp.App`)

**WinRT APIs used directly (no NuGet — built into the Windows TFM):**
- `Windows.Devices.Bluetooth` + `Windows.Devices.Bluetooth.Advertisement` — BLE scanning, device connection, GATT sessions (`LevelApp.Instruments.BLE`)
- `Windows.Devices.HumanInterfaceDevice` — USB HID device access and input report events (`LevelApp.Instruments.UsbHid`)
- `Windows.Devices.Enumeration` — `DeviceInformation.FindAllAsync`, `DeviceWatcher` for USB HID discovery (`LevelApp.Instruments.UsbHid`)
- `Windows.ApplicationModel.Resources.ResourceLoader` — `.resw` localisation, `x:Uid` wiring (`LevelApp.App`)

**Standard BCL features relied upon (no NuGet):**
- `System.Text.Json` — `.levelproj` project file serialisation, settings.json, devices.json, activity log JSON Lines (`LevelApp.Core/Serialization/`, `LevelApp.App/Services/`)
- `System.Net.Http.HttpClient` — GitHub Releases API polling and update zip download (`LevelApp.App/Services/UpdateService.cs`); timeout set explicitly to 10 s
- `System.IO.Compression` — zip extraction in `LevelApp.Updater/Program.cs`
- P/Invoke to `WinUsb.dll` (system DLL, available on all Windows editions since Vista) — STM32 DFU USB control transfers (`LevelApp.Instruments.UsbHid/Dfu/Internal/WinUsbControlTransport.cs`)

**No ORM, no database engine, no third-party HTTP client, no third-party logging framework.**

---

## Configuration

**Environment:**
- No `.env` files; no environment-variable-based runtime configuration
- User preferences persisted to `%LOCALAPPDATA%\LevelApp\settings.json` by `SettingsService` (`LevelApp.App/Services/SettingsService.cs`)
- Device registry persisted to `%LOCALAPPDATA%\LevelApp\devices.json` by `DeviceRegistry` (`LevelApp.Core/Instruments/DeviceRegistry.cs`)
- Activity logs written to `%LOCALAPPDATA%\LevelApp\Logs\` as `.jsonl` + `.instrument` files by `ActivityLogger` (`LevelApp.App/Services/ActivityLogger.cs`)
- Updater logs written to `%TEMP%\LevelApp.Updater.log`

**Build:**
- `LevelApp.App/app.manifest` — Win32 application manifest (UAC, DPI awareness)
- `AllowUnsafeBlocks=true` in `LevelApp.App.csproj` — required for P/Invoke WinUSB interop
- No `Directory.Build.props` or shared MSBuild props file; each `.csproj` is fully self-contained

**Localisation:**
- `.resw` files: `LevelApp.App/Strings/en-US/Resources.resw` and `LevelApp.App/Strings/de-DE/Resources.resw` (195 keys each)
- Declared as `<PRIResource>` in `LevelApp.App.csproj`; compiled to PRI at build time
- XAML uses `x:Uid` for automatic string binding; C# uses `ResourceLoader.GetString` via `ILocalisationService`

---

## Version Source of Truth

- `LevelApp.Core/AppVersion.cs` — single source for `Major`, `Minor`, `Patch` integer constants
- Current version: `0.19.0`
- All `.csproj` `<Version>`, `<AssemblyVersion>`, `<FileVersion>` fields kept in sync manually with `AppVersion.cs`
- Never hardcode a version string anywhere else — not in XAML, not in C#, not in comments

---

## Platform Requirements

**Development:**
- Visual Studio 2022 (Community or higher) with WinUI 3 / Windows App SDK workload
- .NET 8 SDK
- Windows 10 or 11 (ARM64 or x64)

**Production:**
- Windows 10 `10.0.17763.0` minimum (October 2018 Update)
- `win-x64` self-contained publish; no .NET runtime installation required on target machine
- Must be installed to a user-writable directory (not `Program Files`) — the auto-updater requires write access to the install folder; elevation support deferred

---

*Stack analysis: 2026-06-02*
