# Work Package 0.15 — Instrument Plugin Architecture

> Target version: **v0.15.0**
> Prerequisite: WP0.14 complete (v0.14.0) ✓

---

## Goal

Introduce the full instrument plugin architecture in `LevelApp.Core`.
Define all interfaces that instrument projects will implement.
Migrate `ManualEntryProvider` into a `ManualEntryPlugin`.
Update DI registration and `MeasurementViewModel` to work with plugins.

The app remains functionally identical after this work package — only
the internal structure changes.

---

## New enums

### `TransportRequirement`

```csharp
// LevelApp.Core/Instruments/TransportRequirement.cs
public enum TransportRequirement
{
    None,       // no transport needed (e.g. manual entry)
    Any,        // any connected transport suffices
    BleOnly,    // requires Bluetooth LE
    UsbOnly,    // requires USB (e.g. DFU firmware update)
    UsbOrBle,   // either will do
}
```

### `TransportCapabilities`

```csharp
// LevelApp.Core/Instruments/TransportCapabilities.cs
[Flags]
public enum TransportCapabilities
{
    None           = 0,
    SingleReading  = 1 << 0,
    ContinuousStream = 1 << 1,
    Bidirectional  = 1 << 2,   // can send commands to device
}
```

---

## New models

### `KnownDevice`

```csharp
// LevelApp.Core/Instruments/KnownDevice.cs
public record KnownDevice(
    string DeviceId,          // stable hardware identifier
    string PluginId,          // matches IInstrumentPlugin.PluginId
    string TransportId,       // matches ITransport.TransportId
    string DisplayName,       // user-visible name
    string TransportAddress   // BLE MAC, USB path, etc.
);
```

### `FirmwareInfo`

```csharp
// LevelApp.Core/Instruments/FirmwareInfo.cs
public record FirmwareInfo(
    string Version,
    string? ReleaseNotes,
    string? DownloadUrl
);
```

### `DeviceCandidate`

Returned by `IDeviceScanner` during a scan — a device seen on the bus
but not yet registered as a `KnownDevice`.

```csharp
// LevelApp.Core/Instruments/DeviceCandidate.cs
public record DeviceCandidate(
    string CandidateId,       // transport-level address
    string TransportId,
    string DisplayName,       // e.g. "Wyler BT-Level #4A2F"
    int? SignalStrength        // dBm for BLE, null for USB
);
```

---

## New interfaces

### `ITransport`

```csharp
// LevelApp.Core/Interfaces/ITransport.cs
public interface ITransport
{
    string TransportId { get; }          // "ble", "usb-hid", "manual"
    string DisplayName { get; }          // "Bluetooth", "USB", "Manual"
    TransportCapabilities Capabilities { get; }
}
```

### `IDeviceScanner`

```csharp
// LevelApp.Core/Interfaces/IDeviceScanner.cs
public interface IDeviceScanner
{
    ITransport Transport { get; }

    /// <summary>
    /// Scan for nearby devices that match this plugin's expected profile.
    /// Reports candidates as they are found. Completes when the scan
    /// timeout elapses or ct is cancelled.
    /// </summary>
    IAsyncEnumerable<DeviceCandidate> ScanAsync(
        TimeSpan timeout, CancellationToken ct);
}
```

### `IDeviceRegistry`

```csharp
// LevelApp.Core/Interfaces/IDeviceRegistry.cs
public interface IDeviceRegistry
{
    IReadOnlyList<KnownDevice> GetKnownDevices(string pluginId);
    IReadOnlyList<KnownDevice> GetAllKnownDevices();
    void RegisterDevice(KnownDevice device);
    void ForgetDevice(string deviceId);
    KnownDevice? GetPreferredDevice(string pluginId);
    void SetPreferredDevice(string pluginId, string deviceId);
}
```

### `ICalibrationWorkflow`

```csharp
// LevelApp.Core/Interfaces/ICalibrationWorkflow.cs
public interface ICalibrationWorkflow
{
    string DisplayName { get; }

    /// <summary>
    /// Returns a UIElement the app embeds in the instrument management
    /// tab. Declared as object to keep Core free of WinUI dependencies;
    /// cast to UIElement in LevelApp.App.
    /// </summary>
    object CreateView();
}
```

### `IFirmwareUpdater`

```csharp
// LevelApp.Core/Interfaces/IFirmwareUpdater.cs
public interface IFirmwareUpdater
{
    TransportRequirement RequiredTransport { get; }

    /// <summary>
    /// True when the required transport is currently available.
    /// The app uses this to enable/disable the Update button.
    /// </summary>
    bool IsReady { get; }

    event EventHandler IsReadyChanged;

    Task<FirmwareInfo> GetCurrentFirmwareAsync(CancellationToken ct = default);
    Task<FirmwareInfo?> CheckForUpdateAsync(CancellationToken ct = default);
    Task PerformUpdateAsync(IProgress<double> progress, CancellationToken ct);
}
```

### `IInstrumentPlugin`

The root contract. Each instrument project provides exactly one
implementation.

