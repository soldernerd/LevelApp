using LevelApp.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class PreferencesDialog : ContentDialog
{
    private readonly ISettingsService   _settings;
    private readonly nint               _hwnd;
    private readonly Action<ElementTheme> _applyTheme;
    private readonly ElementTheme       _originalTheme;

    // ── ThemeIndex ────────────────────────────────────────────────────────────
    // 0 = Follow system (ElementTheme.Default)
    // 1 = Light          (ElementTheme.Light)
    // 2 = Dark           (ElementTheme.Dark)

    private int _themeIndex;

    /// <summary>
    /// Bound two-way to the RadioButtons in XAML. Each change applies a live
    /// theme preview via the callback from MainWindow.
    /// </summary>
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (_themeIndex == value) return;
            _themeIndex = value;
            _applyTheme(IndexToTheme(value));
        }
    }

    public PreferencesDialog(ISettingsService settings, nint hwnd, Action<ElementTheme> applyTheme)
    {
        _settings      = settings;
        _hwnd          = hwnd;
        _applyTheme    = applyTheme;
        _originalTheme = settings.AppTheme;
        _themeIndex    = ThemeToIndex(settings.AppTheme);

        InitializeComponent();
        FolderPathBox.Text = _settings.DefaultProjectFolder;
    }

    private void OnOkClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _settings.DefaultProjectFolder = FolderPathBox.Text;
        _settings.AppTheme             = IndexToTheme(ThemeIndex);
        _settings.Save();
        // Theme is already applied live; nothing more needed here.
    }

    private void OnCancelClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Revert any live theme preview change.
        _applyTheme(_originalTheme);
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
}
