# External Integrations

**Analysis Date:** 2026-06-02

LevelApp integrates with physical measurement hardware (via BLE and USB HID), the GitHub Releases API (for auto-update), and local OS storage. There are no third-party cloud services, no user authentication providers, and no external databases.

---

## Hardware Transport Layers

LevelApp uses a plugin architecture (`IInstrumentPlugin` / `ITransport` / `IDeviceScanner` in `LevelApp.Core/Interfaces/`) to abstract instrument hardware. Two transport infrastructure projects are built but not yet wired to a concrete instrument plugin.

### USB HID (`LevelApp.Instruments.UsbHid`)

- Transport ID: `"usb-hid"` — `LevelApp.Instruments.UsbHid/UsbHidTransport.cs`
- Scanner: `LevelApp.Instruments.UsbHid/UsbHidDeviceScanner.cs`
  - Uses `Windows.Devices.Enumeration.DeviceInformation.FindAllAsync` + `DeviceWatcher` (WinRT)
  - Filters by Vendor ID (VID) and one or more Product IDs (PID) via HID AQS selector
  - HID interface GUID: `{4D1E55B2-F16F-11CF-88CB-001111000030}`
  - Guards `DeviceWatcher.Stop()` against double-call
- Provider base: `LevelApp.Instruments.UsbHid/UsbHidInstrumentProviderBase.cs`
  - Opens `HidDevice.FromIdAsync`, wires `InputReportReceived`; no reconnect loop (USB is stable while plugged in)
- Transport capabilities: `SingleReading | ContinuousStream | Bidirectional`
- Status: **Infrastructure built and tested** — no concrete instrument plugin registered yet

### Bluetooth Low Energy (`LevelApp.Instruments.BLE`)

- Transport ID: `"ble"` — `LevelApp.Instruments.BLE/BleTransport.cs`
- Scanner: `LevelApp.Instruments.BLE/BleDeviceScanner.cs`
  - Uses `Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher` (WinRT)
  - Filters by GATT service UUIDs; pass empty array to receive all advertisements
  - Deduplication by Bluetooth address — each address reported at most once per scan session
- Provider base: `LevelApp.Instruments.BLE/BleInstrumentProviderBase.cs`
  - Full connection state machine with exponential backoff reconnect: 1 s → 2 s → 4 s → … capped at 30 s
  - `OnUnexpectedDisconnect()` starts a background reconnect loop cancelled cleanly by `DisconnectAsync()`
- Connection manager: `LevelApp.Instruments.BLE/Internal/BleConnectionManager.cs`
  - Manages `BluetoothLEDevice` + `GattSession` lifetime; `MaintainConnection=true`
- Transport capabilities: `SingleReading | ContinuousStream | Bidirectional`
- Status: **Infrastructure built and tested** — no concrete instrument plugin registered yet

### Manual Entry (`LevelApp.Instruments.Manual`)

- Transport ID: `"manual"` — `LevelApp.Instruments.Manual/ManualTransport.cs`
- No hardware; operator enters readings by hand via `NumberBox` in `MeasurementView`
- Plugin: `ManualEntryPlugin` (`LevelApp.Instruments.Manual/ManualEntryPlugin.cs`) — **currently the only registered `IInstrumentPlugin`**
- Built-in device seeded into `IDeviceRegistry` on startup
- Targets plain `net8.0` (no Windows-specific WinRT APIs needed)

---

## DFU — STM32 Device Firmware Update Subsystem

Handles in-field firmware flashing for STM32-based instrument hardware over USB DFU class protocol.

**Protocol implementation:** `LevelApp.Instruments.UsbHid/Dfu/`
- `DfuSession.cs` — executes a firmware download (host → device) over USB control transfers; page-by-page `DFU_DNLOAD` with `DFU_GETSTATUS` polling
- `DfuConnectionDetector.cs` — waits up to 10 s for a WinUSB device with the instrument's DFU VID+PID to appear after `DFU_DETACH`
- `Dfu/Internal/IUsbControlTransport.cs` — testable interface (internal; exposed to test project via `InternalsVisibleTo`)
- `Dfu/Internal/WinUsbControlTransport.cs` — production P/Invoke implementation

**USB access approach — why P/Invoke to `WinUsb.dll`:**
- `Windows.Devices.Usb.UsbDevice` (WinRT) requires the `usbDevice` capability in a packaged app manifest; LevelApp is unpackaged (`WindowsPackageType=None`) and has no manifest, so `UsbDevice` throws `UnauthorizedAccessException`
- `WinUsb.dll` is a system DLL available on all Windows editions since Vista; works from any process without capability declarations
- P/Invoke surface used: `WinUsb_Initialize`, `WinUsb_ControlTransfer`, `WinUsb_Free`

