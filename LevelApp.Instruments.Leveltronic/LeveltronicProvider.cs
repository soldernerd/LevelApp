using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.Protocol;
using LevelApp.Instruments.Leveltronic.Transport;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// <see cref="IInstrumentProvider"/> for the Leveltronic electronic level, over
/// USB HID or BLE, on a shared <see cref="LeveltronicSession"/>. Implements
/// <see cref="ILiveReadingSource"/>: live S1 / S2 / Diff reading, settled
/// precision measurement and the app-side "Zero Diff" offset, all in µm/m (the
/// unit the guided measurement works in; the instrument reports mm/m).
/// </summary>
public sealed class LeveltronicProvider : IInstrumentProvider, ILiveReadingSource, IInstrumentDetails, IAsyncDisposable
{
    public const string ChannelS1 = "s1";
    public const string ChannelS2 = "s2";
    public const string ChannelDiff = "diff";

    private static readonly TimeSpan ReconnectInitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(30);

    private readonly KnownDevice _device;
    private readonly LeveltronicSessionManager _sessions;
    private readonly ILeveltronicPreferences _prefs;
    private readonly TimeProvider _time;
    private readonly bool _autoReconnect;

    private SessionLease? _lease;
    private CancellationTokenSource? _reconnectCts;
    private InstrumentConnectionState _state = InstrumentConnectionState.Disconnected;
    private string _channel = ChannelDiff;

    public LeveltronicProvider(
        KnownDevice device, LeveltronicSessionManager sessions, ILeveltronicPreferences prefs,
        TimeProvider? time = null, bool? autoReconnect = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
        _time = time ?? TimeProvider.System;
        // BLE links drop and come back on their own; a USB cable does not.
        _autoReconnect = autoReconnect ?? device.TransportId == LeveltronicLinkFactory.BleTransportId;
    }

    // ── IInstrumentProvider ──────────────────────────────────────────────────

    public string ProviderId => $"leveltronic:{_device.DeviceId}";
    public string DisplayName => _device.DisplayName;
    public InstrumentCapabilities Capabilities =>
        InstrumentCapabilities.SingleMeasurement | InstrumentCapabilities.ContinuousStream;

    public InstrumentConnectionState ConnectionState
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            ConnectionStateChanged?.Invoke(this, value);
            LiveReadingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler<InstrumentConnectionState>? ConnectionStateChanged;

    /// <summary>The open session while connected (null otherwise).</summary>
    public LeveltronicSession? Session => _lease?.Session;

