using LevelApp.App.Navigation;
using LevelApp.App.Services;
using LevelApp.App.ViewModels;
using LevelApp.App.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace LevelApp.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly ISettingsService _settings;
    private nint _hwnd;

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        _settings = App.Services.GetRequiredService<ISettingsService>();

        this.InitializeComponent(); // x:Bind uses ViewModel

        _hwnd = WindowNative.GetWindowHandle(this);

        // Set the window and taskbar icon
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "levelapp.ico"));

        // Initialise services that need the window handle
        App.Services.GetRequiredService<IProjectFileService>().Initialize(_hwnd);
        ViewModel.Hwnd = _hwnd;

        // Attach navigation frame
        var nav = (NavigationService)App.Services.GetRequiredService<INavigationService>();
        nav.Attach(RootFrame);

        // Update the title bar whenever the project name or dirty flag changes
        this.Title = ViewModel.WindowTitle;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.WindowTitle))
                this.Title = ViewModel.WindowTitle;
        };

        // XamlRoot is available once the content is in the visual tree
        RootFrame.Loaded += (_, _) => ViewModel.XamlRoot = RootFrame.XamlRoot;

        // Intercept the window close button for unsaved-changes handling
        AppWindow.Closing += OnAppWindowClosing;

        nav.NavigateTo(PageKey.ProjectSetup);
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Always cancel the close initially so we can show the dialog asynchronously
        args.Cancel = true;

        if (await ViewModel.ConfirmDiscardChangesAsync())
        {
            // Unhook first to prevent re-entry, then exit
            AppWindow.Closing -= OnAppWindowClosing;
            Application.Current.Exit();
        }
    }

    private async void OnPreferencesClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new PreferencesDialog(_settings, _hwnd)
        {
            XamlRoot = RootFrame.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog
        {
            XamlRoot = RootFrame.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
