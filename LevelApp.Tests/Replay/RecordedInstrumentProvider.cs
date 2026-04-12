using System.Text.Json;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Tests.Replay;

/// <summary>
/// <see cref="IInstrumentProvider"/> implementation that replays readings from a
/// previously recorded <c>.instrument</c> file (one JSON line per reading).
/// Used by <see cref="ActivityReplayRunner"/> during replay tests.
///
/// If no <c>.instrument</c> file exists alongside the <c>.jsonl</c>, the replay
/// runner passes a <see cref="NullInstrumentProvider"/> instead.
/// </summary>
public sealed class RecordedInstrumentProvider : IInstrumentProvider
{
    private readonly Queue<InstrumentReading> _readings;

    public IActivityLogger? RecordingTarget { get; set; }

    public RecordedInstrumentProvider(string instrumentFilePath)
    {
        _readings = new Queue<InstrumentReading>(
            File.ReadLines(instrumentFilePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => JsonSerializer.Deserialize<InstrumentReading>(l)!));
    }

    public Task<InstrumentReading> ReadAsync(CancellationToken ct = default)
    {
        if (_readings.TryDequeue(out var reading))
            return Task.FromResult(reading);

        throw new EndOfRecordingException(
            "No more recorded readings — log may have been truncated.");
    }
}

/// <summary>
/// Stub <see cref="IInstrumentProvider"/> used when no <c>.instrument</c> file
/// exists for a session.  Throws <see cref="NotSupportedException"/> if called.
/// </summary>
public sealed class NullInstrumentProvider : IInstrumentProvider
{
    public IActivityLogger? RecordingTarget { get; set; }

    public Task<InstrumentReading> ReadAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(
            "No instrument recording was captured for this session.");
}
