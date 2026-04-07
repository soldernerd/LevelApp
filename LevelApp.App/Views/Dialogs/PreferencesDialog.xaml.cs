using LevelApp.App.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class PreferencesDialog : ContentDialog
{
    private readonly ISettingsService _settings;
    private readonly nint _hwnd;

    public PreferencesDialog(ISettingsService settings, nint hwnd)
    {
        _settings = settings;
        _hwnd     = hwnd;
        InitializeComponent();
        FolderPathBox.Text = _settings.DefaultProjectFolder;
    }

    private void OnOkClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _settings.DefaultProjectFolder = FolderPathBox.Text;
        _settings.Save();
    }

    private async void OnBrowseClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            FolderPathBox.Text = folder.Path;
    }
}
