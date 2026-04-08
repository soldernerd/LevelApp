using LevelApp.App.DisplayModules.MeasurementsGrid;
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

            DrawMeasurementsGrid();
        }
    }

    // ── Measurements grid ─────────────────────────────────────────────────────

    private void DrawMeasurementsGrid()
    {
        if (ViewModel.ActiveResult     is null) return;
        if (ViewModel.ActiveDefinition is null) return;

        bool isRawMode = RawModeRadio.IsChecked == true;
        bool isUmUnits = MicronRadio.IsChecked  == true;

        MeasurementsGridRenderer.Render(
            MeasurementsCanvas,
            ViewModel.ActiveSteps,
            ViewModel.ActiveResult,
            ViewModel.ActiveDefinition,
            isRawMode,
            isUmUnits);
    }

    /// <summary>
    /// Re-renders the measurements grid whenever the Mode or Units toggle changes.
    /// </summary>
    private void OnModeOrUnitsChanged(object sender, RoutedEventArgs e)
        => DrawMeasurementsGrid();

    /// <summary>
    /// Ensures the measurements grid is drawn when Tab 2 is first selected,
    /// in case the canvas was not yet visible during <see cref="OnNavigatedTo"/>.
    /// </summary>
    private void OnResultsTabSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is TabView tv && tv.SelectedIndex == 1)
            DrawMeasurementsGrid();
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