```csharp
// LevelApp.Core/Interfaces/IInstrumentPlugin.cs
public interface IInstrumentPlugin
{
    string PluginId { get; }
    string DisplayName { get; }
    InstrumentCapabilities Capabilities { get; }

    /// <summary>
    /// All transports this instrument supports.
    /// ManualEntry returns a single ManualTransport.
    /// A dual BLE+USB instrument returns both.
    /// </summary>
    IReadOnlyList<ITransport> SupportedTransports { get; }

    /// <summary>
    /// All scanners, one per supported transport.
    /// </summary>
    IReadOnlyList<IDeviceScanner> CreateScanners();

    /// <summary>
    /// Create the reading provider for a specific known device.
    /// The transport used is determined by device.TransportId.
    /// </summary>
    IInstrumentProvider CreateProvider(KnownDevice device);

    // Optional capabilities — null means not supported by this instrument
    ICalibrationWorkflow? CreateCalibrationWorkflow(KnownDevice device);
    IFirmwareUpdater?     CreateFirmwareUpdater(KnownDevice device);

    /// <summary>
    /// Optional instrument-specific device management view.
    /// Returned as object for the same reason as ICalibrationWorkflow.
    /// </summary>
    object? CreateDeviceManagementView(IDeviceRegistry registry);
}
```

---

## New project: `LevelApp.Instruments.Manual`

Move `ManualEntryProvider` out of `LevelApp.App` into its own project
`LevelApp.Instruments.Manual`. This establishes the pattern all future
instrument projects follow.

```
LevelApp.Instruments.Manual/
├── ManualTransport.cs        ← ITransport, TransportId = "manual"
├── ManualEntryScanner.cs     ← IDeviceScanner (returns empty — no scan needed)
├── ManualEntryProvider.cs    ← IInstrumentProvider (unchanged logic)
└── ManualEntryPlugin.cs      ← IInstrumentPlugin (root)
```

### `ManualEntryPlugin`

```csharp
public class ManualEntryPlugin : IInstrumentPlugin
{
    public string PluginId    => "manual-entry";
    public string DisplayName => "Manual Entry";
    public InstrumentCapabilities Capabilities =>
        InstrumentCapabilities.SingleMeasurement;

    public IReadOnlyList<ITransport> SupportedTransports =>
        [new ManualTransport()];

    public IReadOnlyList<IDeviceScanner> CreateScanners() =>
        [new ManualEntryScanner()];

    // Manual entry needs no device selection — return a fixed device
    public IInstrumentProvider CreateProvider(KnownDevice device) =>
        new ManualEntryProvider();

    // No calibration, firmware update, or device management
    public ICalibrationWorkflow?  CreateCalibrationWorkflow(KnownDevice device) => null;
    public IFirmwareUpdater?      CreateFirmwareUpdater(KnownDevice device)      => null;
    public object?                CreateDeviceManagementView(IDeviceRegistry r)  => null;
}
```

`ManualEntryProvider` acquires a fixed synthetic `KnownDevice` representing
"manual entry" so the system has a consistent device identity even for the
no-hardware case:

```csharp
public static readonly KnownDevice BuiltInDevice = new(
    DeviceId:         "manual-entry-builtin",
    PluginId:         "manual-entry",
    TransportId:      "manual",
    DisplayName:      "Manual Entry",
    TransportAddress: string.Empty
);
```

---

## Changes to `LevelApp.App`

### DI registration

Replace the bare `IInstrumentProvider` registration with `IInstrumentPlugin`:

```csharp
// App.xaml.cs
services.AddSingleton<IInstrumentPlugin, ManualEntryPlugin>();
services.AddSingleton<IDeviceRegistry,  DeviceRegistry>();
```

`IEnumerable<IInstrumentPlugin>` is injected wherever the full list is needed.

### `DeviceRegistry` implementation

Add `LevelApp.App/Services/DeviceRegistry.cs` implementing `IDeviceRegistry`.
Persists known devices as a JSON array in
`%LOCALAPPDATA%\LevelApp\devices.json`. Separate file from `settings.json`
to keep concerns clean.

### `MeasurementViewModel`

`MeasurementViewModel` receives `IEnumerable<IInstrumentPlugin>` and
`IDeviceRegistry`. It selects the active plugin and device based on the
session's `instrumentId` and creates the provider via
`plugin.CreateProvider(device)`. The rest of the ViewModel is unchanged.

### `ProjectSetupView` — instrument selector

The existing instrument selector (currently a hardcoded `ComboBox` showing
only Manual Entry) is now populated from registered plugins. For each plugin,
show its `DisplayName`. For plugins with more than one known device in the
registry, show a sub-selector. For `ManualEntryPlugin`, no device sub-
selector is needed (always uses `BuiltInDevice`).

---

## Unit tests

Add to `LevelApp.Tests`:

```
PluginArchitectureTests.cs
  ✓ ManualEntryPlugin_HasCorrectPluginId
  ✓ ManualEntryPlugin_ReturnsNullForOptionalCapabilities
  ✓ ManualEntryPlugin_CreateProvider_ReturnsConnectedProvider
  ✓ DeviceRegistry_RegisterAndRetrieve
  ✓ DeviceRegistry_ForgetDevice_RemovesFromList
  ✓ DeviceRegistry_PreferredDevice_RoundTrips
```

---

## What this work package explicitly does NOT do

- Add any BLE or USB transport implementation
- Add device scanning UI (that is WP0.16)
- Change any measurement logic, calculators, or file format

---

## Acceptance criteria

1. All new interfaces exist in `LevelApp.Core/Interfaces/`
2. `LevelApp.Instruments.Manual` project exists and is added to the solution
3. `ManualEntryPlugin` is the registered `IInstrumentPlugin` in `App.xaml.cs`
4. `DeviceRegistry` persists to `devices.json` and round-trips correctly
5. `ProjectSetupView` instrument selector is driven by registered plugins
6. All existing tests pass; new plugin architecture tests pass
7. No behaviour change visible to the user

---

## Version bump

Set `AppVersion.Minor` → `15`, `AppVersion.Patch` → `0`. Commit message:

```
[v0.15.0] WP0.15: instrument plugin architecture — interfaces, ManualEntryPlugin, DeviceRegistry
```
