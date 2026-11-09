using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Instruments.BLE;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.Protocol;
using LevelApp.Instruments.Leveltronic.Transport;
using LevelApp.Instruments.Leveltronic.UI.Views;
using LevelApp.Instruments.UsbHid;

namespace LevelApp.Instruments.Leveltronic.UI;

/// <summary>
/// <see cref="IInstrumentPlugin"/> for the Leveltronic precision electronic
/// level (firmware 0.11+, API v3). Supports USB Custom HID and BLE (RN4871
/// Transparent UART); both speak the same API. Every consumer — guided
/// measurement, management view, calibration wizards — shares one
/// <see cref="LeveltronicSession"/> per instrument through <see cref="Sessions"/>.
/// </summary>
public sealed class LeveltronicPlugin : IInstrumentPlugin
{
    public LeveltronicPlugin()
        : this(new JsonLeveltronicPreferences(JsonLeveltronicPreferences.DefaultPath))
    {
    }

    public LeveltronicPlugin(ILeveltronicPreferences preferences)
        : this(preferences, new LeveltronicSessionManager(LeveltronicLinkFactory.Create))
    {
    }

    public LeveltronicPlugin(ILeveltronicPreferences preferences, LeveltronicSessionManager sessions)
    {
        Preferences = preferences;
        Sessions = sessions;
    }

    public ILeveltronicPreferences Preferences { get; }
    public LeveltronicSessionManager Sessions { get; }

    public string PluginId => "leveltronic";
    public string DisplayName => "Leveltronic";
    public InstrumentCapabilities Capabilities =>
        InstrumentCapabilities.SingleMeasurement | InstrumentCapabilities.ContinuousStream;

    public IReadOnlyList<ITransport> SupportedTransports =>
        [new BleTransport(), new UsbHidTransport()];

    public IReadOnlyList<IDeviceScanner> CreateScanners() =>
    [
        // The modules do not advertise the Transparent-UART service, so scan by name.
        new BleDeviceScanner([], [LeveltronicApi.BleNamePrefix]),
        new UsbHidDeviceScanner(LeveltronicApi.UsbVendorId, [LeveltronicApi.UsbProductId]),
    ];

    public IInstrumentProvider CreateProvider(KnownDevice device) =>
        new LeveltronicProvider(device, Sessions, Preferences);

    public ICalibrationWorkflow? CreateCalibrationWorkflow(KnownDevice device) =>
        new LeveltronicCalibrationWorkflow(device, Sessions, Preferences);

    public IFirmwareUpdater? CreateFirmwareUpdater(KnownDevice device) =>
        device.TransportId == LeveltronicLinkFactory.UsbTransportId
            ? new LeveltronicFirmwareUpdater(device, Sessions)
            : null;

    public object? CreateDeviceManagementView(IDeviceRegistry registry) =>
        new LeveltronicManagementView(Sessions, registry);
}
