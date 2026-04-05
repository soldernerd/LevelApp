using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Instruments.ManualEntry;

/// <summary>
/// Instrument provider for manual reading entry.
/// Contains no UI or input logic itself — the caller supplies a delegate that
/// obtains the value however it needs to (console prompt, WinUI dialog, test stub, etc.).
/// This keeps the provider a pure pass-through and fully unit-testable.
///
/// Future providers (Bluetooth LE, USB HID) will replace the delegate with real
/// hardware communication, but the <see cref="IInstrumentProvider"/> contract stays identical.
/// </summary>
public sealed class ManualEntryProvider : IInstrumentProvider
{
    private readonly Func<MeasurementStep, CancellationToken, Task<double>> _requestReading;

    /// <param name="requestReading">
    /// Async callback invoked for each step.  Must return the operator's reading in mm/m.
    /// Example (WinUI): async (step, ct) => await ShowReadingDialogAsync(step, ct)
    /// Example (test):  (step, ct) => Task.FromResult(0.012)
    /// </param>
    public ManualEntryProvider(
        Func<MeasurementStep, CancellationToken, Task<double>> requestReading)
    {
        _requestReading = requestReading;
    }

    public string ProviderId   => "manual-entry";
    public string DisplayName  => "Manual Entry";

    public Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct)
        => _requestReading(step, ct);
}
