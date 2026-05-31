using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;

namespace LevelApp.Instruments.UsbHid;

/// <summary>
/// Abstract base class for USB HID reading providers.
/// <para>
/// Manages device open/close and input-report subscription.  USB connections
/// are inherently stable while the device is plugged in, so this base class
/// does not implement exponential-backoff reconnection (contrast with
/// <see cref="LevelApp.Instruments.BLE.BleInstrumentProviderBase"/>).
/// If the device is unplugged the state transitions to
/// <see cref="InstrumentConnectionState.Error"/> and the operator must
/// reconnect manually.
/// </para>
/// <para>
/// Subclasses supply instrument-specific behaviour via
/// <see cref="OnConnectedAsync"/>, <see cref="OnDisconnectingAsync"/>,
/// <see cref="OnInputReportReceived"/>, and <see cref="GetReadingAsync"/>.
/// </para>
/// </summary>
public abstract class UsbHidInstrumentProviderBase : IInstrumentProvider
{
    // ── Fields ─────────────────────────────────────────────────────────────────

    private readonly string _deviceId;
    private HidDevice? _device;

    // ── IInstrumentProvider ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public abstract string ProviderId { get; }

    /// <inheritdoc/>
    public abstract string DisplayName { get; }

    /// <inheritdoc/>
    public abstract InstrumentCapabilities Capabilities { get; }

    /// <inheritdoc/>
    public InstrumentConnectionState ConnectionState { get; private set; }
        = InstrumentConnectionState.Disconnected;

    /// <inheritdoc/>
    public event EventHandler<InstrumentConnectionState>? ConnectionStateChanged;

    // ── Construction ───────────────────────────────────────────────────────────

    /// <param name="deviceId">
    /// The device-interface path obtained from a <see cref="UsbHidDeviceScanner"/>
    /// scan (i.e. <see cref="Core.Instruments.DeviceCandidate.CandidateId"/>).
    /// This is passed directly to <see cref="HidDevice.FromIdAsync"/>.
    /// </param>
    protected UsbHidInstrumentProviderBase(string deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        _deviceId = deviceId;
    }

    // ── Connection management ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (ConnectionState == InstrumentConnectionState.Connected) return;

        SetState(InstrumentConnectionState.Connecting);
        try
        {
            ct.ThrowIfCancellationRequested();

            _device = await HidDevice
                .FromIdAsync(_deviceId, FileAccessMode.ReadWrite)
                .AsTask(ct)
                .ConfigureAwait(false);

            if (_device is null)
                throw new InvalidOperationException(
                    $"HID device '{_deviceId}' not found or access denied.");

            _device.InputReportReceived += OnInputReportReceived;

            await OnConnectedAsync(_device, ct).ConfigureAwait(false);

            SetState(InstrumentConnectionState.Connected);
        }
        catch (OperationCanceledException)
        {
            SetState(InstrumentConnectionState.Disconnected);
            throw;
        }
        catch
        {
            SetState(InstrumentConnectionState.Error);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        if (_device is not null)
            _device.InputReportReceived -= OnInputReportReceived;

        await OnDisconnectingAsync().ConfigureAwait(false);

        _device?.Dispose();
        _device = null;

        SetState(InstrumentConnectionState.Disconnected);
    }

    /// <inheritdoc/>
    public abstract Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct);

    // ── Override points ────────────────────────────────────────────────────────

    /// <summary>
    /// Called immediately after the <see cref="HidDevice"/> is opened and
    /// before the connection state changes to <see cref="InstrumentConnectionState.Connected"/>.
    /// Use to discover reports, send initialisation commands, etc.
    /// </summary>
    protected abstract Task OnConnectedAsync(HidDevice device, CancellationToken ct);

    /// <summary>
    /// Called at the start of <see cref="DisconnectAsync"/> while the device
    /// is still open.  Override to send a graceful shutdown command.
    /// Default implementation is a no-op.
    /// </summary>
    protected virtual Task OnDisconnectingAsync() => Task.CompletedTask;

    /// <summary>
    /// Called by the WinRT HID stack for every input report received from the
    /// device.  Implementations should parse the report and store the value for
    /// retrieval by <see cref="GetReadingAsync"/>.
    /// </summary>
    protected abstract void OnInputReportReceived(
        HidDevice sender, HidInputReportReceivedEventArgs args);

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Changes <see cref="ConnectionState"/> and fires
    /// <see cref="ConnectionStateChanged"/> if the value is new.
    /// </summary>
    protected void SetState(InstrumentConnectionState state)
    {
        if (ConnectionState == state) return;
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(this, state);
    }
}
