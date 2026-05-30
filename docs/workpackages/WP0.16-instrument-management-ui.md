# Work Package 0.16 — Instrument Management UI

> Target version: **v0.16.0**
> Prerequisite: WP0.15 complete (v0.15.0) ✓

---

## Goal

Add a generic instrument management section to the app's navigation.
The section hosts a per-plugin tab structure: device list, scan-and-add
flow, and placeholders for calibration and firmware update. The UI is
driven entirely by registered `IInstrumentPlugin` implementations —
no hardcoded instrument knowledge in the app.

With only `ManualEntryPlugin` registered the section is minimal but
fully functional, proving the navigation and tab structure before any
real hardware is involved.

---

## Navigation change

Add an **Instruments** top-level menu item (or navigation pane entry,
matching the existing navigation pattern). It is visible always — even
with only manual entry registered — so the pattern is established.

```
Main navigation
├── Project          (existing)
├── Measurement      (existing)
├── Results          (existing)
└── Instruments      (new)
    └── [tab per registered plugin]
        ├── Manual Entry
        │   └── (no devices to list — informational only)
        └── [future: Wyler Level]
            ├── Known Devices list
            ├── Scan for Devices button
            └── [Calibration] [Firmware Update] buttons if supported
```

---

## New views

### `InstrumentsPage.xaml`

Top-level host page. Contains a `TabView` (or `NavigationView` with
sub-items) with one tab per registered `IInstrumentPlugin`. Tabs are
generated at runtime from `IEnumerable<IInstrumentPlugin>`.

```xml
<TabView x:Name="PluginTabs" TabItemsSource="{x:Bind ViewModel.Plugins}">
  <TabView.TabItemTemplate>
    <DataTemplate x:DataType="vm:InstrumentPluginTabViewModel">
      <TabViewItem Header="{x:Bind DisplayName}"
                   Content="{x:Bind TabContent}"/>
    </DataTemplate>
  </TabView.TabItemTemplate>
</TabView>
```

### `InstrumentPluginTabView.xaml`

Content for each plugin tab. Layout:

```
┌─────────────────────────────────────────┐
│ [Plugin DisplayName]                    │
│                                         │
│ Known Devices                           │
│ ┌─────────────────────────────────────┐ │
│ │ • Device A (BLE) — Connected    [✕] │ │
│ │ • Device B (USB) — Last seen 2d [✕] │ │
│ └─────────────────────────────────────┘ │
│ [+ Add Device]                          │
│                                         │
│ [Calibration]   [Firmware Update]       │
│  (disabled if not supported)            │
│                                         │
│ [Plugin-specific management view]       │
│  (embedded if plugin returns one)       │
└─────────────────────────────────────────┘
```

The **Calibration** and **Firmware Update** buttons are:
- Hidden when the capability is null (plugin doesn't support it)
- Disabled when `IFirmwareUpdater.IsReady == false`, with a tooltip
  showing `RequiredTransport` as a hint (e.g. "USB connection required")
- Enabled and open their respective dialogs when ready

### `ScanForDevicesDialog.xaml`

A `ContentDialog` opened by the **+ Add Device** button.

```
┌────────────────────────────────┐
│ Add Device                     │
│                                │
│ Transport  [ Bluetooth ▼ ]     │
│  (only shown if >1 transport)  │
│                                │
│ [Scan]                         │
│                                │
│ ┌──────────────────────────┐   │
│ │ Wyler BT-Level #4A2F -72│   │
│ │ Wyler BT-Level #1B09 -85│   │
│ └──────────────────────────┘   │
│                                │
│ [Add Selected]  [Cancel]       │
└────────────────────────────────┘
```

Behaviour:
1. User opens dialog, selects transport (skip if only one)
2. Taps **Scan** — calls `plugin.CreateScanners()`, finds the scanner
   matching the selected transport, calls `ScanAsync()`
3. `DeviceCandidate` items stream in and populate the list
4. User selects one and taps **Add Selected**
5. App calls `_deviceRegistry.RegisterDevice(new KnownDevice(...))`
6. Dialog closes; device appears in the Known Devices list

For `ManualEntryPlugin` the **+ Add Device** button is hidden — it uses
its built-in device and needs no scanning.

### `FirmwareUpdateDialog.xaml`

A `ContentDialog` opened from the **Firmware Update** button.

```
┌──────────────────────────────────────┐
│ Firmware Update                      │
│                                      │
│ Current version:  1.2.0              │
│ Available version: 1.3.1             │
│                                      │
│ Release notes:                       │
│ ┌──────────────────────────────────┐ │
│ │ - Fixed settling algorithm       │ │
│ │ - Improved BLE stability         │ │
│ └──────────────────────────────────┘ │
│                                      │
│ ████████████░░░░░░░  62%             │
│                                      │
│ [Update Now]  [Cancel]               │
└──────────────────────────────────────┘
```

Uses `IFirmwareUpdater.CheckForUpdateAsync()` on open to populate version
info. Progress bar bound to `IProgress<double>` during update. Both buttons
disabled during update. On completion: show "Update complete. Please
reconnect the device." and close button only.

---

## `InstrumentsViewModel`

```csharp
public class InstrumentsViewModel : ObservableObject
{
    public ObservableCollection<InstrumentPluginTabViewModel> Plugins { get; }

    public InstrumentsViewModel(
        IEnumerable<IInstrumentPlugin> plugins,
        IDeviceRegistry registry)
    {
        Plugins = new ObservableCollection<InstrumentPluginTabViewModel>(
            plugins.Select(p => new InstrumentPluginTabViewModel(p, registry)));
    }
}
```

### `InstrumentPluginTabViewModel`

One instance per registered plugin. Owns:
- `ObservableCollection<KnownDeviceViewModel> KnownDevices`
- `bool CanCalibrate` — plugin.`CreateCalibrationWorkflow(device) != null`
- `bool CanUpdateFirmware` — plugin.`CreateFirmwareUpdater(device) != null`
- `bool FirmwareUpdateReady` — `updater.IsReady` (subscribes to `IsReadyChanged`)
- `string FirmwareUpdateTooltip` — derived from `updater.RequiredTransport`
- Commands: `AddDeviceCommand`, `ForgetDeviceCommand`,
  `OpenCalibrationCommand`, `OpenFirmwareUpdateCommand`

---

## What this work package explicitly does NOT do

- Implement any BLE or USB scanning (the scan dialog is present but
  will return no results until a real transport is registered — WP0.17)
- Implement calibration workflow UI beyond the dialog host
- Change any measurement logic, data models, or file format

---

## Acceptance criteria

1. **Instruments** navigation item exists and is reachable
2. One tab per registered plugin appears at runtime
3. `ManualEntryPlugin` tab shows correctly with no device list and no
   scan button
4. `ScanForDevicesDialog` opens and shows "No devices found" gracefully
   when no real transport is registered
5. `FirmwareUpdateDialog` structure exists (verify with a mock updater
   that returns a fake `FirmwareInfo`)
6. All existing tests pass
7. No behaviour change to measurement workflow

---

## Version bump

Set `AppVersion.Minor` → `16`, `AppVersion.Patch` → `0`. Commit message:

```
[v0.16.0] WP0.16: instrument management UI — plugin tabs, device list, scan dialog, firmware update dialog
```
