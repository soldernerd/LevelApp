using System.Diagnostics;
using LevelApp.App.Services;
using LevelApp.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class UpdateDialog : ContentDialog
{
    private readonly UpdateInfo     _update;
    private readonly IUpdateService _updateService;
    private bool                    _downloading;

    public UpdateDialog(UpdateInfo update)
    {
        _update        = update;
        _updateService = App.Services.GetRequiredService<IUpdateService>();

        InitializeComponent();

        VersionInfoText.Text  = $"Version {update.Version} is available (you have {AppVersion.Full}).";
        ReleaseNotesText.Text = update.ReleaseNotes;
    }

    private async void OnUpdateNowClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        _downloading           = true;
        IsPrimaryButtonEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Text        = "Downloading…";
        StatusText.Visibility  = Visibility.Visible;

        try
        {
            var progress = new Progress<double>(p => DownloadProgress.Value = p);
            string zipPath = await _updateService.DownloadUpdateAsync(_update, progress);

            StatusText.Text = "Restarting…";

            // TrimEnd: AppContext.BaseDirectory has a trailing backslash, which
            // causes \"…\" in the argument string to be mis-parsed as an escaped
            // quote, merging the install-folder and exe-name arguments into one.
            string installFolder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar,
                                                                     Path.AltDirectorySeparatorChar);
            string updaterPath   = Path.Combine(AppContext.BaseDirectory, "LevelApp.Updater.exe");

            Process.Start(new ProcessStartInfo
            {
                FileName        = updaterPath,
                Arguments       = $"\"{zipPath}\" \"{installFolder}\" \"LevelApp.App.exe\"",
                UseShellExecute = true
            });

            Application.Current.Exit();
            // App exits; deferral is never completed but that is intentional.
        }
        catch
        {
            args.Cancel    = true; // Keep dialog open to show the error
            StatusText.Text = "Download failed. Please try again later.";
            IsPrimaryButtonEnabled = true;
            _downloading   = false;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnNotNowClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Prevent dismissal while a download is in progress.
        if (_downloading) args.Cancel = true;
    }
}
