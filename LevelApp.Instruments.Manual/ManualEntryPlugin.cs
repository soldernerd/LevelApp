using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;

namespace LevelApp.Instruments.Manual;

public sealed class ManualEntryPlugin : IInstrumentPlugin
{
    public string PluginId    => "manual-entry";
    public string DisplayName => "Manual Entry";
    public InstrumentCapabilities Capabilities => InstrumentCapabilities.SingleMeasurement;

    public IReadOnlyList<ITransport>     SupportedTransports => [new ManualTransport()];
    public IReadOnlyList<IDeviceScanner> CreateScanners()    => [new ManualEntryScanner()];

    // Manual entry needs no device selection — always returns the same provider.
    public IInstrumentProvider CreateProvider(KnownDevice device) =>
        new ManualEntryProvider();

    // No calibration, firmware update, or device management UI.
    public ICalibrationWorkflow? CreateCalibrationWorkflow(KnownDevice device) => null;
    public IFirmwareUpdater?     CreateFirmwareUpdater(KnownDevice device)      => null;
    public object?               CreateDeviceManagementView(IDeviceRegistry r)  => null;
}
