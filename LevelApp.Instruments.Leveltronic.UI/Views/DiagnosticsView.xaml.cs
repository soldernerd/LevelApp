using LevelApp.Instruments.Leveltronic.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.Instruments.Leveltronic.UI.Views;

/// <summary>
/// Signal diagnostics: RMS, peak-to-peak and phase of A, B, S1, S2 with ADC channel and PGA
/// gain, the calibrated phase of S1/S2 and the theoretical tilt. All data comes from
/// <see cref="DiagnosticsViewModel"/>; the host sets its <c>IsActive</c> while this view shows.
/// </summary>
public sealed partial class DiagnosticsView : UserControl
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsView(DiagnosticsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
