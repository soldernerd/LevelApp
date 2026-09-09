using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;
using LevelApp.Instruments.Leveltronic.Transport;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>
/// Backs <c>LeveltronicManagementView</c> — connects to a registered Leveltronic
/// and reads/writes every parameter the API v2 build exposes (Settings, RTC),
/// plus the one-shot commands (test beep, force charge, reboot to DFU).
/// </summary>
public sealed partial class LeveltronicManagementViewModel : ObservableObject, IAsyncDisposable
{
    private const string PluginId = "leveltronic";

    private readonly IDeviceRegistry _registry;
    private LeveltronicDeviceClient? _client;

    public LeveltronicManagementViewModel(IDeviceRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        foreach (var d in LeveltronicApi.Settings)
            Settings.Add(new LeveltronicSettingRow(d));

        UpdateTargetDevice();
    }

    /// <summary>
    /// Set by the view: shows a confirm dialog, returns true if the user agreed.
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public ObservableCollection<LeveltronicSettingRow> Settings { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    private bool _isBusy;

    [ObservableProperty] private bool _hasDevice;
    [ObservableProperty] private string _deviceName = "No Leveltronic registered";
    [ObservableProperty] private string _statusMessage = "Not connected.";
    [ObservableProperty] private string _identityText = "";
    [ObservableProperty] private string _deviceStateText = "";
    [ObservableProperty] private string _rtcText = "";

    public bool CanConnect => HasDevice && !IsConnected && !IsBusy;
    public bool CanOperate => IsConnected && !IsBusy;

    private KnownDevice? _target;

    /// <summary>Re-read which device this view targets (call when returning to the tab).</summary>
    public void UpdateTargetDevice()
    {
        _target = _registry.GetPreferredDevice(PluginId)
               ?? _registry.GetKnownDevices(PluginId).FirstOrDefault();
        HasDevice = _target is not null;
        DeviceName = _target?.DisplayName ?? "No Leveltronic registered";
        ConnectCommand.NotifyCanExecuteChanged();
    }

    // ── Connect / disconnect ────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (_target is null) return;

        await RunAsync("Connecting…", async ct =>
        {
            var link = LeveltronicLinkFactory.Create(_target);
            await link.ConnectAsync(ct);
            _client = new LeveltronicDeviceClient(link);
            IsConnected = true;
            await RefreshCoreAsync(ct);
            StatusMessage = $"Connected to {_target.DisplayName}.";
        });
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        IsConnected = false;
        IdentityText = DeviceStateText = RtcText = "";
        foreach (var row in Settings) row.DeviceValue = null;
        StatusMessage = "Disconnected.";
    }

    // ── Reads ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task RefreshAllAsync() =>
        RunAsync("Reading all parameters…", async ct =>
        {
            await RefreshCoreAsync(ct);
            foreach (var row in Settings)
            {
                ct.ThrowIfCancellationRequested();
                long v = await _client!.GetSettingValueAsync(row.Descriptor, ct);
                row.SetFromDevice(v);
            }
            StatusMessage = "All parameters read.";
        });

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task ReadSettingAsync(LeveltronicSettingRow row) =>
        RunAsync($"Reading {row.Field}…", async ct =>
        {
            long v = await _client!.GetSettingValueAsync(row.Descriptor, ct);
            row.SetFromDevice(v);
            StatusMessage = $"{row.Field} = {v}";
        });

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        var id = await _client!.GetIdentityAsync(ct);
        IdentityText = $"Firmware {id.FirmwareVersion} · {id.Product} · S/N {id.Serial}";

        var st = await _client.GetDeviceStateAsync(ct);
        DeviceStateText =
            $"Battery {st.BatterySocPercent}% ({st.BatteryMilliVolts} mV, {st.Battery}) · " +
            $"USB {(st.UsbConnected ? "yes" : "no")} · BLE {(st.BleConnected ? "yes" : "no")} · " +
            $"calibration {(st.CalibrationValid ? "valid" : "invalid")}";

