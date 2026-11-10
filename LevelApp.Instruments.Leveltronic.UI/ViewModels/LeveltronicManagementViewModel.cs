using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>
/// Backs <c>LeveltronicManagementView</c> — everything the phone app's Info,
/// Settings, Actions, Calibration-values and Diagnostics screens offer, on a
/// shared <see cref="LeveltronicSession"/>.
/// </summary>
public sealed partial class LeveltronicManagementViewModel : ObservableObject, IAsyncDisposable
{
    private const string PluginId = "leveltronic";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly LeveltronicSessionManager _sessions;
    private readonly IDeviceRegistry _registry;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private SessionLease? _lease;
    private IDisposable? _keepAlive;
    private IDisposable? _diagSub;
    private CancellationTokenSource? _pollCts;
    private KnownDevice? _target;

    // Info rows (fixed layout, values updated in place)
    private readonly Dictionary<string, InfoRow> _info = [];

    public LeveltronicManagementViewModel(LeveltronicSessionManager sessions, IDeviceRegistry registry)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        InfoGroups = BuildInfoGroups();
        SettingGroups = BuildGroups(ValueCatalog.Settings);
        CalibrationGroups = BuildGroups(ValueCatalog.Calibrations);
        Actions = BuildActions();
        foreach (var n in SignalDiag.ChannelNames) DiagChannels.Add(new DiagChannelRow(n));

