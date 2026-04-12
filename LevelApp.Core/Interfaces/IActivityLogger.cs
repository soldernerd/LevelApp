using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// Records user interactions to a JSON Lines (.jsonl) file during an app session.
/// When <see cref="IsEnabled"/> is <c>false</c>, all methods are no-ops.
/// </summary>
public interface IActivityLogger
{
    /// <summary>Appends a single log entry.</summary>
    void Log(string action, string? detail = null,
             Dictionary<string, object>? extra = null);

    /// <summary>
    /// Copies the project file at <paramref name="projectPath"/> to the log folder
    /// with a _p{n} suffix and writes a <c>File.Open</c> log entry that includes
    /// the snapshot filename.
    /// </summary>
    void AttachProjectSnapshot(string projectPath);

    /// <summary>
    /// Appends a serialised <see cref="InstrumentReading"/> to the .instrument file
    /// for this session.
    /// </summary>
    void AttachInstrumentRecording(InstrumentReading reading);

    bool IsEnabled { get; set; }
}