**DFU protocol sequence (USB DFU spec v1.1):**
1. For each firmware page (default 2048 bytes, configurable per-instrument): send `DFU_DNLOAD` (request 0x01), poll `DFU_GETSTATUS` (request 0x03) until device reaches `dfuDNLOAD-IDLE` (state 5)
2. After last page: send zero-length `DFU_DNLOAD` to trigger manifestation; poll until `dfuIDLE` (state 2) or `dfuMANIFEST-WAIT-RESET` (state 8)
3. Device reboots; `DfuConnectionDetector` waits for normal-mode HID device to reappear

**Firmware updater interface:** `LevelApp.Core/Interfaces/IFirmwareUpdater.cs`
- Status: **Defined** — no concrete implementation yet; all current plugins return `null` from `IInstrumentPlugin.CreateFirmwareUpdater()`; `FirmwareUpdateDialog` handles `null` gracefully

---

## Auto-Update — GitHub Releases API

**Check and download (in-app):**
- Service: `LevelApp.App/Services/UpdateService.cs` (implements `IUpdateService`)
- API endpoint: `GET https://api.github.com/repos/soldernerd/LevelApp/releases/latest`
- HTTP client: `System.Net.Http.HttpClient` with `Timeout = TimeSpan.FromSeconds(10)` and `User-Agent: LevelApp/{version}`
- Authentication: **none** — public releases endpoint; rate-limited to 60 requests/hour per IP
- Version comparison: parses `tag_name` (`"v0.19.0"`) and compares numerically against `AppVersion.Full`; returns `null` if up to date, error, or no `.zip` asset found — never throws to caller
- Download: streams the `.zip` asset from `browser_download_url` to `%TEMP%\LevelApp-{version}.zip` with chunked progress reporting (80 KB chunks)
- Update check on startup is fire-and-forget; failure is always swallowed silently

**Install (external process — `LevelApp.Updater`):**
- Standalone self-contained executable: `LevelApp.Updater/Program.cs` (targets `net8.0`, no Windows TFM needed)
- Published as a single-file `win-x64` executable alongside `LevelApp.App.exe`
- Uses a copy-to-temp pattern:
  1. First invocation copies itself to `%TEMP%\LevelApp.Updater.tmp.exe`, relaunches with `--from-temp`
  2. Temp copy waits up to 10 s for main app process to exit
  3. Extracts zip over install folder (`overwriteFiles: true`) using `System.IO.Compression`
  4. Deletes the zip
  5. Launches the updated `LevelApp.App.exe`
- All steps logged to `%TEMP%\LevelApp.Updater.log`
- Argument contract defined in `UpdaterContract.cs` (duplicated verbatim in `LevelApp.App/Services/UpdaterContract.cs` and `LevelApp.Updater/UpdaterContract.cs` — cannot share a project reference because `LevelApp.Updater` targets plain `net8.0`):
  ```
  LevelApp.Updater.exe  <zipPath>  <installFolder>  <mainExeName>  [--from-temp]
  ```
  `installFolder` must NOT end with a backslash — `UpdateDialog.xaml.cs` strips trailing backslash from `AppContext.BaseDirectory` before passing it

---

## File Format — `.levelproj`

- Format: UTF-8 JSON, indented, camelCase property names
- File extension: `.levelproj` (identified as such; internally standard JSON)
- Current schema version: `"1.1"` (field `schemaVersion` at root)
- Serialiser: `LevelApp.Core/Serialization/ProjectSerializer.cs` using `System.Text.Json`
  - Custom converters: `ObjectValueConverter` (preserves concrete types in `Dictionary<string, object>`), `OrientationConverter` (reads/writes `Orientation` as string enum), `JsonStringEnumConverter`
  - `NotSupportedException` thrown for unrecognised schema versions
- Schema version `1.0` files accepted without migration (missing fields default safely)
- File open/save: `LevelApp.App/Services/ProjectFileService.cs` uses Win32 COM `IFileOpenDialog` / `IFileSaveDialog` via `CoCreateInstance` directly — WinRT file pickers were bypassed because they create the underlying COM dialog lazily, making `SetFolder` calls ineffective for controlling the initial directory
- Full format reference: `docs/levelproj.md`

