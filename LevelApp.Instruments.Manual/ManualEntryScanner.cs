using System.Runtime.CompilerServices;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;

namespace LevelApp.Instruments.Manual;

/// <summary>
/// Scanner for manual entry — no physical scan is needed, so this always
/// returns an empty sequence immediately.
/// </summary>
public sealed class ManualEntryScanner : IDeviceScanner
{
    public ITransport Transport => new ManualTransport();

    public async IAsyncEnumerable<DeviceCandidate> ScanAsync(
        TimeSpan timeout, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}
