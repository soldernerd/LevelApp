using LevelApp.App.Navigation;
using LevelApp.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace LevelApp.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        var nav = (NavigationService)App.Services.GetRequiredService<INavigationService>();
        nav.Attach(RootFrame);

        var fileService = App.Services.GetRequiredService<ProjectFileService>();
        fileService.Initialize(WindowNative.GetWindowHandle(this));

        nav.NavigateTo(PageKey.ProjectSetup);
    }
}