---

## Localisation

- Resource format: `.resw` (Windows PRI resources), compiled to `.pri` at build time
- Supported locales: `en-US`, `de-DE` (195 keys each)
- Resource files: `LevelApp.App/Strings/en-US/Resources.resw`, `LevelApp.App/Strings/de-DE/Resources.resw`
- Runtime API: `Windows.ApplicationModel.Resources.ResourceLoader` (WinRT)
- Service wrapper: `LevelApp.App/Services/LocalisationService.cs` (interface `ILocalisationService`)
- XAML: `x:Uid` attribute wires keys to TextBlock, Button, MenuBarItem, and other element properties automatically
- Adding a new locale requires only a new `.resw` file — no code changes

---

## CI/CD Pipeline

- Platform: GitHub Actions
- Workflow file: `.github/workflows/ci.yml`
- Trigger: push to `master` branch
- Runner: `windows-latest`

**Pipeline steps:**
1. Checkout (`actions/checkout@v4`)
2. Setup .NET 8 (`actions/setup-dotnet@v4`)
3. `dotnet restore LevelApp.slnx`
4. `dotnet build LevelApp.slnx --configuration Release --no-restore`
5. `dotnet test LevelApp.Tests/LevelApp.Tests.csproj --configuration Release --no-build`
6. Read version from `LevelApp.Core/AppVersion.cs` via PowerShell regex; outputs `VERSION` and `TAG` step variables
7. `dotnet publish LevelApp.App` — self-contained, `win-x64`; custom `CopyWinUIAssetsToPublish` MSBuild target copies `.xbf` and PRI file to publish folder
8. `dotnet publish LevelApp.Updater` — self-contained, `win-x64`, `-p:PublishSingleFile=true`
9. Authenticode code-sign both executables with `signtool.exe` using PFX from `CODE_SIGN_CERT` / `CODE_SIGN_PASSWORD` GitHub secrets; timestamp authority `http://timestamp.digicert.com`; step skipped gracefully if secrets absent
10. `Compress-Archive` → `LevelApp-{VERSION}.zip`
11. Create GitHub Release (`softprops/action-gh-release@v2`), upload zip asset, auto-generate release notes; tag `v{Major}.{Minor}.{Patch}`

**Required secrets:**
- `CODE_SIGN_CERT` — base64-encoded PFX certificate (optional; code signing skipped if absent)
- `CODE_SIGN_PASSWORD` — PFX passphrase (optional)
- `GITHUB_TOKEN` — built-in; used for release creation

**Known limitation:** Self-signed certificate shows "Unknown Publisher" in Windows SmartScreen. Only OV/EV certificates from a public CA establish SmartScreen reputation.

---

## Monitoring & Observability

**Error Tracking:** None — no external service (Sentry, AppInsights, etc.)

**In-process activity log:**
- `LevelApp.App/Services/ActivityLogger.cs` (implements `LevelApp.Core/Interfaces/IActivityLogger`)
- Writes `.jsonl` (JSON Lines) and `.instrument` sidecar files to `%LOCALAPPDATA%\LevelApp\Logs\`
- Toggleable via `ISettingsService.ActivityLoggingEnabled`; persisted to `settings.json`
- Prunes log files older than 14 days on startup
- Unhandled exception hooks in `App.OnLaunched` write `CRASH` and `CRASH.UI` entries
- Replay infrastructure in `LevelApp.Tests/Replay/` allows `.jsonl` sessions to be replayed against ViewModel stubs for regression testing

**Updater log:** plain text at `%TEMP%\LevelApp.Updater.log`

---

## Authentication & Identity

- No user authentication
- No OAuth, OpenID Connect, or identity provider
- GitHub Releases API calls are unauthenticated (public endpoint; rate-limited to 60 requests/hour per IP)

---

## Data Storage

**Project files:** `.levelproj` JSON files, user-chosen location on local filesystem

**App settings:** `%LOCALAPPDATA%\LevelApp\settings.json` (created by `SettingsService`; not a database)

**Device registry:** `%LOCALAPPDATA%\LevelApp\devices.json` (created by `DeviceRegistry`; corrupt files backed up to `.corrupt` and `LoadError` exposed on `IDeviceRegistry`)

**Activity logs:** `%LOCALAPPDATA%\LevelApp\Logs\` (`.jsonl` + `.instrument` files; no external log sink)

**No cloud storage, no remote database, no SQLite.**

---

*Integration audit: 2026-06-02*
