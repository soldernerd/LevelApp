# Work Package 0.18 — USB HID Transport Infrastructure

> Target version: **v0.18.0**
> Prerequisite: WP0.17 complete (v0.17.0) ✓
> Can be developed in parallel with WP0.17 if desired.

---

## Goal

Create `LevelApp.Instruments.UsbHid` — the USB transport infrastructure
project, mirroring `LevelApp.Instruments.BLE` in structure. Provides
`UsbHidTransport`, `UsbHidDeviceScanner`, `UsbHidInstrumentProviderBase`,
and critically `DfuConnectionDetector` — logic to recognise when an STM32
device re-enumerates in DFU mode after receiving the detach command.

Contains no instrument-specific knowledge: no VID/PID constants, no
report formats, no DFU image handling.

---

## New project: `LevelApp.Instruments.UsbHid`

A .NET 8 class library. References:
- `LevelApp.Core`
- `Microsoft.Windows.SDK.Contracts` (WinRT HID APIs)

Add to `LevelApp.slnx`.

```
LevelApp.Instruments.UsbHid/
├── UsbHidTransport.cs
├── UsbHidDeviceScanner.cs
├── UsbHidInstrumentProviderBase.cs
└── Dfu/
    ├── DfuConnectionDetector.cs
    └── DfuSession.cs
```

---

## `UsbHidTransport`

```csharp
public sealed class UsbHidTransport : ITransport
{
    public string TransportId  => "usb-hid";
    public string DisplayName  => "USB";
    public TransportCapabilities Capabilities =>
        TransportCapabilities.SingleReading |
        TransportCapabilities.ContinuousStream |
        TransportCapabilities.Bidirectional;
}
```

---

## `UsbHidDeviceScanner`

Enumerates connected USB HID devices matching a supplied VID/PID filter.
Unlike BLE, USB HID devices do not need to be "scanned" — they are
enumerated synchronously from the OS device list. The scanner yields
currently connected matching devices and then optionally watches for
new connections.

```csharp
public sealed class UsbHidDeviceScanner : IDeviceScanner
{
    public ITransport Transport => new UsbHidTransport();

    /// <param name="vendorId">USB Vendor ID to filter by.</param>
    /// <param name="productIds">
    /// One or more Product IDs. Multiple PIDs handle the case where the
    /// same device presents different PIDs in normal vs DFU mode.
    /// </param>
    public UsbHidDeviceScanner(ushort vendorId, ushort[] productIds) { ... }

    public async IAsyncEnumerable<DeviceCandidate> ScanAsync(
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 1. Enumerate currently connected matching devices via
        //    Windows.Devices.HumanInterfaceDevice.HidDevice.GetDeviceSelector()
        //    + DeviceInformation.FindAllAsync()
        // 2. Yield each as a DeviceCandidate immediately
        // 3. Watch for new arrivals via DeviceWatcher until timeout/ct
    }
}
```

---

## `UsbHidInstrumentProviderBase`

Abstract base class for USB HID reading providers. Simpler than BLE —
USB connections are inherently stable while plugged in, so reconnection
logic is minimal.

```csharp
public abstract class UsbHidInstrumentProviderBase : IInstrumentProvider
{
    public abstract string ProviderId { get; }
    public abstract string DisplayName { get; }
    public abstract InstrumentCapabilities Capabilities { get; }

    public InstrumentConnectionState ConnectionState { get; private set; }
        = InstrumentConnectionState.Disconnected;

    public event EventHandler<InstrumentConnectionState>? ConnectionStateChanged;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        SetState(InstrumentConnectionState.Connecting);
        try
        {
            _device = await HidDevice.FromIdAsync(_deviceId,
                FileAccessMode.ReadWrite);
            if (_device == null)
                throw new InvalidOperationException("Device not found.");

            _device.InputReportReceived += OnInputReportReceived;
            await OnConnectedAsync(_device, ct);
            SetState(InstrumentConnectionState.Connected);
        }
        catch { SetState(InstrumentConnectionState.Error); throw; }
    }

    public async Task DisconnectAsync()
    {
        await OnDisconnectingAsync();
        _device?.Dispose();
        SetState(InstrumentConnectionState.Disconnected);
    }

    public abstract Task<double> GetReadingAsync(
        MeasurementStep step, CancellationToken ct);

    // ── Override points ──────────────────────────────────────────────

    protected abstract Task OnConnectedAsync(
        HidDevice device, CancellationToken ct);

    protected virtual Task OnDisconnectingAsync() => Task.CompletedTask;

    /// Called for every USB HID input report received.
    protected abstract void OnInputReportReceived(
        HidDevice sender, HidInputReportReceivedEventArgs args);

    protected void SetState(InstrumentConnectionState state)
    {
        if (ConnectionState == state) return;
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(this, state);
    }
}
```

