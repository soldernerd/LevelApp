using CommunityToolkit.Mvvm.ComponentModel;
using LevelApp.Core.Interfaces;
using System.Collections.ObjectModel;

namespace LevelApp.App.ViewModels;

/// <summary>
/// ViewModel for one plugin tab in the Instruments page. Owns the list of
/// known devices and exposes capability flags for the tab UI.
/// </summary>
public sealed partial class InstrumentPluginTabViewModel : ObservableObject
{
    private readonly IInstrumentPlugin _plugin;
    private readonly IDeviceRegistry   _registry;

    // ── Identity ──────────────────────────────────────────────────────────────

    public string PluginId    => _plugin.PluginId;
    public string DisplayName => _plugin.DisplayName;

    /// <summary>
    /// True when the plugin requires active device discovery.
    /// False for always-connected providers (e.g. Manual Entry — transport "manual").
    /// </summary>
    public bool ShowAddDevice =>
        _plugin.SupportedTransports.Any(t => t.TransportId != "manual");

    // ── Known devices ─────────────────────────────────────────────────────────

    public ObservableCollection<KnownDeviceViewModel> KnownDevices { get; } = [];

    // ── Capability flags ──────────────────────────────────────────────────────

    /// <summary>True if any known device supports calibration.</summary>
    public bool CanCalibrate =>
        KnownDevices.Any(d => _plugin.CreateCalibrationWorkflow(d.Device) is not null);

    /// <summary>True if any known device supports firmware update.</summary>
    public bool CanUpdateFirmware =>
        KnownDevices.Any(d => _plugin.CreateFirmwareUpdater(d.Device) is not null);

    // ── Constructor ───────────────────────────────────────────────────────────

    public InstrumentPluginTabViewModel(IInstrumentPlugin plugin, IDeviceRegistry registry)
    {
        _plugin   = plugin;
        _registry = registry;
        RefreshDevices();
    }

    // ── Public API called from view code-behind ───────────────────────────────

    public void RefreshDevices()
    {
        KnownDevices.Clear();
        foreach (var d in _registry.GetKnownDevices(_plugin.PluginId))
            KnownDevices.Add(new KnownDeviceViewModel(d));
    }

    public void ForgetDevice(KnownDeviceViewModel vm)
    {
        _registry.ForgetDevice(vm.Device.DeviceId);
        KnownDevices.Remove(vm);
        OnPropertyChanged(nameof(CanCalibrate));
        OnPropertyChanged(nameof(CanUpdateFirmware));
    }

    public IInstrumentPlugin Plugin => _plugin;
    public IDeviceRegistry   Registry => _registry;
}
