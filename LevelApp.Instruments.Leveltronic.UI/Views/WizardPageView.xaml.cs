using LevelApp.Instruments.Leveltronic.UI.Calibration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.Instruments.Leveltronic.UI.Views;

/// <summary>Renders the <see cref="WizardPage"/> of one wizard view-model.</summary>
public sealed partial class WizardPageView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(WizardViewModelBase), typeof(WizardPageView),
        new PropertyMetadata(null, (d, _) => ((WizardPageView)d).Bindings.Update()));

    public WizardPageView() => InitializeComponent();

    public WizardViewModelBase? ViewModel
    {
        get => (WizardViewModelBase?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
