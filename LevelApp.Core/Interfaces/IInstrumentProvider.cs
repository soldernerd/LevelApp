using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// Abstracts instrument connectivity. Today: manual entry. Future: Bluetooth LE, USB HID.
/// Geometry modules never know which provider is active.
/// </summary>
public interface IInstrumentProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct);
}
