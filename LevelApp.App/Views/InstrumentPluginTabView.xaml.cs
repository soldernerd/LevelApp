using LevelApp.App.ViewModels;
using LevelApp.App.Views.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views;

/// <summary>
/// UserControl displayed as the content of each plugin tab in
/// <see cref="InstrumentsPage"/>.
/// </summary>
public sealed partial class InstrumentPluginTabView : UserControl
{
    public InstrumentPluginTabViewModel ViewModel { get; }

    public InstrumentPluginTabView(InstrumentPluginTabViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshUI();
        ViewModel.KnownDevices.CollectionChanged += (_, _) => RefreshUI();
    }

    private void RefreshUI()
    {
        bool hasDevices = ViewModel.KnownDevices.Count > 0;

        // Empty-state text
        NoDevicesText.Visibility = hasDevices ? Visibility.Collapsed : Visibility.Visible;

        // Add Device — hidden for always-connected plugins (manual transport)
        AddDeviceButton.Visibility = ViewModel.ShowAddDevice
            ? Visibility.Visible : Visibility.Collapsed;

        // Capability buttons — hidden when plugin doesn't support the capability
        CalibrationButton.Visibility    = ViewModel.CanCalibrate
            ? Visibility.Visible : Visibility.Collapsed;
        FirmwareUpdateButton.Visibility = ViewModel.CanUpdateFirmware
            ? Visibility.Visible : Visibility.Collapsed;
        FirmwareUpdateButton.IsEnabled  = true; // fine-grained readiness handled at click time

        // Plugin-specific management view
        var customView = ViewModel.Plugin.CreateDeviceManagementView(ViewModel.Registry);
        if (customView is UIElement uiElement)
        {
            PluginSpecificContent.Content    = uiElement;
            PluginSpecificContent.Visibility = Visibility.Visible;
        }
    }

    // ── Forget Device ─────────────────────────────────────────────────────────

    private async void OnForgetDeviceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is KnownDeviceViewModel vm)
        {
            var dialog = new ContentDialog
            {
                Title               = "Forget Device",
                Content             = $"Remove \"{vm.DisplayName}\" from the device list?",
                PrimaryButtonText   = "Remove",
                CloseButtonText     = "Cancel",
                DefaultButton       = ContentDialogButton.Close,
                XamlRoot            = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                ViewModel.ForgetDevice(vm);
        }
    }

    // ── Add Device ────────────────────────────────────────────────────────────

    private async void OnAddDeviceClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ScanForDevicesDialog(ViewModel.Plugin, ViewModel.Registry)
        {
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();

        if (dialog.AddedDevice is not null)
            ViewModel.RefreshDevices();
    }

    // ── Calibration ───────────────────────────────────────────────────────────

    private void OnCalibrationClicked(object sender, RoutedEventArgs e)
    {
        // No device selected — operate on first device as default when list has items.
        var deviceVm = ViewModel.KnownDevices.FirstOrDefault();
        if (deviceVm is null) return;

        var workflow = ViewModel.Plugin.CreateCalibrationWorkflow(deviceVm.Device);
        if (workflow is null) return;

        // Embed the calibration view in the plugin-specific area
        if (workflow.CreateView() is UIElement uiElement)
        {
            PluginSpecificContent.Content    = uiElement;
            PluginSpecificContent.Visibility = Visibility.Visible;
        }
    }

    // ── Firmware Update ───────────────────────────────────────────────────────

    private async void OnFirmwareUpdateClicked(object sender, RoutedEventArgs e)
    {
        var deviceVm = ViewModel.KnownDevices.FirstOrDefault();
        if (deviceVm is null) return;

        var updater = ViewModel.Plugin.CreateFirmwareUpdater(deviceVm.Device);
        if (updater is null) return;

        if (!updater.IsReady)
        {
            var hint = new ContentDialog
            {
                Title           = "Firmware Update",
                Content         = $"Connection required: {updater.RequiredTransport}",
                CloseButtonText = "OK",
                XamlRoot        = this.XamlRoot
            };
            await hint.ShowAsync();
            return;
        }

        var dialog = new FirmwareUpdateDialog(updater)
        {
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
