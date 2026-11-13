using LevelApp.Instruments.Leveltronic.UI.Calibration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.Instruments.Leveltronic.UI.Views;

/// <summary>Renders the <see cref="WizardPage"/> of one wizard view-model.</summary>
public sealed partial class WizardPageView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(WizardViewModelBase), typeof(WizardPageView),
        new PropertyMetadata(null, (d, _) =>
        {
            var view = (WizardPageView)d;
            view.Bindings.Update();
            view.OnViewModelChanged();
        }));

    public WizardPageView() => InitializeComponent();

    private WizardViewModelBase? _subscribed;

    private void OnViewModelChanged()
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnPageChanged;
        _subscribed = ViewModel;
        if (_subscribed is not null) _subscribed.PropertyChanged += OnPageChanged;
        UpdateDiagram();
    }

    private void OnPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WizardViewModelBase.Page)) UpdateDiagram();
    }

    /// <summary>Shows the pose of the current page in the instrument diagram, or hides the diagram.</summary>
    private void UpdateDiagram()
    {
        var page = ViewModel?.Page;
        if (page?.Pose is { } pose)
        {
            DiagramCard.Visibility = Visibility.Visible;
            Diagram.Show(pose, page.PoseLabel);
        }
        else
        {
            DiagramCard.Visibility = Visibility.Collapsed;
        }
    }

    public WizardViewModelBase? ViewModel
    {
        get => (WizardViewModelBase?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
