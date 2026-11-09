using LevelApp.Core.Instruments;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.UI.Calibration;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.Instruments.Leveltronic.UI.Views;

/// <summary>
/// The calibration surface of a Leveltronic: the three wizards (phase → gain → zero) with
/// service-mode handling, behaving as on the phone app. Opened by the Instruments page's
/// Calibrate button through <see cref="LeveltronicCalibrationWorkflow"/>.
/// </summary>
public sealed partial class CalibrationHubView : UserControl
{
    public CalibrationHubViewModel ViewModel { get; }

    public CalibrationHubView(LeveltronicSessionManager sessions, ILeveltronicPreferences prefs, KnownDevice device)
    {
        ViewModel = new CalibrationHubViewModel(sessions, prefs, device);
        InitializeComponent();

        Loaded += async (_, _) => await ViewModel.ConnectAsync();
        Unloaded += async (_, _) => await ViewModel.DisposeAsync();
    }
}
