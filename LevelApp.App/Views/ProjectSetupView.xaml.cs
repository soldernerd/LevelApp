using LevelApp.App.ViewModels;
using LevelApp.Core.Models;
using Microsoft.Extensions.DependencyInjection;
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
    }

    private void OnParameterChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        => ViewModel.SchedulePreviewUpdate(DispatcherQueue);

    private void OnStrategyChanged(object sender, SelectionChangedEventArgs e)
    {
        Bindings.Update();
        ViewModel.SchedulePreviewUpdate(DispatcherQueue);
    }

    private void OnRingsOptionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SchedulePreviewUpdate(DispatcherQueue);
}
