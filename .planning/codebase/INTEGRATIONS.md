# External Integrations

**Analysis Date:** 2026-05-31

## Hardware Transport Layers

LevelApp communicates with physical measurement instruments through two transport plugins, both implementing `LevelApp.Core.Interfaces.ITransport` and `IDeviceScanner`.

**USB HID (`LevelApp.Instruments.UsbHid`):**
- Transport ID: `"usb-hid"` — `LevelApp.Instruments.UsbHid/UsbHidTransport.cs`
- Scanner: `LevelApp.Instruments.UsbHid/UsbHidDeviceScanner.cs`
  - Uses `Windows.Devices.Enumeration.DeviceWatcher` (WinRT API via Windows App SDK)
  - Filters by Vendor ID (VID) and one or more Product IDs (PID)
  - HID interface GUID: `{4D1E55B2-F16F-11CF-88CB-001111000030}`
  - Yields currently-connected devices first, then watches for new arrivals
- Provider base: `LevelApp.Instruments.UsbHid/UsbHidInstrumentProviderBase.cs`
- Capabilities: `SingleReading | ContinuousStream | Bidirectional`

**Bluetooth Low Energy (`LevelApp.Instruments.BLE`):**
- Transport ID: `"ble"` — `LevelApp.Instruments.BLE/BleTransport.cs`
- Scanner: `LevelApp.Instruments.BLE/BleDeviceScanner.cs`
  - Uses `Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher` (WinRT)
  - Filters by GATT service UUIDs; pass empty array to receive all advertisements
  - Each Bluetooth address reported at most once per scan session
- Provider base: `LevelApp.Instruments.BLE/BleInstrumentProviderBase.cs`
- Connection manager: `LevelApp.Instruments.BLE/Internal/BleConnectionManager.cs`
- Capabilities: `SingleReading | ContinuousStream | Bidirectional`

**Manual Entry (`LevelApp.Instruments.Manual`):**
- No hardware transport; operator enters readings by hand
- Plugin: `LevelApp.Instruments.Manual/` — targets plain `net8.0` (no Windows-specific APIs)

## DFU — Device Firmware Update Subsystem

Target: STM32 microcontrollers in measurement instruments (USB DFU class protocol).

- Protocol files: `LevelApp.Instruments.UsbHid/Dfu/`
  - `DfuSession.cs` — executes a firmware download (host → device) over USB control transfers
  - `DfuConnectionDetector.cs` — waits for a DFU-mode USB device to appear after reboot
  - `LevelApp.Instruments.UsbHid/Dfu/Internal/IUsbControlTransport.cs` — testable interface
  - `LevelApp.Instruments.UsbHid/Dfu/Internal/WinUsbControlTransport.cs` — production P/Invoke implementation

**USB access approach:**
- Uses P/Invoke to `WinUsb.dll` (system DLL, available on all Windows editions since Vista)
- WinRT `Windows.Devices.Usb.UsbDevice` was rejected: requires `usbDevice` capability in a packaged manifest, which is unavailable for this unpackaged app
- `WinUsb.dll` surface used: `WinUsb_Initialize`, `WinUsb_ControlTransfer`, `WinUsb_Free`
- `AllowUnsafeBlocks=true` is set in `LevelApp.Instruments.UsbHid/LevelApp.Instruments.UsbHid.csproj`

**DFU protocol sequence:**
1. For each firmware page (default 2048 bytes): send `DFU_DNLOAD`, poll `DFU_GETSTATUS` until `dfuDNLOAD-IDLE` (state 5)
2. After last page: send zero-length `DFU_DNLOAD` to trigger manifestation, poll until `dfuIDLE` (state 2) or `dfuMANIFEST-WAIT-RESET` (state 8)
3. Device reboots; caller waits for normal-mode HID device to reappear

**Firmware updater interface:** `LevelApp.Core/Interfaces/IFirmwareUpdater.cs`

## Auto-Update (GitHub Releases)