        var rtc = await _client.GetRtcAsync(ct);
        RtcText = rtc.IsSet
            ? $"{rtc.Year:0000}-{rtc.Month:00}-{rtc.Day:00} {rtc.Hour:00}:{rtc.Minute:00}:{rtc.Second:00}"
            : "not set";
    }

    // ── Writes ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task WriteSettingAsync(LeveltronicSettingRow row) =>
        RunAsync($"Writing {row.Field}…", async ct =>
        {
            long value = (long)Math.Round(row.EditValue);
            await _client!.SetSettingValueAsync(row.Descriptor, value, ct);
            long readBack = await _client.GetSettingValueAsync(row.Descriptor, ct);
            row.SetFromDevice(readBack);
            StatusMessage = $"{row.Field} set to {readBack}.";
        });

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task WriteAllChangedAsync() =>
        RunAsync("Writing changed parameters…", async ct =>
        {
            int n = 0;
            foreach (var row in Settings.Where(r => r.IsDirty && r.DeviceValue is not null))
            {
                ct.ThrowIfCancellationRequested();
                await _client!.SetSettingValueAsync(row.Descriptor, (long)Math.Round(row.EditValue), ct);
                long readBack = await _client.GetSettingValueAsync(row.Descriptor, ct);
                row.SetFromDevice(readBack);
                n++;
            }
            StatusMessage = n == 0 ? "Nothing to write." : $"Wrote {n} parameter(s).";
        });

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task SetRtcToNowAsync() =>
        RunAsync("Setting RTC…", async ct =>
        {
            await _client!.SetRtcAsync(DateTime.Now, ct);
            var rtc = await _client.GetRtcAsync(ct);
            RtcText = $"{rtc.Year:0000}-{rtc.Month:00}-{rtc.Day:00} {rtc.Hour:00}:{rtc.Minute:00}:{rtc.Second:00}";
            StatusMessage = "RTC set to PC clock.";
        });

    // ── One-shot commands ──────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task TestBeepAsync() =>
        RunAsync("Beeping…", async ct =>
        {
            await _client!.TestBeepAsync(ct);
            StatusMessage = "Test beep sent.";
        });

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task ForceChargeAsync() =>
        RunAsync("Arming force-charge…", async ct =>
        {
            await _client!.ForceChargeAsync(ct);
            StatusMessage = "Force-charge armed (clears on full or USB unplug).";
        });

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task RebootToDfuAsync()
    {
        bool ok = ConfirmAsync is not null && await ConfirmAsync(
            "Reboot to firmware-update mode?",
            "The device will reboot into the USB bootloader and STAY there on every " +
            "boot until it is reflashed with a firmware image that restores the nBOOT0 " +
            "option byte. A power cycle will not bring the application back. Continue?");
        if (!ok) return;

        await RunAsync("Rebooting to DFU…", async ct =>
        {
            await _client!.RebootToDfuAsync(ct);
            await DisconnectAsync();
            StatusMessage = "Device is in the USB bootloader. Reflash with dfu_flash.ps1 to recover.";
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task RunAsync(string busyMessage, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = busyMessage;
        NotifyCommandStates();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await action(cts.Token);
        }
        catch (InstrumentResourceNotSupportedException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            if (!IsConnected) await DisconnectAsync();
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private void NotifyCommandStates()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        RefreshAllCommand.NotifyCanExecuteChanged();
        ReadSettingCommand.NotifyCanExecuteChanged();
        WriteSettingCommand.NotifyCanExecuteChanged();
        WriteAllChangedCommand.NotifyCanExecuteChanged();
        SetRtcToNowCommand.NotifyCanExecuteChanged();
        TestBeepCommand.NotifyCanExecuteChanged();
        ForceChargeCommand.NotifyCanExecuteChanged();
        RebootToDfuCommand.NotifyCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _client = null;
    }
}
