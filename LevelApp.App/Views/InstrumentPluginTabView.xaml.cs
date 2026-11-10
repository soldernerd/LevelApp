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

    // The plugin's management view is created once: it owns the instrument connection, so
    // rebuilding it on every device-list change (as before) would drop the connection.
    private UIElement? _managementView;
    private UIElement? _calibrationView;
    private bool _wired;

    public InstrumentPluginTabView(InstrumentPluginTabViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshUI();
        if (_wired) return;
        _wired = true;
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

        // Plugin-specific management view (created once, see _managementView)
        _managementView ??= ViewModel.Plugin.CreateDeviceManagementView(ViewModel.Registry) as UIElement;
        if (_managementView is not null && _calibrationView is null)
        {
            PluginSpecificContent.Content    = _managementView;
            PluginSpecificContent.Visibility = Visibility.Visible;

            // The instrument's own tabs get the room; the device list folds away once a device exists.
            DevicesExpander.IsExpanded = !hasDevices;
        }
    }

    // ── Rename Device ─────────────────────────────────────────────────────────

    private async void OnRenameDeviceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: KnownDeviceViewModel vm }) return;

        var box = new TextBox { Text = vm.DisplayName, MinWidth = 280, SelectionStart = 0 };
        box.SelectAll();
        var dialog = new ContentDialog
        {
            Title             = "Name this instrument",
            Content           = box,
            PrimaryButtonText = "Save",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Primary,
            XamlRoot          = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        string name = box.Text.Trim();
        if (name.Length == 0 || name == vm.DisplayName) return;

        ViewModel.RenameDevice(vm, name);
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
        // Second click goes back to the instrument's own view.
        if (_calibrationView is not null)
        {
            CloseCalibration();
            return;
        }

        var deviceVm = ViewModel.KnownDevices.FirstOrDefault();
        if (deviceVm is null) return;

        var workflow = ViewModel.Plugin.CreateCalibrationWorkflow(deviceVm.Device);
        if (workflow is null) return;

        // Show the calibration view in the plugin-specific area, in place of the instrument view.
        if (workflow.CreateView() is UIElement uiElement)
        {
            _calibrationView                 = uiElement;
            PluginSpecificContent.Content    = uiElement;
            PluginSpecificContent.Visibility = Visibility.Visible;
            CalibrationButton.Content        = "← Back to instrument";
            DevicesExpander.IsExpanded       = false;
        }
    }

    private void CloseCalibration()
    {
        _calibrationView          = null;
        CalibrationButton.Content = "Calibration";
        if (_managementView is not null)
            PluginSpecificContent.Content = _managementView;
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
