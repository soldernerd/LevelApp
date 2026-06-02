using System.Diagnostics;
using LevelApp.App.Services;
using LevelApp.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class UpdateDialog : ContentDialog
{
    private readonly UpdateInfo     _update;
    private readonly IUpdateService _updateService;
    private bool                    _downloading;

    public UpdateDialog(UpdateInfo update, IUpdateService updateService)
    {
        _update        = update;
        _updateService = updateService;

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
            // TrimEnd: AppContext.BaseDirectory has a trailing backslash, which
            // causes \"…\" in the argument string to be mis-parsed as an escaped
            // quote, merging the install-folder and exe-name arguments into one.
            string installFolder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar,
                                                                     Path.AltDirectorySeparatorChar);
            string updaterPath   = Path.Combine(AppContext.BaseDirectory, "LevelApp.Updater.exe");

            // Check before starting the download — a missing binary is a different
            // failure from a network error and gives a more accurate error message.
            if (!File.Exists(updaterPath))
            {
                args.Cancel            = true;
                StatusText.Text        = "Update failed: updater binary not found.";
                IsPrimaryButtonEnabled = true;
                _downloading           = false;
                return;
            }

            string zipPath;
            try
            {
                var progress = new Progress<double>(p => DownloadProgress.Value = p);
                zipPath = await _updateService.DownloadUpdateAsync(_update, progress);
            }
            catch
            {
                args.Cancel            = true;
                StatusText.Text        = "Download failed. Please try again later.";
                IsPrimaryButtonEnabled = true;
                _downloading           = false;
                return;
            }

            StatusText.Text = "Restarting…";

            // Build arguments in contract-defined order so any positional change
            // to UpdaterContract causes a compile error here rather than a runtime mis-parse.
            var argArray = new string[UpdaterContract.ExpectedArgCount];
            argArray[UpdaterContract.ArgZipPath]       = $"\"{zipPath}\"";
            argArray[UpdaterContract.ArgInstallFolder] = $"\"{installFolder}\"";
            argArray[UpdaterContract.ArgMainExeName]   = "\"LevelApp.App.exe\"";

            Process.Start(new ProcessStartInfo
            {
                FileName        = updaterPath,
                Arguments       = string.Join(" ", argArray),
                UseShellExecute = true
            });

            Application.Current.Exit();
            // deferral.Complete() is called in the finally block below — this is fine because
            // Application.Current.Exit() schedules shutdown rather than immediately terminating.
        }
        catch
        {
            args.Cancel            = true;
            StatusText.Text        = "Update failed: could not launch updater. Please try again.";
            IsPrimaryButtonEnabled = true;
            _downloading           = false;
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