- Service: `LevelApp.App/Services/UpdateService.cs` (implements `IUpdateService`)
- API endpoint: `https://api.github.com/repos/soldernerd/LevelApp/releases/latest`
- HTTP client: `System.Net.Http.HttpClient` with `User-Agent: LevelApp/{version}`
- Checks if the remote release tag version is newer than `AppVersion.Full`
- Downloads the `.zip` asset from `browser_download_url` with streaming progress reporting
- Interface: `LevelApp.App/Services/IUpdateService.cs`

**Update installer (`LevelApp.Updater`):**
- Separate self-contained executable: `LevelApp.Updater/Program.cs`
- Launched by the main app after download completes
- Copies itself to `%TEMP%` first (so the install folder is fully unlocked), then re-launches with `--from-temp`
- Waits up to 10 seconds for the main app to exit, extracts the zip over the install folder, deletes the zip, relaunches the new version
- No NuGet dependencies; uses only inbox BCL (`System.IO.Compression`, `System.Diagnostics.Process`)
- Published as a single-file `win-x64` executable (`-p:PublishSingleFile=true`) alongside `LevelApp.App.exe`

## File Format Integration — `.levelproj`

- Format: UTF-8 JSON with `schemaVersion` field
- Current schema version: `"1.1"` (defined in `LevelApp.Core/Serialization/ProjectSerializer.cs`)
- Serialiser: `LevelApp.Core/Serialization/ProjectSerializer.cs` using `System.Text.Json`
  - `camelCase` property naming policy
  - Custom converters: `ObjectValueConverter`, `JsonStringEnumConverter`
- Schema version `1.0` files are accepted without migration (missing fields default safely)
- File service: `LevelApp.App/Services/ProjectFileService.cs` (interface `IProjectFileService`)
- Documentation: `docs/levelproj.md`

## Localisation

- Resource format: `.resw` (Windows PRI resources)
- Supported locales: `en-US`, `de-DE`
- Resource files: `LevelApp.App/Strings/en-US/Resources.resw`, `LevelApp.App/Strings/de-DE/Resources.resw`
- Service: `LevelApp.App/Services/LocalisationService.cs` (interface `ILocalisationService`)

## CI/CD Pipeline

- Platform: GitHub Actions
- Workflow file: `.github/workflows/ci.yml`
- Trigger: push to `master` branch
- Runner: `windows-latest`

**Pipeline steps:**
1. Checkout (`actions/checkout@v4`)
2. Setup .NET 8 (`actions/setup-dotnet@v4`)
3. `dotnet restore LevelApp.slnx`
4. `dotnet build --configuration Release`
5. `dotnet test LevelApp.Tests/LevelApp.Tests.csproj --configuration Release`
6. Read version from `LevelApp.Core/AppVersion.cs` via PowerShell regex
7. `dotnet publish` both `LevelApp.App` and `LevelApp.Updater` — self-contained, win-x64, single-file for Updater
8. Code-sign executables with `signtool.exe` using PFX from `CODE_SIGN_CERT` / `CODE_SIGN_PASSWORD` GitHub secrets (optional; skipped if secret absent)
   - Timestamp authority: `http://timestamp.digicert.com`
9. `Compress-Archive` → `LevelApp-{VERSION}.zip`
10. Create GitHub Release (`softprops/action-gh-release@v2`), upload zip, auto-generate release notes
    - Tag: `v{Major}.{Minor}.{Patch}`
    - Token: `GITHUB_TOKEN` (built-in)

**Required secrets:**
- `CODE_SIGN_CERT` — base64-encoded PFX certificate (optional; signing skipped if absent)
- `CODE_SIGN_PASSWORD` — PFX passphrase (optional)

## Monitoring & Observability

**Error Tracking:** None (no external service)

**Logs:**
- `LevelApp.App/Services/ActivityLogger.cs` (implements `LevelApp.Core.Interfaces.IActivityLogger`) — in-process activity log
- `LevelApp.Updater` writes a plain-text log to `%TEMP%\LevelApp.Updater.log`

## Authentication & Identity

- No user authentication
- No OAuth or identity provider
- GitHub API calls are unauthenticated (public releases endpoint, rate-limited to 60 req/h per IP)

---

*Integration audit: 2026-05-31*
