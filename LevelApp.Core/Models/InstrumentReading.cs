namespace LevelApp.Core.Models;

/// <summary>
/// A single reading captured from an electronic level instrument during a measurement session.
/// Serialised as a JSON line to the .instrument activity log file.
/// </summary>
public sealed class InstrumentReading
{
    public DateTime Timestamp { get; set; }
    public double   Value     { get; set; }
}
