# Session Prompts — WP0.14 through WP0.18

---

## WP0.14 — Extended Instrument Provider Interface

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in
`docs/workpackages/WP0.14-instrument-provider.md`. Please read both before
starting any work.

We are implementing Work Package 0.14 (target v0.14.0). This is a pure
Core interface change — no BLE, no new projects.

Key things to know:
- Current version is v0.13.2. Bump to v0.14.0 on completion.
- `IInstrumentProvider` is in `LevelApp.Core/Interfaces/`.
- `ManualEntryProvider` is currently in `LevelApp.App` — do not move it
  yet, that is WP0.15.
- The InfoBar for connection status goes in `MeasurementView.xaml`. Check
  the existing layout before adding it to ensure it fits without disrupting
  the current step-by-step UI.
- Do not change any data models, calculators, persistence, or other
  interfaces.
- All existing tests must pass after this change.
```

---

## WP0.15 — Instrument Plugin Architecture

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in
`docs/workpackages/WP0.15-plugin-architecture.md`. Please read both before
starting any work.

We are implementing Work Package 0.15 (target v0.15.0). This introduces
the full plugin interface hierarchy in Core and migrates ManualEntryProvider
into a new LevelApp.Instruments.Manual project.

Key things to know:
- Current version is v0.14.0.
- All new interfaces go in `LevelApp.Core/Interfaces/`.
- New models (KnownDevice, FirmwareInfo, DeviceCandidate) go in
  `LevelApp.Core/Instruments/`.
- Create `LevelApp.Instruments.Manual` as a new .NET 8 class library and
  add it to `LevelApp.slnx`.
- `DeviceRegistry` persists to `%LOCALAPPDATA%\LevelApp\devices.json` —
  separate file from `settings.json`.
- `ICalibrationWorkflow.CreateView()` and
  `IInstrumentPlugin.CreateDeviceManagementView()` return `object` (not
  UIElement) to keep Core free of WinUI dependencies. Cast to UIElement
  in LevelApp.App at the call site.
- After migration, `ManualEntryProvider` must no longer exist directly
  in `LevelApp.App` — it lives in `LevelApp.Instruments.Manual`.
- The app must behave identically from the user's perspective after this
  change. Verify by running through a full measurement session.
- All existing tests must pass; add the new plugin architecture tests
  specified in the work package.
```

---

## WP0.16 — Instrument Management UI

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in
`docs/workpackages/WP0.16-instrument-management-ui.md`. Please read both
before starting any work.

We are implementing Work Package 0.16 (target v0.16.0). This adds the
Instruments navigation section and all associated views.

Key things to know:
- Current version is v0.15.0.
- The navigation pattern (menu bar, frame navigation) was established in
  WP0.01/WP0.02 — match the existing pattern exactly, do not introduce a
  new navigation style.
- `IInstrumentPlugin.CreateDeviceManagementView()` returns `object` —
  cast to `UIElement` in `InstrumentPluginTabView` before embedding.
- The scan dialog calls `plugin.CreateScanners()` — with only
  ManualEntryPlugin registered, `ManualEntryScanner.ScanAsync()` returns
  an empty async enumerable. The dialog must handle this gracefully
  (show "No devices found" message, not hang or crash).
- The firmware update dialog must also handle the case where
  `IFirmwareUpdater.CheckForUpdateAsync()` returns null (already up to
  date) gracefully.
- Use `{ThemeResource}` and `{StaticResource}` consistently — no inline
  colours or font sizes in any new XAML.
- Do not change any measurement logic, data models, or file format.
- All existing tests must pass.
```

---

## WP0.17 — BLE Transport Infrastructure

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in
`docs/workpackages/WP0.17-ble-transport.md`. Please read both before
starting any work.

We are implementing Work Package 0.17 (target v0.17.0). This creates the
LevelApp.Instruments.BLE project — pure infrastructure, no instrument
specifics.

Key things to know:
- Current version is v0.16.0.
- Create `LevelApp.Instruments.BLE` as a .NET 8 class library. Add it to
  `LevelApp.slnx`. It does NOT need to be a WinUI project.
- Use `Microsoft.Windows.SDK.Contracts` NuGet package for WinRT BLE types
  (`Windows.Devices.Bluetooth`, `Windows.Devices.Bluetooth.Advertisement`,
  `Windows.Devices.Bluetooth.GenericAttributeProfile`).
- `BleDeviceScanner` uses `BluetoothLEAdvertisementWatcher` — check the
  WinRT docs for the correct pattern for yielding results as
  IAsyncEnumerable.
- `BleInstrumentProviderBase` reconnection uses exponential backoff up to
  30s — implement this exactly as specified, it is important for production
  reliability.
- This project must contain ZERO GATT UUIDs, instrument names, or
  device-specific constants. If you find yourself typing a UUID, stop —
  it belongs in the concrete instrument project.
- The existing app must not reference this new project yet — it will be
  referenced by concrete instrument projects in WP0.18+.
- Write unit tests for the state machine and cancellation behaviour as
  specified. If mocking WinRT types is impractical, cover the pure logic
  (state transitions, backoff timing) with interface-based mocks and
  note what requires hardware testing.
```

---

## WP0.18 — USB HID Transport Infrastructure

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in
`docs/workpackages/WP0.18-usb-hid-transport.md`. Please read both before
starting any work.

We are implementing Work Package 0.18 (target v0.18.0). This creates the
LevelApp.Instruments.UsbHid project — USB HID transport infrastructure
plus the STM32 DFU subsystem.

Key things to know:
- Current version is v0.17.0.
- Create `LevelApp.Instruments.UsbHid` as a .NET 8 class library. Add to
  `LevelApp.slnx`.
- Use `Microsoft.Windows.SDK.Contracts` for WinRT HID types
  (`Windows.Devices.HumanInterfaceDevice`).
- For DFU mode (WinUSB), first try `Windows.Devices.Usb.UsbDevice`. If
  the STM32 DFU interface class (0xFE/0x01/0x02) is not supported by the
  WinRT USB stack, fall back to P/Invoke against WinUsb.dll. Document
  which approach you used and why in a comment in DfuSession.cs.
- DFU page size defaults to 2048 bytes — make this a constructor parameter
  on DfuSession so concrete instrument projects can override it.
- This project must contain ZERO VID/PID constants, report format
  knowledge, or device-specific logic.
- The existing app must not reference this new project yet.
- Write unit tests for cancellation and progress reporting with mocks.
  Full DFU integration testing requires hardware and is deferred to the
  concrete instrument work package.
```
