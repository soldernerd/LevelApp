using LevelApp.App.Navigation;
using LevelApp.App.ViewModels;
using LevelApp.App.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LevelApp.App.Views;

public sealed partial class ResultsView : Page
{
    public ResultsViewModel ViewModel { get; }

    public ResultsView()
    {
        ViewModel = App.Services.GetRequiredService<ResultsViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ResultsArgs args)
        {
            ViewModel.Initialize(args);

            if (ViewModel.PlotContent is UIElement plotElement)
                PlotContainer.Content = plotElement;
        }
    }

    // ── New Measurement ───────────────────────────────────────────────────────

    private async void OnNewMeasurementClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveObjectDefinition is null) return;

        var dialog = new NewMeasurementDialog(
            ViewModel.ActiveObjectDefinition,
            ViewModel.ActiveOperator)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            ViewModel.StartNewMeasurement(dialog.OperatorName, dialog.Notes, dialog.StrategyId);
    }
}
