using LevelApp.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class PreferencesDialog : ContentDialog
{
    private readonly ISettingsService _settings;
    private readonly nint             _hwnd;
    private readonly IThemeService    _theme;
    private readonly ElementTheme     _originalTheme;

    // ── ThemeIndex ────────────────────────────────────────────────────────────
    // 0 = Follow system (ElementTheme.Default)
    // 1 = Light          (ElementTheme.Light)
    // 2 = Dark           (ElementTheme.Dark)

    private int _themeIndex;

    /// <summary>
    /// Bound two-way to the RadioButtons in XAML. Each change applies a live
    /// theme preview via <see cref="IThemeService"/>.
    /// </summary>
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (_themeIndex == value) return;
            _themeIndex = value;
            _theme.Apply(IndexToTheme(value));
        }
    }

    // ── LanguageIndex ─────────────────────────────────────────────────────────
    // 0 = Follow system (null)
    // 1 = English        ("en-US")
    // 2 = Deutsch        ("de-DE")

    public int LanguageIndex { get; set; }

    public PreferencesDialog(ISettingsService settings, nint hwnd, IThemeService theme)
    {
        _settings      = settings;
        _hwnd          = hwnd;
        _theme         = theme;
        _originalTheme = settings.AppTheme;
        _themeIndex    = ThemeToIndex(settings.AppTheme);
        LanguageIndex  = LanguageToIndex(settings.AppLanguage);

        InitializeComponent();

        FolderPathBox.Text = _settings.DefaultProjectFolder;
    }

    private void OnOkClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _settings.DefaultProjectFolder = FolderPathBox.Text;
        _settings.AppTheme             = IndexToTheme(ThemeIndex);
        _settings.AppLanguage          = IndexToLanguage(LanguageIndex);
        _settings.Save();
        // Theme is already applied live; nothing more needed here.
        // Language takes effect on next app start (set via ApplicationLanguages.PrimaryLanguageOverride in App()).
    }

    private void OnCancelClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Revert any live theme preview change.
        _theme.Apply(_originalTheme);
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            FolderPathBox.Text = folder.Path;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ThemeToIndex(ElementTheme theme) => theme switch
    {
        ElementTheme.Light => 1,
        ElementTheme.Dark  => 2,
        _                  => 0   // Default / Follow system
    };

    private static ElementTheme IndexToTheme(int index) => index switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private static int LanguageToIndex(string? lang) => lang switch
    {
        "en-US" => 1,
        "de-DE" => 2,
        _       => 0   // null / unknown = follow system
    };

    private static string? IndexToLanguage(int index) => index switch
    {
        1 => "en-US",
        2 => "de-DE",
        _ => null
    };
}
