using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using LevelApp.Instruments.BLE.Internal;
using Windows.Devices.Bluetooth;

namespace LevelApp.Instruments.BLE;

/// <summary>
/// Abstract base class for BLE-backed instrument providers.
/// <para>
/// Handles the connection state machine and automatic reconnection with
/// exponential backoff (1 s → 2 s → 4 s … capped at 30 s, retries indefinitely).
/// Subclasses supply instrument-specific logic via
/// <see cref="DoConnectAsync"/> and <see cref="DoDisconnectAsync"/>.
/// </para>
/// </summary>
public abstract class BleInstrumentProviderBase : IInstrumentProvider
{
    // ── Fields ─────────────────────────────────────────────────────────────────

    private readonly ulong _bluetoothAddress;

    private InstrumentConnectionState _state = InstrumentConnectionState.Disconnected;
    private CancellationTokenSource?  _cts;

    // ── IInstrumentProvider ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public abstract string ProviderId { get; }

    /// <inheritdoc/>
    public abstract string DisplayName { get; }

    /// <inheritdoc/>
    public abstract InstrumentCapabilities Capabilities { get; }

    /// <inheritdoc/>
    public InstrumentConnectionState ConnectionState
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            ConnectionStateChanged?.Invoke(this, value);
        }
    }

    /// <inheritdoc/>
    public event EventHandler<InstrumentConnectionState>? ConnectionStateChanged;

    // ── Construction ───────────────────────────────────────────────────────────

    /// <param name="bluetoothAddress">
    /// The 48-bit BLE address of the target device (as seen in scan results).
    /// </param>
    protected BleInstrumentProviderBase(ulong bluetoothAddress)
    {
        _bluetoothAddress = bluetoothAddress;
    }

    // ── Connection management ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (ConnectionState == InstrumentConnectionState.Connected) return;

        ConnectionState = InstrumentConnectionState.Connecting;

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // ConnectAsync returns after the first successful connection attempt.
        // The background reconnect loop (OnUnexpectedDisconnect) takes over later.
        await AttemptConnectWithBackoffAsync(_cts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync()
    {
        // Stop any in-progress connect or background reconnect loop
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _cts = null;
        }

        await DoDisconnectAsync().ConfigureAwait(false);
        ConnectionState = InstrumentConnectionState.Disconnected;
    }

    /// <summary>
    /// Called by the subclass when the peripheral disconnects unexpectedly.
    /// Starts a background reconnect loop with exponential backoff.
    /// </summary>
    protected void OnUnexpectedDisconnect()
    {
        if (ConnectionState != InstrumentConnectionState.Connected) return;

        ConnectionState = InstrumentConnectionState.Connecting;

        // Cancel any leftover CTS and create a fresh one for the reconnect loop
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        _ = Task.Run(() => ReconnectLoopAsync(_cts.Token));
    }

    // ── Backoff loop ───────────────────────────────────────────────────────────

    private static readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maxDelay     = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Attempts to connect and, on failure, retries with exponential backoff.
    /// Returns only after a successful connection (or on cancellation).
    /// </summary>
    private async Task AttemptConnectWithBackoffAsync(CancellationToken ct)
    {
        var delay = _initialDelay;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await DoConnectAsync(_bluetoothAddress, ct).ConfigureAwait(false);
                ConnectionState = InstrumentConnectionState.Connected;
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                ConnectionState = InstrumentConnectionState.Error;

                await Task.Delay(delay, ct).ConfigureAwait(false);

                delay = TimeSpan.FromTicks(
                    Math.Min(delay.Ticks * 2, _maxDelay.Ticks));

                ConnectionState = InstrumentConnectionState.Connecting;
            }
        }
    }

    /// <summary>
    /// Background reconnect loop (used after an unexpected disconnect).
    /// Identical backoff logic to <see cref="AttemptConnectWithBackoffAsync"/>
    /// but silently exits on cancellation rather than throwing.
    /// </summary>
    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        var delay = _initialDelay;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DoConnectAsync(_bluetoothAddress, ct).ConfigureAwait(false);
                ConnectionState = InstrumentConnectionState.Connected;
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                ConnectionState = InstrumentConnectionState.Error;

                try   { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                delay = TimeSpan.FromTicks(
                    Math.Min(delay.Ticks * 2, _maxDelay.Ticks));

                if (!ct.IsCancellationRequested)
                    ConnectionState = InstrumentConnectionState.Connecting;
            }
        }
    }

    // ── Subclass hooks ─────────────────────────────────────────────────────────

    /// <summary>
    /// Perform the actual BLE connection for the given <paramref name="address"/>.
    /// Should open a <see cref="BleConnectionManager"/>, discover required GATT
    /// services and characteristics, and subscribe to notifications.
    /// Throw any exception to indicate failure; the base class will retry.
    /// </summary>
    protected abstract Task DoConnectAsync(ulong address, CancellationToken ct);

    /// <summary>
    /// Release all BLE resources (GATT session, device handle, etc.).
    /// Called by <see cref="DisconnectAsync"/>; must not throw.
    /// </summary>
    protected abstract Task DoDisconnectAsync();

    /// <inheritdoc/>
    public abstract Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct);
}
