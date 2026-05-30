# Work Package 0.14 — Extended Instrument Provider Interface

> Target version: **v0.14.0**
> Prerequisite: v0.13.2 ✓

---

## Goal

Extend `IInstrumentProvider` in `LevelApp.Core` with connection lifecycle and
capability flags. This is a pure interface and model change — no BLE, no new
projects. `ManualEntryProvider` is migrated to prove the new contract works.
`MeasurementView` gains a connection status indicator.

Every subsequent instrument work package (WP0.15+) depends on this being
stable.

---

## Changes to `LevelApp.Core`

### New enum: `InstrumentConnectionState`

```csharp
// LevelApp.Core/Instruments/InstrumentConnectionState.cs
public enum InstrumentConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Degraded,    // connected but signal/quality poor
    Error        // unrecoverable until reconnect attempted
}
```

### New flags enum: `InstrumentCapabilities`

```csharp
// LevelApp.Core/Instruments/InstrumentCapabilities.cs
[Flags]
public enum InstrumentCapabilities
{
    None             = 0,
    SingleMeasurement  = 1 << 0,  // supports one-shot GetReadingAsync()
    ContinuousStream   = 1 << 1,  // supports SubscribeToReadingsAsync()
}
```

### Updated `IInstrumentProvider`

```csharp
// LevelApp.Core/Interfaces/IInstrumentProvider.cs
public interface IInstrumentProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    InstrumentCapabilities Capabilities { get; }
    InstrumentConnectionState ConnectionState { get; }
    event EventHandler<InstrumentConnectionState> ConnectionStateChanged;

    /// <summary>
    /// Establish connection to the instrument. No-op for providers that
    /// are always connected (e.g. ManualEntry).
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Release the connection. No-op for always-connected providers.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Request a single reading. Valid when Capabilities includes
    /// SingleMeasurement. For ContinuousStream-only providers, this
    /// returns the most recently received value.
    /// </summary>
    Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct);
}
```

### Updated `ManualEntryProvider`

`ManualEntryProvider` implements the new interface trivially:

- `Capabilities` → `InstrumentCapabilities.SingleMeasurement`
- `ConnectionState` → always `InstrumentConnectionState.Connected`
- `ConnectionStateChanged` → never fires
- `ConnectAsync()` / `DisconnectAsync()` → no-op, return `Task.CompletedTask`
- `GetReadingAsync()` → unchanged (returns user-entered value)

---

## Changes to `LevelApp.App`

### `MeasurementView` — connection status indicator

Add a small status indicator to `MeasurementView.xaml`. It is visible only
when the active provider's `ConnectionState` is not `Connected`:

```xml
<!-- Shown only when ConnectionState != Connected -->
<InfoBar
    x:Name="ConnectionStatusBar"
    IsOpen="{x:Bind ViewModel.ShowConnectionWarning, Mode=OneWay}"
    Severity="{x:Bind ViewModel.ConnectionSeverity, Mode=OneWay}"
    Title="{x:Bind ViewModel.ConnectionStatusMessage, Mode=OneWay}"
    IsClosable="False"/>
```

### `MeasurementViewModel` additions

```csharp
// Derived from IInstrumentProvider.ConnectionState
public bool ShowConnectionWarning =>
    _provider.ConnectionState != InstrumentConnectionState.Connected;

public InfoBarSeverity ConnectionSeverity =>
    _provider.ConnectionState == InstrumentConnectionState.Error
        ? InfoBarSeverity.Error
        : InfoBarSeverity.Warning;

public string ConnectionStatusMessage =>
    _provider.ConnectionState switch
    {
        InstrumentConnectionState.Disconnected => "Instrument disconnected",
        InstrumentConnectionState.Connecting   => "Connecting to instrument…",
        InstrumentConnectionState.Degraded     => "Instrument signal degraded",
        InstrumentConnectionState.Error        => "Instrument connection error",
        _                                      => string.Empty
    };
```

Subscribe to `IInstrumentProvider.ConnectionStateChanged` in the ViewModel
constructor and call `OnPropertyChanged` for the above properties when it
fires.

The `AcceptReading` command should be disabled (not throw) when
`ConnectionState` is `Disconnected` or `Error` — add a guard to the
existing `CanExecute` logic.

---

## Unit tests

Add to `LevelApp.Tests`:

```
InstrumentProviderTests.cs
  ✓ ManualEntryProvider_IsAlwaysConnected
  ✓ ManualEntryProvider_HasSingleMeasurementCapability
  ✓ ManualEntryProvider_ConnectAsync_IsNoOp
  ✓ ManualEntryProvider_GetReadingAsync_ReturnsExpectedValue
```

---

## What this work package explicitly does NOT do

- Introduce `IInstrumentPlugin` (that is WP0.15)
- Add any BLE or USB code
- Change any data models, calculators, or persistence

---

## Acceptance criteria

1. `IInstrumentProvider` has `Capabilities`, `ConnectionState`,
   `ConnectionStateChanged`, `ConnectAsync()`, `DisconnectAsync()`
2. `ManualEntryProvider` compiles and passes all new unit tests
3. `MeasurementView` shows the `InfoBar` when `ConnectionState` is not
   `Connected` (verify by temporarily setting state to `Disconnected`
   in a debug build)
4. The app builds and all existing tests pass
5. No behaviour change visible to the user in normal operation

---

## Version bump

Set `AppVersion.Minor` → `14`, `AppVersion.Patch` → `0`. Commit message:

```
[v0.14.0] WP0.14: extended IInstrumentProvider — connection state and capabilities
```
