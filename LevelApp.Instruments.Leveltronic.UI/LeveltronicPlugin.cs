using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Instruments.BLE;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;
using LevelApp.Instruments.Leveltronic.Transport;
using LevelApp.Instruments.Leveltronic.UI.Views;
using LevelApp.Instruments.UsbHid;

namespace LevelApp.Instruments.Leveltronic.UI;

/// <summary>
/// <see cref="IInstrumentPlugin"/> for the Leveltronic precision electronic
/// level. Supports USB Custom HID and BLE (RN4871 Transparent UART); both speak
/// the same Device API v2. Carries a device-management view; DFU firmware update
/// over USB; no calibration workflow (the firmware's Calibrations category is
/// absent on this build).
/// </summary>
public sealed class LeveltronicPlugin : IInstrumentPlugin
{
    public string PluginId => "leveltronic";
    public string DisplayName => "Leveltronic";
    public InstrumentCapabilities Capabilities => InstrumentCapabilities.SingleMeasurement;

    public IReadOnlyList<ITransport> SupportedTransports =>
        [new UsbHidTransport(), new BleTransport()];

    public IReadOnlyList<IDeviceScanner> CreateScanners() =>
    [
        new UsbHidDeviceScanner(LeveltronicApi.UsbVendorId, [LeveltronicApi.UsbProductId]),
        new BleDeviceScanner([LeveltronicApi.BleServiceUuid]),
    ];

    public IInstrumentProvider CreateProvider(KnownDevice device) =>
        new LeveltronicProvider(device);

    public ICalibrationWorkflow? CreateCalibrationWorkflow(KnownDevice device) => null;

    public IFirmwareUpdater? CreateFirmwareUpdater(KnownDevice device) =>
        device.TransportId == LeveltronicLinkFactory.UsbTransportId
            ? new LeveltronicFirmwareUpdater(device)
            : null;

    public object? CreateDeviceManagementView(IDeviceRegistry registry) =>
        new LeveltronicManagementView(registry);
}
