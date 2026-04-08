using LevelApp.App.Navigation;
using LevelApp.App.Services;
using LevelApp.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace LevelApp.App;

public partial class App : Application
{
    /// <summary>Application-wide DI container. Available after the constructor returns.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Settings
        services.AddSingleton<ISettingsService, SettingsService>();

        // Navigation
        services.AddSingleton<INavigationService, NavigationService>();

        // Services
        services.AddSingleton<IProjectFileService, ProjectFileService>();

        // Shell ViewModel — singleton so all page VMs share the same project state
        services.AddSingleton<MainViewModel>();

        // ViewModels — Transient so each navigation gets a fresh instance
        services.AddTransient<ProjectSetupViewModel>();
        services.AddTransient<MeasurementViewModel>();
        services.AddTransient<ResultsViewModel>();
        services.AddTransient<CorrectionViewModel>();

        return services.BuildServiceProvider();
    }

    public App()
    {
        Services = BuildServiceProvider();

        // Load persisted settings before any UI is created
        Services.GetRequiredService<ISettingsService>().Load();

        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private Window? _window;
}
