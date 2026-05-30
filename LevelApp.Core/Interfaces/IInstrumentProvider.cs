using LevelApp.Core.Instruments;
using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

public interface IInstrumentProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    InstrumentCapabilities Capabilities { get; }
    InstrumentConnectionState ConnectionState { get; }
    event EventHandler<InstrumentConnectionState> ConnectionStateChanged;

    /// <summary>
    /// Establish connection to the instrument. No-op for providers that
    /// are always connected (e.g. ManualEntry).
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Release the connection. No-op for always-connected providers.</summary>
    Task DisconnectAsync();

    /// <summary>
    /// Request a single reading. Valid when Capabilities includes SingleMeasurement.
    /// For ContinuousStream-only providers, returns the most recently received value.
    /// </summary>
    Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct);
}
