using LevelApp.App.ViewModels;
using LevelApp.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LevelApp.App.Views;

public sealed partial class ProjectSetupView : Page
{
    public ProjectSetupViewModel ViewModel { get; }

    public ProjectSetupView()
    {
        ViewModel = App.Services.GetRequiredService<ProjectSetupViewModel>();
        this.InitializeComponent();
        this.Loaded += (_, _) => ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Project project)
        {
            ViewModel.Initialize(project);
            Bindings.Update();
        }
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
        this.ActualThemeChanged += OnActualThemeChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        this.ActualThemeChanged -= OnActualThemeChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
        => ViewModel.SchedulePreviewUpdate(DispatcherQueue);

    // ── Surface Plate handlers ────────────────────────────────────────────────

    private void OnParameterChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        => ViewModel.SchedulePreviewUpdate(DispatcherQueue);

    private void OnStrategyChanged(object sender, SelectionChangedEventArgs e)
    {
        Bindings.Update();
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    private void OnRingsOptionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SchedulePreviewUpdate(DispatcherQueue);

    // ── Geometry type ─────────────────────────────────────────────────────────

    private void OnGeometryChanged(object sender, SelectionChangedEventArgs e)
    {
        Bindings.Update();
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    // ── Parallel Ways: rail handlers ─────────────────────────────────────────

    private void OnRailIsReferenceClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is RailViewModel rail)
        {
            ViewModel.SetRailAsReference(rail);
            // Restore the checked state based on model (don't let the checkbox toggle freely)
            cb.IsChecked = rail.IsReference;
        }
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    private void OnRemoveRailClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is RailViewModel rail)
            ViewModel.RemoveRailCommand.Execute(rail);
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    // ── Parallel Ways: task handlers ─────────────────────────────────────────

    private void OnTaskTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        Bindings.Update();
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    private void OnRemoveTaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TaskViewModel task)
            ViewModel.RemoveTaskCommand.Execute(task);
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    private void OnMoveTaskUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TaskViewModel task)
            ViewModel.MoveTaskUpCommand.Execute(task);
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    private void OnMoveTaskDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TaskViewModel task)
            ViewModel.MoveTaskDownCommand.Execute(task);
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    // ── Parallel Ways: generic parameter change ───────────────────────────────

    private void OnParallelWaysNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        => ViewModel.SchedulePreviewUpdate(DispatcherQueue);

    private void OnParallelWaysComboChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SchedulePreviewUpdate(DispatcherQueue);
}