        UpdateTargetDevice();
    }

    /// <summary>Set by the view: shows a confirm dialog, returns true if the user agreed.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    // ── Observable state ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    private bool _isBusy;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanConnect))] private bool _hasDevice;
    [ObservableProperty] private string _deviceName = "No Leveltronic registered";
    [ObservableProperty] private string _statusMessage = "Not connected.";
    [ObservableProperty] private bool _statusIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceTitle))]
    [NotifyPropertyChangedFor(nameof(ServiceText))]
    private bool _serviceMode;

    [ObservableProperty] private string _theoreticalTiltText = "";

    public bool CanConnect => HasDevice && !IsConnected && !IsBusy;
    public bool CanOperate => IsConnected && !IsBusy;

    public string ServiceTitle => ServiceMode ? "Service mode is on" : "Service mode is off";
    public string ServiceText => ServiceMode
        ? "Calibration values can be written. It ends by itself after 10 minutes without a request, or when the instrument sleeps."
        : "Writing calibration values and the maintenance actions need service mode. Switch it on at the instrument: SETTINGS > Service mode, press the right knob twice.";

    public IReadOnlyList<InfoGroup> InfoGroups { get; }
    public ObservableCollection<ValueGroup> SettingGroups { get; }
    public ObservableCollection<ValueGroup> CalibrationGroups { get; }
    public IReadOnlyList<ActionItem> Actions { get; }
    public ObservableCollection<DiagChannelRow> DiagChannels { get; } = [];

    /// <summary>The Info tab is showing (RTC and HEALTH are polled only then).</summary>
    public bool InfoVisible { get; set; }

    private bool _diagVisible;

    /// <summary>The Diagnostics tab is showing (SIGNAL_DIAG is subscribed only then).</summary>
    public bool DiagnosticsVisible
    {
        get => _diagVisible;
        set
        {
            _diagVisible = value;
            SyncDiagSubscription();
        }
    }

    private LeveltronicSession? Session => _lease?.Session;

    // ── Target device ────────────────────────────────────────────────────────

    /// <summary>Re-read which device this view targets (call when returning to the tab).</summary>
    public void UpdateTargetDevice()
    {
        _target = _registry.GetPreferredDevice(PluginId)
               ?? _registry.GetKnownDevices(PluginId).FirstOrDefault();
        HasDevice = _target is not null;
        if (!IsConnected) DeviceName = _target?.DisplayName ?? "No Leveltronic registered";
        ConnectCommand.NotifyCanExecuteChanged();
    }

    // ── Connect / disconnect ─────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (_target is null) return;
        IsBusy = true;
        Report("Connecting…", false);
        try
        {
            var lease = await _sessions.AcquireAsync(_target);
            _lease = lease;
            var s = lease.Session;
            s.Closed += OnSessionClosed;
            s.Feed.Updated += OnFeedUpdated;
            _keepAlive = s.KeepServiceModeAlive();

            DeviceName = $"{_target.DisplayName} · {s.Identity.Serial}";
            IsConnected = true;
            ApplySnapshot(s.Feed.State);
            FillIdentity(s.Identity);
            _pollCts = new CancellationTokenSource();
            _ = PollAsync(s, _pollCts.Token);
            SyncDiagSubscription();
            Report($"Connected to {_target.DisplayName}.", false);
        }
        catch (Exception ex)
        {
            Report(ex is ApiException api ? api.UserMessage : ex.Message, true);
            await ReleaseAsync();
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }

        if (IsConnected) await RefreshAllValuesAsync();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await ReleaseAsync();
        Report("Disconnected.", false);
    }

    private async Task ReleaseAsync()
    {
        _pollCts?.Cancel();
        _pollCts = null;
        _diagSub?.Dispose();
        _diagSub = null;
        _keepAlive?.Dispose();
        _keepAlive = null;

        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is not null)
        {
            lease.Session.Closed -= OnSessionClosed;
            lease.Session.Feed.Updated -= OnFeedUpdated;
            await lease.DisposeAsync();
        }
        Post(() =>
        {
            IsConnected = false;
            ServiceMode = false;
            foreach (var row in AllRows()) row.DeviceValue = null;
            foreach (var r in _info.Values) r.Value = "—";
            NotifyCommands();
        });
    }

    private void OnSessionClosed(object? sender, EventArgs e) => Post(() =>
    {
        _ = ReleaseAsync();
        Report("Lost contact with the instrument.", true);
    });

    private void OnFeedUpdated(object? sender, InstrumentSnapshot s) => Post(() => ApplySnapshot(s));

    // ── Info tab ─────────────────────────────────────────────────────────────

    private IReadOnlyList<InfoGroup> BuildInfoGroups()
    {
        InfoRow R(string key, string label)
        {
            var row = new InfoRow(label);
            _info[key] = row;
            return row;
        }

        return
        [
            new("Battery", R("soc", "Charge"), R("mv", "Voltage"), R("bstate", "State"), R("usb", "USB power"),
                R("charging", "Charging"), R("force", "Forced charge armed"), R("inhibit", "Charging inhibited")),
            new("Temperature", R("tonboard", "On-board (TMP236)"), R("tbme", "Ambient (BME280)"), R("text", "External (LM35)")),
            new("Environment", R("hum", "Humidity"), R("press", "Pressure")),
            new("Clock", R("rtc", "Instrument time"), R("pc", "PC time"), R("drift", "Difference")),
            new("Instrument", R("product", "Product"), R("serial", "Serial"), R("fw", "Firmware"),
                R("build", "Build"), R("api", "API version")),
            new("Activity", R("demod", "Tilt measurement running"), R("phasor", "Phasor stream active"),
                R("bulk", "Bulk capture active"), R("service", "Service mode"), R("r33", "3.3 V rail"), R("r5", "5 V rail")),
            new("Health", R("uptime", "Uptime"), R("reset", "Last reset"), R("eeprom", "Settings storage"),
                R("adc", "ADC / ADS / DAC"), R("disp", "Display / BME280"), R("drops", "Dropped frames (USB/BLE/UART)"),
                R("malformed", "Malformed requests"), R("fault", "Last fault")),
        ];
    }

    private static string YesNo(bool? b) => b is null ? "—" : b.Value ? "yes" : "no";
    private static string Ok(bool b) => b ? "ok" : "FAILED";

    private void ApplySnapshot(InstrumentSnapshot snap)
    {
        if (snap.Status is { } st)
        {
            ServiceMode = st.ServiceMode;
            _info["soc"].Value = $"{st.BatterySocPct} %";
            _info["mv"].Value = $"{st.BatteryMv / 1000.0:0.00} V";
            _info["bstate"].Value = st.Battery.ToString();
            _info["usb"].Value = YesNo(st.UsbConnected);
            _info["charging"].Value = YesNo(st.Charging);
            _info["force"].Value = YesNo(st.ForceCharging);
            _info["inhibit"].Value = YesNo(st.ChargeInhibited);
            _info["demod"].Value = YesNo(st.DemodRunning);
            _info["phasor"].Value = YesNo(st.PhasorStreamActive);
            _info["bulk"].Value = YesNo(st.BulkActive);
            _info["service"].Value = YesNo(st.ServiceMode);
            _info["r33"].Value = YesNo(st.Rail3v3On);
            _info["r5"].Value = YesNo(st.Rail5vOn);
        }
        if (snap.Env is { } env)
        {
            _info["tonboard"].Value = $"{env.OnboardTempC:0.0} °C";
            _info["tbme"].Value = env.BmeOk ? $"{env.BmeTempC:0.0} °C" : "not available";
            _info["text"].Value = env.ExternalTempOk ? $"{env.ExternalTempC:0.0} °C" : "not connected";
            _info["hum"].Value = env.BmeOk ? $"{env.HumidityPct:0} %" : "not available";
            _info["press"].Value = env.BmeOk ? $"{env.PressureHpa:0.0} hPa" : "not available";
        }
    }

    private void FillIdentity(DeviceIdentity id)
    {
        _info["product"].Value = id.Product;
        _info["serial"].Value = id.Serial;
        _info["fw"].Value = id.Firmware;
        _info["build"].Value = id.Build;
        _info["api"].Value = id.ApiVersionText;
    }

    private void ApplyRtc(RtcInfo rtc)
    {
        _info["rtc"].Value = rtc.IsSet ? rtc.DateTime.ToString("yyyy-MM-dd HH:mm:ss", Inv) : "not set";
        var pc = DateTime.Now;
        _info["pc"].Value = pc.ToString("yyyy-MM-dd HH:mm:ss", Inv);
        _info["drift"].Value = rtc.IsSet ? FormatDrift(rtc.DateTime - pc) : "—";
    }

    private static string FormatDrift(TimeSpan d)
    {
        double s = d.TotalSeconds;
        return Math.Abs(s) < 60 ? $"{s:+0.0;-0.0;0.0} s" : $"{d.TotalMinutes:+0.0;-0.0;0.0} min";
    }

    private void ApplyHealth(HealthInfo h)
    {
        _info["uptime"].Value = FormatUptime(h.UptimeS);
        _info["reset"].Value = h.ResetCauseNames.Count == 0 ? "—" : string.Join(", ", h.ResetCauseNames);
        _info["eeprom"].Value = (h.EepromSelfTest switch { 1 => "self-test passed", 2 => "self-test FAILED", _ => "self-test not run" })
                                + (h.SettingsSaveFailed ? ", a save failed" : "");
        _info["adc"].Value = $"{Ok(h.AdcOk)} / {Ok(h.AdsOk)} / {Ok(h.DacOk)}";
        _info["disp"].Value = $"{Ok(h.DisplayOk)} / {Ok(h.BmeOk)}";
        _info["drops"].Value = $"{h.UsbTxDropped} / {h.BleTxDropped} / {h.UartTxDropped}";
        _info["malformed"].Value = h.RxMalformed.ToString(Inv);
        _info["fault"].Value = h.LastFaultKind switch
        {
            0 => "none",
            1 => $"HardFault at 0x{h.LastFaultPc:X8} (LR 0x{h.LastFaultLr:X8})",
            _ => "initialisation failure",
        };
    }

    private static string FormatUptime(uint s)
    {
        var t = TimeSpan.FromSeconds(s);
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h {t.Minutes} min"
             : t.TotalHours >= 1 ? $"{t.Hours} h {t.Minutes} min" : $"{t.Minutes} min {t.Seconds} s";
    }

    /// <summary>RTC every second and HEALTH every five while the Info tab is showing (as the phone app).</summary>
    private async Task PollAsync(LeveltronicSession s, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        int tick = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!InfoVisible) continue;
                try
                {
                    var rtc = await s.Api.RtcAsync(ct);
                    HealthInfo? health = null;
                    if (tick++ % 5 == 0) health = await s.Api.HealthAsync(ct);
                    Post(() =>
                    {
                        ApplyRtc(rtc);
                        if (health is not null) ApplyHealth(health);
                    });
                }
                catch (Exception ex) when (ex is ApiException or LinkException) { /* try again next tick */ }
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task SetClockToPcAsync() => RunAsync("Setting the clock…", async s =>
    {
        await s.Api.SetRtcAsync(DateTime.Now);
        ApplyRtc(await s.Api.RtcAsync());
        Report("Instrument clock set to PC time.", false);
    });

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task LeaveServiceModeAsync() => RunAsync("Leaving service mode…", async s =>
    {
        await s.Api.ServiceEndAsync();
        Report("Service mode is off.", false);
    });

    // ── Settings / calibration values ────────────────────────────────────────

    private ObservableCollection<ValueGroup> BuildGroups(IReadOnlyList<ValueDef> defs)
    {
        var groups = new ObservableCollection<ValueGroup>();
        foreach (var g in defs.GroupBy(d => d.Group))
            groups.Add(new ValueGroup(g.Key, new ObservableCollection<ValueRowViewModel>(
                g.Select(d => new ValueRowViewModel(d, () => Session, Report)))));
        return groups;
    }

    private IEnumerable<ValueRowViewModel> AllRows() =>
        SettingGroups.Concat(CalibrationGroups).SelectMany(g => g.Rows);

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task RefreshAllValuesAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var row in AllRows())
            {
                if (Session is null) break;
                await row.LoadAsync();
            }
            Report("All values read.", false);
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private IReadOnlyList<ActionItem> BuildActions()
    {
        IAsyncRelayCommand Act(string what, Func<InstrumentApi, Task> run, string? confirmTitle = null, string? confirmText = null) =>
            new AsyncRelayCommand(async () =>
            {
                if (confirmTitle is not null &&
                    !(ConfirmAsync is not null && await ConfirmAsync(confirmTitle, confirmText ?? "")))
                    return;
                await RunAsync($"{what}…", async s =>
                {
                    await run(s.Api);
                    Report($"{what}: done.", false);
                });
            }, () => CanOperate);

        var list = new List<ActionItem>
        {
            new("Power off", "Put the instrument to sleep now. Wake it with a knob or by plugging in USB power.",
                Act("Power off", a => a.PowerOffAsync(), "Power off the instrument?",
                    "The connection ends. Wake the instrument with a knob press or by plugging in USB power.")),
            new("Reboot", "Restart the instrument.", Act("Reboot", a => a.RebootAsync())),
            new("Force charge", "Charge regardless of the level while USB power is present, until full or until USB is removed.",
                Act("Force charge", a => a.ForceChargeAsync())),
            new("End forced charge", "Cancel an armed forced charge (normal automatic charging resumes).",
                Act("End forced charge", a => a.EndChargingAsync())),
            new("Inhibit charging", "Hold charging off (also cancels a forced charge).",
                Act("Inhibit charging", a => a.SetChargeInhibitAsync(true))),
            new("Allow charging", "Lift the charging inhibit.",
                Act("Allow charging", a => a.SetChargeInhibitAsync(false))),
            new("Test beep", "Sound the buzzer for 100 ms.", Act("Test beep", a => a.TestBeepAsync())),
            new("Clear counters", "Zero the communication, drop and fault counters and the sticky error latches.",
                Act("Clear counters", a => a.ClearCountersAsync())),
            new("Leave service mode", "Lock calibration writes and maintenance commands again.",
                Act("Leave service mode", a => a.ServiceEndAsync())),
            new("Reboot to DFU", "Put the instrument in the ROM bootloader for flashing. Needs service mode.",
                Act("Reboot to DFU", a => a.RebootToDfuAsync(), "Reboot to the bootloader?",
                    "The instrument reboots into the USB bootloader and STAYS there on every boot until it is " +
                    "reflashed with a firmware image that restores the nBOOT0 option byte (dfu_flash.ps1). " +
                    "A power cycle does not bring the application back. Continue?"), Danger: true),
            new("Factory defaults", "Reset ALL settings and calibrations to the compiled defaults. Wipes the tilt calibrations. Needs service mode.",
                Act("Factory defaults", a => a.FactoryDefaultsAsync(), "Reset to factory defaults?",
                    "ALL settings and calibrations are reset, including the tilt calibration of both sensors. " +
                    "You will have to recalibrate. Continue?"), Danger: true),
        };
        return list;
    }

    // ── Diagnostics ──────────────────────────────────────────────────────────

    private void SyncDiagSubscription()
    {
        if (_diagVisible && Session is { } s)
        {
            if (_diagSub is not null) return;
            _diagSub = s.Api.SubscribeSignalDiag(d => Post(() => ApplyDiag(d)), ex => Post(() =>
                Report(ex is ApiException api ? api.UserMessage : "Signal diagnostics are not available.", true)));
        }
        else
        {
            _diagSub?.Dispose();
            _diagSub = null;
        }
    }

    private void ApplyDiag(SignalDiag d)
    {
        for (int i = 0; i < DiagChannels.Count && i < 4; i++)
        {
            DiagChannels[i].Rms = $"{d.RmsMv[i]:0.00} mV";
            DiagChannels[i].P2p = $"{d.P2pMv[i]:0.0} mV";
            DiagChannels[i].Phase = $"{d.PhaseDeg[i]:0.0}°";
        }
        TheoreticalTiltText =
            $"S1 {d.TheoreticalTiltS1 * 1000:+0.0;-0.0;0.0} µm/m · S2 {d.TheoreticalTiltS2 * 1000:+0.0;-0.0;0.0} µm/m";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task RunAsync(string busyMessage, Func<LeveltronicSession, Task> action)
    {
        if (Session is not { } s || IsBusy) return;
        IsBusy = true;
        Report(busyMessage, false);
        try
        {
            await action(s);
        }
        catch (ApiException ex) { Report(ex.UserMessage, true); }
        catch (LinkException ex) { Report(ex.Message, true); }
        catch (Exception ex) { Report($"Error: {ex.Message}", true); }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void Report(string message, bool error) => Post(() =>
    {
        StatusMessage = message;
        StatusIsError = error;
    });

    private void NotifyCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        RefreshAllValuesCommand.NotifyCanExecuteChanged();
        SetClockToPcCommand.NotifyCanExecuteChanged();
        LeaveServiceModeCommand.NotifyCanExecuteChanged();
        foreach (var a in Actions) a.Command.NotifyCanExecuteChanged();
    }

    private void Post(Action a)
    {
        if (_ui is null || ReferenceEquals(SynchronizationContext.Current, _ui)) a();
        else _ui.Post(_ => a(), null);
    }

    public async ValueTask DisposeAsync() => await ReleaseAsync();
}
