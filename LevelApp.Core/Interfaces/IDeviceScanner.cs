using LevelApp.Core.Instruments;

namespace LevelApp.Core.Interfaces;

public interface IDeviceScanner
{
    ITransport Transport { get; }

    /// <summary>
    /// Scan for nearby devices that match this plugin's expected profile.
    /// Reports candidates as they are found. Completes when the scan
    /// timeout elapses or ct is cancelled.
    /// </summary>
    IAsyncEnumerable<DeviceCandidate> ScanAsync(TimeSpan timeout, CancellationToken ct);
}
