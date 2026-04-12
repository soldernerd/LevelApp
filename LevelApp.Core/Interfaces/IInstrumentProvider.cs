using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// Abstraction for a hardware electronic level instrument.
/// Concrete implementations (Bluetooth, USB, simulation) are resolved via DI.
///
/// The optional <see cref="RecordingTarget"/> hook causes each successful
/// <see cref="ReadAsync"/> call to forward the reading to the activity logger,
/// enabling replay testing from recorded sessions.
/// Set it when a measurement session starts; clear it (set to <c>null</c>) when
/// the session ends.
/// </summary>
public interface IInstrumentProvider
{
    /// <summary>
    /// When set, each successful <see cref="ReadAsync"/> calls
    /// <see cref="IActivityLogger.AttachInstrumentRecording"/> with the reading.
    /// </summary>
    IActivityLogger? RecordingTarget { get; set; }

    /// <summary>Reads a single measurement from the instrument.</summary>
    Task<InstrumentReading> ReadAsync(CancellationToken ct = default);
}
