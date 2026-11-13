namespace LevelApp.Core.Interfaces;

/// <summary>
/// Optional capability of an <see cref="IDeviceScanner"/>: a one-line account of what its last
/// scan saw. Shown when a scan finds nothing, so "No devices found" can be told apart from
/// "the radio is off" or "the device advertises under another name".
/// </summary>
public interface IScanDiagnostics
{
    /// <summary>What the most recent scan saw, or null when there is nothing to report.</summary>
    string? LastScanSummary { get; }
}
