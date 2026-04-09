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
        Services.GetRequiredService<ISettingsService>().Load();

        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Apply language qualifier before any page XAML is parsed.
        // Must run after InitializeComponent() so WinRT is fully initialised.
        // Page XAMLs are lazy-loaded on first navigation, so this still takes effect.
        var lang = Services.GetRequiredService<ISettingsService>().AppLanguage;
        if (!string.IsNullOrEmpty(lang))
            Windows.ApplicationModel.Resources.Core.ResourceContext
                .SetGlobalQualifierValue("Language", lang);

        _window = new MainWindow();
        _window.Activate();
    }

    private Window? _window;
}
