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
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Project project)
        {
            ViewModel.Initialize(project);
            // Force compiled x:Bind to re-read all properties — PropertyChanged alone
            // is not reliably applied during OnNavigatedTo (especially for NumberBox).
            Bindings.Update();
        }
    }
}
