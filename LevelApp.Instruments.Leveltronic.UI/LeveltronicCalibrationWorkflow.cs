using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.UI.Views;

namespace LevelApp.Instruments.Leveltronic.UI;

/// <summary>
/// The Leveltronic's guided calibration — phase, gain (sensitivity) and zero (flip)
/// wizards, run in that order — as an <see cref="ICalibrationWorkflow"/>: the
/// Instruments page embeds <see cref="CreateView"/> when the operator presses
/// Calibrate. The first concrete implementation of the interface.
/// </summary>
public sealed class LeveltronicCalibrationWorkflow(
    KnownDevice device, LeveltronicSessionManager sessions, ILeveltronicPreferences prefs) : ICalibrationWorkflow
{
    public string DisplayName => "Calibration";

    public object CreateView() => new CalibrationHubView(sessions, prefs, device);
}
