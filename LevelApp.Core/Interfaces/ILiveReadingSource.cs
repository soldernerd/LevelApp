using LevelApp.Core.Instruments;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// An instrument measurement could not be taken; the message is fit to show the operator.
/// </summary>
public sealed class InstrumentMeasurementException(string message) : Exception(message);

/// <summary>
/// Optional capability of an <see cref="IInstrumentProvider"/>: the instrument
/// continuously reports a value the operator can watch, can be told which of
/// several channels to use, and takes a settled "precision" measurement on
/// request. Providers that only support manual entry or one-shot reads do not
/// implement it; the guided measurement then stays manual.
/// </summary>
public interface ILiveReadingSource
{
    /// <summary>Channels the operator can pick between (e.g. S1, S2, Diff). At least one.</summary>
    IReadOnlyList<ReadingChannel> Channels { get; }

    /// <summary>Id of the selected channel; setting it changes <see cref="Latest"/> and what <see cref="MeasureAsync"/> returns.</summary>
    string SelectedChannelId { get; set; }

    /// <summary>The newest live value on the selected channel, or null when not connected / nothing received yet.</summary>
    LiveReading? Latest { get; }

    /// <summary>
    /// Raised when <see cref="Latest"/>, <see cref="SelectedChannelId"/> or the zero
    /// offset changed. May be raised on a background thread.
    /// </summary>
    event EventHandler? LiveReadingChanged;

    /// <summary>
    /// Takes one settled measurement on the selected channel and returns it in µm/m
    /// (zero offset applied). Throws if no steady value was found or the
    /// instrument refused.
    /// </summary>
    Task<double> MeasureAsync(IProgress<double>? progress, CancellationToken ct);

    // ── Optional zero offset (e.g. "Zero Diff") ──────────────────────────────

    /// <summary>True when an offset can be set on the selected channel.</summary>
    bool SupportsZero { get; }

    /// <summary>True when an offset is applied to the selected channel.</summary>
    bool IsZeroed { get; }

    /// <summary>The applied offset in µm/m, or null.</summary>
    double? ZeroOffsetMicronsPerMetre { get; }

    /// <summary>Measures the selected channel and uses the result as the new zero offset.</summary>
    Task ZeroAsync(IProgress<double>? progress, CancellationToken ct);

    /// <summary>Removes the zero offset.</summary>
    void ClearZero();
}
