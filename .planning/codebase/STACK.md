# Technology Stack

**Analysis Date:** 2026-05-31

## Languages

**Primary:**
- C# (.NET 8) — all projects target `net8.0` or `net8.0-windows10.0.19041.0`

**Secondary:**
- XAML — WinUI 3 UI markup (`LevelApp.App/**/*.xaml`)
- XML — `.resw` resource files for localisation (`LevelApp.App/Strings/`)

## Runtime

**Environment:**
- .NET 8 (`net8.0` / `net8.0-windows10.0.19041.0`)
- Windows 10 build 19041 (min), Windows 11 supported
- Minimum Windows version: 10.0.17763.0 (Windows 10 1809)

**Package Manager:**
- NuGet (managed via `<PackageReference>` in `.csproj` files)
- Lockfile: not present (no `packages.lock.json`)
- Solution file: `LevelApp.slnx` (new XML-based solution format)

## Frameworks

**Core:**
- WinUI 3 / Windows App SDK 1.8.260317003 — UI framework (`LevelApp.App/LevelApp.App.csproj`)
- CommunityToolkit.Mvvm 8.3.2 — MVVM helpers (ObservableObject, RelayCommand, etc.) (`LevelApp.App/LevelApp.App.csproj`)
- Microsoft.Extensions.DependencyInjection 8.0.1 — DI container (`LevelApp.App/LevelApp.App.csproj`)

**Testing:**
- xUnit 2.5.3 — test runner (`LevelApp.Tests/LevelApp.Tests.csproj`)
- xunit.runner.visualstudio 2.5.3 — VS Test Explorer integration
- coverlet.collector 6.0.0 — code coverage collection
- Microsoft.NET.Test.Sdk 17.8.0 — test SDK host

**Build/Dev:**
- MSBuild via `dotnet build` — build toolchain
- `Microsoft.Windows.SDK.BuildTools` 10.0.26100.4654 — Windows SDK build tools (XAML compilation, PRI resources)
- Visual Studio 2022 — primary IDE

## Key Dependencies

**Critical:**
- `Microsoft.WindowsAppSDK` 1.8.260317003 — WinUI 3 runtime, Windows.Devices.* APIs (BLE, device enumeration, HID)
- `CommunityToolkit.Mvvm` 8.3.2 — source-generated ViewModels, commands, observable properties

**Infrastructure:**
- `System.Text.Json` (inbox with .NET 8) — `.levelproj` project file serialisation
- `System.IO.Compression` (inbox) — used by `LevelApp.Updater` to extract release zips
- `Windows.Devices.Bluetooth.Advertisement` (via WinRT, Windows App SDK) — BLE advertisement scanning
- `Windows.Devices.Enumeration` (via WinRT, Windows App SDK) — USB HID device enumeration
- P/Invoke to `WinUsb.dll` (system DLL) — raw USB control transfers for STM32 DFU

## Configuration

**Environment:**
- No `.env` file; no runtime environment variables used
- App settings persisted to user `ApplicationData` folder via `SettingsService` (`LevelApp.App/Services/SettingsService.cs`)
- Localisation via `.resw` resource files: `LevelApp.App/Strings/en-US/Resources.resw`, `LevelApp.App/Strings/de-DE/Resources.resw`

**Build:**
- `LevelApp.App/LevelApp.App.csproj` — main app project, platform `x64`, targets `win-x86;win-x64;win-arm64`
- `LevelApp.App/app.manifest` — Win32 application manifest
- `WindowsPackageType=None` — unpackaged (no MSIX), self-contained deployment
- `WindowsAppSdkSelfContained=true` — Windows App SDK runtime bundled in output
- `AllowUnsafeBlocks=true` — required for P/Invoke WinUSB interop in `LevelApp.Instruments.UsbHid`

## Version Source of Truth

- `LevelApp.Core/AppVersion.cs` — single source for Major/Minor/Patch constants
- Current version: `0.18.0`
- All `.csproj` `<Version>` fields kept in sync manually

## Platform Requirements

**Development:**
- Windows 10/11 with Visual Studio 2022
- .NET 8 SDK
- Windows App SDK 1.8

**Production:**
- Self-contained win-x64 publish (all .NET and WinAppSDK runtimes bundled)
- Minimum OS: Windows 10 build 17763 (October 2018 Update)
- No MSIX packaging; deployed as a folder of files

---

*Stack analysis: 2026-05-31*
