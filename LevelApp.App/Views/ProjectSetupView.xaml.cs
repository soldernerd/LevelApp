using LevelApp.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views;

public sealed partial class ProjectSetupView : Page
{
    public ProjectSetupViewModel ViewModel { get; }

    public ProjectSetupView()
    {
        ViewModel = App.Services.GetRequiredService<ProjectSetupViewModel>();
        this.InitializeComponent();
    }
}
