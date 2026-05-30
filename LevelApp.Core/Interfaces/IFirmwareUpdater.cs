using LevelApp.Core.Instruments;

namespace LevelApp.Core.Interfaces;

public interface IFirmwareUpdater
{
    TransportRequirement RequiredTransport { get; }

    /// <summary>
    /// True when the required transport is currently available.
    /// The app uses this to enable/disable the Update button.
    /// </summary>
    bool IsReady { get; }

    event EventHandler IsReadyChanged;

    Task<FirmwareInfo> GetCurrentFirmwareAsync(CancellationToken ct = default);
    Task<FirmwareInfo?> CheckForUpdateAsync(CancellationToken ct = default);
    Task PerformUpdateAsync(IProgress<double> progress, CancellationToken ct);
}