---

## DFU subsystem

The DFU subsystem is the most complex part of this work package. It handles
the STM32 USB DFU protocol without instrument-specific knowledge.

### STM32 DFU protocol overview

```
1. Normal mode (HID device, custom VID/PID)
   App sends DFU_DETACH command over HID
   Device disconnects and re-enumerates as DFU device (different PID)

2. DFU mode (WinUSB device, DFU VID/PID)
   App sends firmware image in 2kB pages
   Each page: DFU_DNLOAD command + data + DFU_GETSTATUS poll
   After all pages: DFU_MANIFESTATION command
   Device reboots in normal mode
```

### `DfuConnectionDetector`

Watches for a device with a specific PID to appear after DFU detach:

```csharp
public sealed class DfuConnectionDetector
{
    /// <summary>
    /// Waits for a USB device with the given VID/DFU PID to appear.
    /// Called immediately after sending the DFU_DETACH command.
    /// Times out after 10 seconds.
    /// </summary>
    public Task<string> WaitForDfuDeviceAsync(
        ushort vendorId,
        ushort dfuProductId,
        CancellationToken ct);
}
```

### `DfuSession`

Performs the actual firmware download. Instrument projects call this
after `DfuConnectionDetector` has found the DFU-mode device:

```csharp
public sealed class DfuSession : IDisposable
{
    /// <param name="dfuDeviceId">Device path from DfuConnectionDetector</param>
    public DfuSession(string dfuDeviceId) { ... }

    /// <summary>
    /// Sends the firmware image to the device page by page.
    /// Progress 0.0 → 1.0 represents flashing progress.
    /// On completion the device reboots automatically.
    /// </summary>
    public async Task FlashAsync(
        byte[] firmwareImage,
        IProgress<double> progress,
        CancellationToken ct)
    {
        // DFU_DNLOAD loop with DFU_GETSTATUS polling between pages
        // Page size: 2048 bytes (configurable per device)
        // After last page: DFU_MANIFESTATION
    }
}
```

### Access to WinUSB from .NET

WinRT's `HidDevice` class is used for normal HID operation. For DFU mode,
the device re-enumerates as a WinUSB device. Options in order of preference:

1. **`Windows.Devices.Usb.UsbDevice`** (WinRT) — available in .NET 8 via
   `Microsoft.Windows.SDK.Contracts`. Preferred if the STM32 DFU descriptor
   is compatible. Inspect at implementation time.
2. **P/Invoke to `WinUsb.dll`** — fallback if WinRT USB doesn't support
   the DFU interface descriptor. More verbose but fully capable.

Claude Code must inspect which approach is viable based on the STM32 DFU
interface class (0xFE / 0x01 / 0x02) before choosing. Document the decision
in a comment in `DfuSession.cs`.

---

## No instrument-specific code

This project must not contain:
- Any VID, PID, or USB product string constants
- Any HID report format knowledge
- Any firmware image format knowledge
- Any reference to specific devices

---

## Unit tests

```
UsbHidTransportTests.cs
  ✓ UsbHidTransport_HasCorrectTransportId
  ✓ UsbHidTransport_HasExpectedCapabilities

UsbHidDeviceScannerTests.cs
  ✓ Scanner_RespectsTimeout
  ✓ Scanner_RespectsCancel

DfuSessionTests.cs
  ✓ FlashAsync_ProgressReachesOneOnCompletion (mock USB device)
  ✓ FlashAsync_ThrowsOnCancel
```

Note: full integration testing of DFU requires real hardware (WP0.19).
Unit tests here cover the state machine and progress reporting with mocks.

---

## What this work package explicitly does NOT do

- Implement any instrument-specific report parsing or commands
- Register any `IInstrumentPlugin` in the app
- Change the measurement workflow or any existing UI

---

## Acceptance criteria

1. `LevelApp.Instruments.UsbHid` project exists, compiles, is in solution
2. `UsbHidTransport`, `UsbHidDeviceScanner`, `UsbHidInstrumentProviderBase`
   implement their Core interfaces correctly
3. `DfuConnectionDetector` and `DfuSession` exist with correct signatures
4. The WinUSB access approach is documented in `DfuSession.cs` with rationale
5. No VID/PID or instrument-specific constants anywhere in this project
6. All existing tests pass; new transport tests pass

---

## Version bump

Set `AppVersion.Minor` → `18`, `AppVersion.Patch` → `0`. Commit message:

```
[v0.18.0] WP0.18: LevelApp.Instruments.UsbHid — USB HID transport + DFU subsystem
```