    /// <inheritdoc/>
    public string? Details => Session is { } s
        ? $"serial {s.Serial} \u00b7 firmware {s.Identity.Firmware} \u00b7 API {s.Identity.ApiVersionText}"
        : null;

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
        await ReleaseAsync().ConfigureAwait(false);
        ConnectionState = InstrumentConnectionState.Disconnected;
    }

    public Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct) =>
        MeasureAsync(null, ct);

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    // ── ILiveReadingSource ───────────────────────────────────────────────────

    public IReadOnlyList<ReadingChannel> Channels { get; } =
    [
        new(ChannelS1, "S1"),
        new(ChannelS2, "S2"),
        new(ChannelDiff, "Diff (S1 − S2)"),
    ];

    public string SelectedChannelId
    {
        get => _channel;
        set
        {
            if (value == _channel || Channels.All(c => c.Id != value)) return;
            _channel = value;
            if (Session is { } s) _prefs.SetChannel(s.Serial, value);
            LiveReadingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? LiveReadingChanged;

    public LiveReading? Latest
    {
        get
        {
            if (Session?.Feed.State.Live is not { } live || ConnectionState != InstrumentConnectionState.Connected)
                return null;
            double mmPerM = _channel switch
            {
                ChannelS1 => live.TiltS1,
                ChannelS2 => live.TiltS2,
                _ => live.TiltDiff - (Tare ?? 0),
            };
            bool doubtful = _channel switch
            {
                ChannelS1 => live.S1Doubtful,
                ChannelS2 => live.S2Doubtful,
                _ => live.AnyDoubtful,
            };
            return new LiveReading(mmPerM * 1000.0, live.Valid, doubtful);
        }
    }

    public async Task<double> MeasureAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var status = await RunPrecisionAsync(progress, ct).ConfigureAwait(false);
        double mmPerM = _channel switch
        {
            ChannelS1 => status.TiltS1,
            ChannelS2 => status.TiltS2,
            _ => status.TiltDiff - (Tare ?? 0),
        };
        return mmPerM * 1000.0;
    }

    public bool SupportsZero => _channel == ChannelDiff;
    public bool IsZeroed => Tare is not null;
    public double? ZeroOffsetMicronsPerMetre => Tare * 1000.0;

    public async Task ZeroAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (!SupportsZero || Session is not { } s) return;
        var status = await RunPrecisionAsync(progress, ct).ConfigureAwait(false);
        _prefs.SetTare(s.Serial, status.TiltDiff);
        LiveReadingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearZero()
    {
        if (Session is not { } s) return;
        _prefs.SetTare(s.Serial, null);
        LiveReadingChanged?.Invoke(this, EventArgs.Empty);
    }

    private double? Tare => Session is { } s ? _prefs.GetTare(s.Serial) : null;

    private async Task<PrecisionStatus> RunPrecisionAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var session = Session ?? throw new InvalidOperationException("The Leveltronic is not connected.");
        var run = new PrecisionRun(session.Api, _time);
        if (progress is not null)
            run.Progress += (_, s) =>
            {
                if (s.Phase == PrecisionPhase.Running && s.Target > 0)
                    progress.Report(Math.Min(1.0, (double)s.Count / s.Target));
            };

        using var reg = ct.Register(() => _ = run.CancelAsync());
        PrecisionStatus result;
        try
        {
            result = await run.StartAsync().ConfigureAwait(false);
        }
        catch (PrecisionCancelledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (ApiException ex)
        {
            throw new InstrumentMeasurementException(ex.UserMessage);
        }
        catch (LinkException ex)
        {
            throw new InstrumentMeasurementException(ex.Message);
        }

        if (result.Failed)
            throw new InstrumentMeasurementException(
                "No quiet 2 s window within 5 s. Keep the table still and measure again.");
        progress?.Report(1.0);
        return result;
    }

    // ── Connection internals ─────────────────────────────────────────────────

    private async Task OpenOnceAsync(CancellationToken ct)
    {
        await ReleaseAsync().ConfigureAwait(false);

        var lease = await _sessions.AcquireAsync(_device, ct).ConfigureAwait(false);
        var session = lease.Session;
        session.Closed += OnSessionClosed;
        session.Feed.Updated += OnFeedUpdated;
        _lease = lease;

        if (_prefs.GetChannel(session.Serial) is { } saved && Channels.Any(c => c.Id == saved))
            _channel = saved;
    }

    private async Task ReleaseAsync()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is null) return;
        lease.Session.Closed -= OnSessionClosed;
        lease.Session.Feed.Updated -= OnFeedUpdated;
        await lease.DisposeAsync().ConfigureAwait(false);
    }

    private void OnFeedUpdated(object? sender, InstrumentSnapshot e) =>
        LiveReadingChanged?.Invoke(this, EventArgs.Empty);

    private void OnSessionClosed(object? sender, EventArgs e)
    {
        if (ConnectionState is InstrumentConnectionState.Disconnected) return;

        _ = ReleaseAsync();
        if (_autoReconnect && _reconnectCts is { IsCancellationRequested: false } cts)
        {
            ConnectionState = InstrumentConnectionState.Connecting;
            _ = Task.Run(() => ReconnectLoopAsync(cts.Token));
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
                try { await Task.Delay(delay, _time, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, ReconnectMaxDelay.Ticks));
                if (!ct.IsCancellationRequested)
                    ConnectionState = InstrumentConnectionState.Connecting;
            }
        }
    }
}
