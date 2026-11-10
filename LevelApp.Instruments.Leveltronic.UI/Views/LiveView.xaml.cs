using LevelApp.Instruments.Leveltronic.UI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.Instruments.Leveltronic.UI.Views;

/// <summary>
/// The live screen: S1, S2 and Diff in large tabular type with a vibration indicator each, battery
/// and on-board temperature, Zero Diff and Precision. Always dark, like the phone app's instrument
/// display. All logic lives in <see cref="LiveViewModel"/>; attach it to the session via
/// <see cref="ISessionAware"/>.
/// </summary>
public sealed partial class LiveView : UserControl
{
    public LiveView(LiveViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public LiveViewModel ViewModel { get; }
}
