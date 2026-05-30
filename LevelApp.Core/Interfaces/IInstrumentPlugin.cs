using LevelApp.Core.Instruments;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// Root contract for instrument plugins. Each instrument project provides
/// exactly one implementation.
/// </summary>
public interface IInstrumentPlugin
{
    string PluginId { get; }
    string DisplayName { get; }
    InstrumentCapabilities Capabilities { get; }

    /// <summary>All transports this instrument supports.</summary>
    IReadOnlyList<ITransport> SupportedTransports { get; }

    /// <summary>All scanners, one per supported transport.</summary>
    IReadOnlyList<IDeviceScanner> CreateScanners();

    /// <summary>
    /// Create the reading provider for a specific known device.
    /// The transport used is determined by device.TransportId.
    /// </summary>
    IInstrumentProvider CreateProvider(KnownDevice device);

    // Optional capabilities — null means not supported by this instrument
    ICalibrationWorkflow? CreateCalibrationWorkflow(KnownDevice device);
    IFirmwareUpdater?     CreateFirmwareUpdater(KnownDevice device);

    /// <summary>
    /// Optional instrument-specific device management view. Returned as
    /// object to keep Core free of WinUI dependencies; cast to UIElement
    /// in LevelApp.App.
    /// </summary>
    object? CreateDeviceManagementView(IDeviceRegistry registry);
}
