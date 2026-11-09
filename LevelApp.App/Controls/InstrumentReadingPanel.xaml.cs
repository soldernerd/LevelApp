using LevelApp.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Controls;

/// <summary>
/// Live value, channel choice, settled measurement and zero for an instrument that
/// reports readings; hidden for manual entry. Bound to an <see cref="InstrumentReadingViewModel"/>
/// owned by the hosting page's view-model.
/// </summary>
public sealed partial class InstrumentReadingPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(InstrumentReadingViewModel), typeof(InstrumentReadingPanel),
        new PropertyMetadata(null, (d, _) => ((InstrumentReadingPanel)d).Bindings.Update()));

    public InstrumentReadingPanel() => InitializeComponent();

    public InstrumentReadingViewModel? ViewModel
    {
        get => (InstrumentReadingViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
