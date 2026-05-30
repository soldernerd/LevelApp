using LevelApp.Core.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

/// <summary>
/// Dialog for checking and applying instrument firmware updates.
/// </summary>
public sealed partial class FirmwareUpdateDialog : ContentDialog
{
    private readonly IFirmwareUpdater _updater;
    private CancellationTokenSource?  _updateCts;

    public FirmwareUpdateDialog(IFirmwareUpdater updater)
    {
        _updater = updater;
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Fetch current and available firmware info in parallel
            var current   = await _updater.GetCurrentFirmwareAsync();
            var available = await _updater.CheckForUpdateAsync();

            CheckingRow.Visibility = Visibility.Collapsed;

            if (available is null)
            {
                // Already up to date
                CurrentVersionText.Text    = current.Version;
                VersionPanel.Visibility    = Visibility.Visible;
                AvailableVersionText.Text  = "(none)";
                UpToDateText.Visibility    = Visibility.Visible;
                IsPrimaryButtonEnabled     = false;
            }
            else
            {
                CurrentVersionText.Text   = current.Version;
                AvailableVersionText.Text = available.Version;
                VersionPanel.Visibility   = Visibility.Visible;

                if (!string.IsNullOrWhiteSpace(available.ReleaseNotes))
                {
                    ReleaseNotesText.Text      = available.ReleaseNotes;
                    ReleaseNotesPanel.Visibility = Visibility.Visible;
                }

                IsPrimaryButtonEnabled = _updater.IsReady;
            }
        }
        catch (Exception ex)
        {
            CheckingRow.Visibility = Visibility.Collapsed;
            ErrorBar.Message       = ex.Message;
            ErrorBar.IsOpen        = true;
            IsPrimaryButtonEnabled = false;
        }
    }

    private async void OnUpdateNowClicked(object sender, ContentDialogButtonClickEventArgs e)
    {
        // Defer the close so we can run the update and show completion in-dialog
        var deferral = e.GetDeferral();

        IsPrimaryButtonEnabled  = false;
        IsSecondaryButtonEnabled = false;

        _updateCts = new CancellationTokenSource();

        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<double>(pct =>
        {
            UpdateProgress.Value = pct * 100;
            ProgressText.Text    = $"Updating… {pct:P0}";
        });

        try
        {
            await _updater.PerformUpdateAsync(progress, _updateCts.Token);
            ProgressPanel.Visibility = Visibility.Collapsed;
            CompleteBar.IsOpen       = true;
            CloseButtonText          = "Close";
        }
        catch (OperationCanceledException)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorBar.Message         = ex.Message;
            ErrorBar.IsOpen          = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
