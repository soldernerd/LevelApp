using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using LevelApp.Instruments.Leveltronic.Protocol;
using LevelApp.Instruments.Leveltronic.Transport;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// <see cref="IInstrumentProvider"/> for the Leveltronic electronic level.
/// Drives the device over USB HID or BLE (chosen from
/// <see cref="KnownDevice.TransportId"/>) using the shared API v2 protocol.
/// </summary>
public sealed class LeveltronicProvider : IInstrumentProvider
{
    private static readonly TimeSpan ReconnectInitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(30);

    private readonly KnownDevice _device;
    private readonly Func<ILeveltronicLink> _linkFactory;
    private readonly bool _autoReconnect;

    private ILeveltronicLink? _link;
    private LeveltronicDeviceClient? _client;
    private CancellationTokenSource? _reconnectCts;
    private InstrumentConnectionState _state = InstrumentConnectionState.Disconnected;

    /// <param name="device">The registered device to drive.</param>
    public LeveltronicProvider(KnownDevice device)
        : this(device,
               () => LeveltronicLinkFactory.Create(device),
               autoReconnect: device is not null && device.TransportId == LeveltronicLinkFactory.BleTransportId)
    {
    }

    /// <summary>Test seam: inject the link and reconnect policy.</summary>
    internal LeveltronicProvider(KnownDevice device, Func<ILeveltronicLink> linkFactory, bool autoReconnect)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _linkFactory = linkFactory ?? throw new ArgumentNullException(nameof(linkFactory));
        _autoReconnect = autoReconnect;
    }

    public string ProviderId => $"leveltronic:{_device.DeviceId}";
    public string DisplayName => _device.DisplayName;
    public InstrumentCapabilities Capabilities => InstrumentCapabilities.SingleMeasurement;

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

    public event EventHandler<InstrumentConnectionState>? ConnectionStateChanged;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (ConnectionState == InstrumentConnectionState.Connected) return;

        ConnectionState = InstrumentConnectionState.Connecting;
        _reconnectCts?.Dispose();
        _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await OpenOnceAsync(ct).ConfigureAwait(false);
            ConnectionState = InstrumentConnectionState.Connected;
        }
        catch
        {
            ConnectionState = InstrumentConnectionState.Error;
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_reconnectCts is not null)
        {
            await _reconnectCts.CancelAsync().ConfigureAwait(false);
            _reconnectCts.Dispose();
            _reconnectCts = null;
        }

        await TeardownLinkAsync().ConfigureAwait(false);
        ConnectionState = InstrumentConnectionState.Disconnected;
    }

    public async Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct)
    {
        var client = _client
            ?? throw new InvalidOperationException("Leveltronic provider is not connected.");
        return await client.GetInclinationMicronsPerMetreAsync(ct).ConfigureAwait(false);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task OpenOnceAsync(CancellationToken ct)
    {
        await TeardownLinkAsync().ConfigureAwait(false);

        var link = _linkFactory();
        link.ConnectionChanged += OnLinkConnectionChanged;
        await link.ConnectAsync(ct).ConfigureAwait(false);

        var client = new LeveltronicDeviceClient(link);
        // Identity read doubles as a connection handshake.
        await client.GetIdentityAsync(ct).ConfigureAwait(false);

        _link = link;
        _client = client;
    }

    private async Task TeardownLinkAsync()
    {
        if (_link is not null)
            _link.ConnectionChanged -= OnLinkConnectionChanged;

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false); // disposes the link too
            _client = null;
            _link = null;
        }
        else if (_link is not null)
        {
            await _link.DisposeAsync().ConfigureAwait(false);
            _link = null;
        }
    }

    private void OnLinkConnectionChanged(object? sender, bool connected)
    {
        if (connected) return;
        if (ConnectionState is InstrumentConnectionState.Disconnected) return;

        if (_autoReconnect && _reconnectCts is { IsCancellationRequested: false })
        {
            ConnectionState = InstrumentConnectionState.Connecting;
            _ = Task.Run(() => ReconnectLoopAsync(_reconnectCts.Token));
        }
        else
        {
            ConnectionState = InstrumentConnectionState.Error;
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        var delay = ReconnectInitialDelay;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await OpenOnceAsync(ct).ConfigureAwait(false);
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
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, ReconnectMaxDelay.Ticks));
                if (!ct.IsCancellationRequested)
                    ConnectionState = InstrumentConnectionState.Connecting;
            }
        }
    }

}
