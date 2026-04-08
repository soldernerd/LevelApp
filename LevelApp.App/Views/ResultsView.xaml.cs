using LevelApp.App.DisplayModules.MeasurementsGrid;
using LevelApp.App.Navigation;
using LevelApp.App.ViewModels;
using LevelApp.App.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace LevelApp.App.Views;

public sealed partial class ResultsView : Page
{
    private double _zoomFactor = 1.0;

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

    // ── Recalculate ───────────────────────────────────────────────────────────

    private async void OnRecalculateClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new RecalculateDialog(
            ViewModel.CurrentCalculationParameters,
            null)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.None) return;   // Cancel

        bool save = result == ContentDialogResult.Secondary; // "Recalculate & Save"
        var parameters = dialog.BuildParameters();

        await ViewModel.RecalculateAsync(parameters, save);

        if (ViewModel.PlotContent is UIElement plotElement)
            PlotContainer.Content = plotElement;
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
            isUmUnits,
            _zoomFactor);
    }

    /// <summary>
    /// Re-renders the measurements grid whenever the Mode or Units toggle changes.
    /// </summary>
    private void OnModeOrUnitsChanged(object sender, RoutedEventArgs e)
        => DrawMeasurementsGrid();

    /// <summary>
    /// Mouse-wheel zoom: each notch scales by ×1.2 or ÷1.2, clamped to 0.25 – 8×.
    /// The event is marked handled so the parent ScrollViewer scrolls only via its bars.
    /// </summary>
    private void OnMeasurementsCanvasWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(MeasurementsCanvas).Properties.MouseWheelDelta;
        double factor = delta > 0 ? 1.2 : 1.0 / 1.2;
        _zoomFactor = Math.Clamp(_zoomFactor * factor, 0.25, 8.0);
        DrawMeasurementsGrid();
        e.Handled = true;
    }

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
