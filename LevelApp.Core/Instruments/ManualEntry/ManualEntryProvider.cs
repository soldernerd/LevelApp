using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Instruments.ManualEntry;

/// <summary>
/// Instrument provider for manual keyboard entry. Always reports as Connected
/// because no physical connection is required.
///
/// In normal operation the ViewModel captures readings directly from the UI
/// NumberBox and does not call <see cref="GetReadingAsync"/>. The property
/// <see cref="NextReading"/> exists so that automated tests can inject a value.
/// </summary>
public sealed class ManualEntryProvider : IInstrumentProvider
{
    public string ProviderId  => "manual-entry";
    public string DisplayName => "Manual Entry";

    public InstrumentCapabilities    Capabilities    => InstrumentCapabilities.SingleMeasurement;
    public InstrumentConnectionState ConnectionState => InstrumentConnectionState.Connected;

    // Never fires — this provider is always connected.
    public event EventHandler<InstrumentConnectionState>? ConnectionStateChanged
    {
        add    { }
        remove { }
    }

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync() => Task.CompletedTask;

    /// <summary>
    /// The value returned by <see cref="GetReadingAsync"/>. Set this in tests to
    /// simulate a user-entered reading.
    /// </summary>
    public double NextReading { get; set; } = 0.0;

    public Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct) =>
        Task.FromResult(NextReading);
}
