# Work Package 0.17 — BLE Transport Infrastructure

> Target version: **v0.17.0**
> Prerequisite: WP0.16 complete (v0.16.0) ✓

---

## Goal

Create `LevelApp.Instruments.BLE` — a reusable transport infrastructure
project. Provides `BleTransport`, `BleDeviceScanner`, and
`BleInstrumentProviderBase`. Contains no instrument-specific knowledge:
no GATT service UUIDs, no data parsing, no protocol details. All of that
lives in the concrete instrument project (WP0.18+).

Acceptance is proven with a loopback/mock test — no real hardware required.

---

## New project: `LevelApp.Instruments.BLE`

A .NET 8 class library. References:
- `LevelApp.Core` (for interfaces and models)
- `Microsoft.Windows.SDK.Contracts` (for WinRT BLE APIs without requiring
  a WinUI project)

Add to `LevelApp.slnx`.

```
LevelApp.Instruments.BLE/
├── BleTransport.cs
├── BleDeviceScanner.cs
├── BleInstrumentProviderBase.cs
└── Internal/
    └── BleConnectionManager.cs
```

---

## `BleTransport`

```csharp
// LevelApp.Instruments.BLE/BleTransport.cs
public sealed class BleTransport : ITransport
{
    public string TransportId  => "ble";
    public string DisplayName  => "Bluetooth";
    public TransportCapabilities Capabilities =>
        TransportCapabilities.SingleReading |
        TransportCapabilities.ContinuousStream |
        TransportCapabilities.Bidirectional;
}
```

---

## `BleDeviceScanner`

Scans for BLE advertisements that match a supplied filter. The filter is
provided by the concrete instrument project — `BleDeviceScanner` itself
has no hardcoded UUIDs.

```csharp
public sealed class BleDeviceScanner : IDeviceScanner
{
    public ITransport Transport => new BleTransport();

    /// <param name="serviceUuids">
    /// GATT service UUIDs to filter by. Pass an empty array to see all
    /// BLE advertisements (useful during development).
    /// </param>
    public BleDeviceScanner(Guid[] serviceUuids) { ... }

    public async IAsyncEnumerable<DeviceCandidate> ScanAsync(
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Uses Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher
        // Yields DeviceCandidate for each advertisement matching serviceUuids
        // Stops after timeout elapses or ct is cancelled
    }
}
```

---

## `BleInstrumentProviderBase`

Abstract base class. Handles all connection plumbing. Concrete instrument
providers override only the GATT interaction methods.

```csharp
public abstract class BleInstrumentProviderBase : IInstrumentProvider
{
    // ── IInstrumentProvider ──────────────────────────────────────────

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
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(_address);
            _gattSession = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
            _gattSession.MaintainConnection = true;

            await OnConnectedAsync(device, ct);   // override point

            SetState(InstrumentConnectionState.Connected);
            SubscribeToConnectionStatusChanged(device);
        }
        catch (OperationCanceledException) { SetState(InstrumentConnectionState.Disconnected); throw; }
        catch                              { SetState(InstrumentConnectionState.Error);         throw; }
    }

    public async Task DisconnectAsync()
    {
        await OnDisconnectingAsync();             // override point
        _gattSession?.Dispose();
        SetState(InstrumentConnectionState.Disconnected);
    }

    public abstract Task<double> GetReadingAsync(
        MeasurementStep step, CancellationToken ct);

    // ── Override points for concrete providers ───────────────────────

    /// Called once after GATT connection is established.
    /// Subscribe to characteristics, verify service UUIDs, etc.
    protected abstract Task OnConnectedAsync(
        BluetoothLEDevice device, CancellationToken ct);

    /// Called before disconnecting. Unsubscribe from notifications, etc.
    protected virtual Task OnDisconnectingAsync() => Task.CompletedTask;

    // ── Reconnection ─────────────────────────────────────────────────

    private void SubscribeToConnectionStatusChanged(BluetoothLEDevice device)
    {
        device.ConnectionStatusChanged += (sender, _) =>
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                SetState(InstrumentConnectionState.Disconnected);
                _ = TryReconnectAsync();
            }
        };
    }

    /// Exponential backoff reconnection. Retries indefinitely until
    /// connected or DisconnectAsync() is called.
    private async Task TryReconnectAsync()
    {
        var delay = TimeSpan.FromSeconds(1);
        while (ConnectionState == InstrumentConnectionState.Disconnected)
        {
            SetState(InstrumentConnectionState.Connecting);
            try
            {
                await ConnectAsync();
                return;
            }
            catch
            {
                SetState(InstrumentConnectionState.Disconnected);
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    protected void SetState(InstrumentConnectionState state)
    {
        if (ConnectionState == state) return;
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(this, state);
    }
}
```

### Reconnection policy

- On unexpected disconnect: retry immediately, then 2 s, 4 s, 8 s … up to
  30 s between retries
- Retries indefinitely — the operator may have walked away momentarily
- `MeasurementView` `InfoBar` reflects `Connecting` state during retries
- `DisconnectAsync()` cancels any pending reconnect

---

## `BleConnectionManager` (internal)

Manages the `GattSession` lifetime and the `MaintainConnection` flag.
Extracted from `BleInstrumentProviderBase` to keep it readable. Not part
of the public API.

---

## No instrument-specific code

This project must not contain:
- Any GATT service or characteristic UUIDs
- Any data parsing or unit conversion
- Any reference to Wyler, InclinationMeter, or any specific device

If a UUID or parsing detail is needed to write a test, use a randomly
generated mock UUID.

---

## Unit / integration tests

Add to `LevelApp.Tests` (or a new `LevelApp.Instruments.BLE.Tests` project
if mocking WinRT types proves difficult):

```
BleTransportTests.cs
  ✓ BleTransport_HasCorrectTransportId
  ✓ BleTransport_HasExpectedCapabilities

BleDeviceScannerTests.cs
  ✓ BleDeviceScanner_RespectsTimeout
  ✓ BleDeviceScanner_RespectsCancel
  ✓ BleDeviceScanner_FiltersById (mock advertisement watcher)

BleInstrumentProviderBaseTests.cs
  ✓ ConcreteProvider_InitialState_IsDisconnected
  ✓ ConcreteProvider_ConnectionStateChanged_FiresOnTransition
  ✓ ConcreteProvider_ReconnectsAfterUnexpectedDisconnect (mock device)
```

A minimal concrete test provider extending `BleInstrumentProviderBase`
with a mock `BluetoothLEDevice` should be sufficient. If WinRT mocking is
too complex in the test project, defer the integration tests to WP0.18
when real hardware is available and cover only the state machine logic
with pure unit tests here.

---

## What this work package explicitly does NOT do

- Implement any instrument-specific GATT interaction
- Register any `IInstrumentPlugin` in the app
- Change the measurement workflow or UI beyond what WP0.14 already added

---

## Acceptance criteria

1. `LevelApp.Instruments.BLE` project exists, compiles, and is in the
   solution
2. `BleTransport`, `BleDeviceScanner`, `BleInstrumentProviderBase` are
   all public and implement their respective Core interfaces
3. `BleDeviceScanner` respects timeout and cancellation (verified by test)
4. `BleInstrumentProviderBase` state machine transitions are correct
   (Disconnected → Connecting → Connected → Disconnected → Connecting…)
5. No GATT UUIDs or instrument-specific constants anywhere in this project
6. All existing tests pass

---

## Version bump

Set `AppVersion.Minor` → `17`, `AppVersion.Patch` → `0`. Commit message:

```
[v0.17.0] WP0.17: LevelApp.Instruments.BLE — transport infrastructure
```
