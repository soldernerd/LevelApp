using LevelApp.App.Navigation;
using LevelApp.App.Services;
using LevelApp.App.ViewModels;
using LevelApp.Core.Geometry.ParallelWays;
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

        // Theme
        services.AddSingleton<IThemeService, ThemeService>();

        // Navigation
        services.AddSingleton<INavigationService, NavigationService>();

        // Localisation
        services.AddSingleton<ILocalisationService, LocalisationService>();

        // Services
        services.AddSingleton<IProjectFileService, ProjectFileService>();

        // Shell ViewModel — singleton so all page VMs share the same project state
        services.AddSingleton<MainViewModel>();

        // Core calculators resolved via DI so ViewModels don't directly instantiate them
        services.AddTransient<ParallelWaysCalculator>();

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
        var settings = Services.GetRequiredService<ISettingsService>();
        settings.Load();

        // Apply language override before XAML is parsed so x:Uid strings use the right language.
        // An empty string clears the override and falls back to the system language.
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
            settings.AppLanguage ?? string.Empty;

        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private Window? _window;
}
