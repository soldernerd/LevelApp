using LevelApp.App.ViewModels;
using LevelApp.App.Views.Dialogs;
using LevelApp.Core.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views;

/// <summary>
/// UserControl displayed as the content of each plugin tab in
/// <see cref="InstrumentsPage"/>: the plugin's known devices (each with a Connect button when
/// the plugin has a live connection to offer), and — only while a device is connected — the
/// actions and the plugin's own surface for that device.
/// </summary>
public sealed partial class InstrumentPluginTabView : UserControl
{
    public InstrumentPluginTabViewModel ViewModel { get; }

    // The plugin's management view is created once: it owns the instrument connection, so
    // rebuilding it on every device-list change would drop the connection.
    private UIElement? _managementView;
    private IDeviceManagementHost? _host;
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
        EnsureManagementView();
        RefreshUI();
        if (_wired) return;
        _wired = true;
        ViewModel.KnownDevices.CollectionChanged += (_, _) => RefreshUI();
    }

    private void EnsureManagementView()
    {
        if (_managementView is not null) return;

        _managementView = ViewModel.Plugin.CreateDeviceManagementView(ViewModel.Registry) as UIElement;
        if (_managementView is null) return;

        PluginSpecificContent.Content = _managementView;
        _host = _managementView as IDeviceManagementHost;
        if (_host is not null)
            _host.ConnectionChanged += (_, _) => UpdateConnectedUi();
    }

    private void RefreshUI()
    {
        bool hasDevices = ViewModel.KnownDevices.Count > 0;

        NoDevicesText.Visibility = hasDevices ? Visibility.Collapsed : Visibility.Visible;

        // Add Device: hidden for always-connected plugins (manual transport)
        AddDeviceButton.Visibility = ViewModel.ShowAddDevice
            ? Visibility.Visible : Visibility.Collapsed;

        foreach (var device in ViewModel.KnownDevices)
            device.CanConnect = _host is not null;

        UpdateConnectedUi();
    }

    /// <summary>
    /// Shows what belongs to the connected device (its name, actions, the plugin's surface) and
    /// hides all of it while nothing is connected; marks the connected row in the device list.
    /// </summary>
    private void UpdateConnectedUi()
    {
        var connected = _host?.ConnectedDevice;

        foreach (var device in ViewModel.KnownDevices)
            device.IsConnected = connected is not null && device.Device.DeviceId == connected.DeviceId;

        if (connected is null)
        {
            if (_calibrationView is not null) CloseCalibration();
            ConnectedActions.Visibility = Visibility.Collapsed;
            PluginHost.Visibility       = Visibility.Collapsed;
            return;
        }

        ConnectedTitle.Text = $"{connected.DisplayName}:";
        CalibrationButton.Visibility = ViewModel.Plugin.CreateCalibrationWorkflow(connected) is not null
            ? Visibility.Visible : Visibility.Collapsed;
        FirmwareUpdateButton.Visibility = ViewModel.Plugin.CreateFirmwareUpdater(connected) is not null
            ? Visibility.Visible : Visibility.Collapsed;

        ConnectedActions.Visibility = Visibility.Visible;
        PluginHost.Visibility       = Visibility.Visible;
    }

    // ── Connect / disconnect ──────────────────────────────────────────────────

    private async void OnConnectDeviceClicked(object sender, RoutedEventArgs e)
    {
        if (_host is null || sender is not Button { DataContext: KnownDeviceViewModel vm }) return;

        ConnectError.IsOpen = false;
        vm.IsBusy = true;
        try
        {
            if (vm.IsConnected)
            {
                await _host.DisconnectAsync();
            }
            else
            {
                string? error = await _host.ConnectAsync(vm.Device);
                if (error is not null)
                {
                    ConnectError.Title   = $"Could not connect {vm.DisplayName}";
                    ConnectError.Message = error;
                    ConnectError.IsOpen  = true;
                }
            }
        }
        finally
        {
            vm.IsBusy = false;
            UpdateConnectedUi();
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
        if (sender is not Button { DataContext: KnownDeviceViewModel vm }) return;

        var dialog = new ContentDialog
        {
            Title             = "Forget Device",
            Content           = $"Remove \"{vm.DisplayName}\" from the device list?",
            PrimaryButtonText = "Remove",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        // A device that is connected is disconnected before it is forgotten.
        if (_host?.ConnectedDevice?.DeviceId == vm.Device.DeviceId)
            await _host.DisconnectAsync();

        ViewModel.ForgetDevice(vm);
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

    // ── Calibration (of the connected device) ─────────────────────────────────

    private void OnCalibrationClicked(object sender, RoutedEventArgs e)
    {
        // A second click goes back to the instrument's own view.
        if (_calibrationView is not null)
        {
            CloseCalibration();
            return;
        }

        var connected = _host?.ConnectedDevice;
        if (connected is null) return;

        var workflow = ViewModel.Plugin.CreateCalibrationWorkflow(connected);
        if (workflow?.CreateView() is not UIElement view) return;

        // Both views stay in the tree (hidden when not shown): taking the management view out
        // would let it unload while it holds the connection.
        _calibrationView                = view;
        CalibrationContent.Content      = view;
        CalibrationContent.Visibility   = Visibility.Visible;
        PluginSpecificContent.Visibility = Visibility.Collapsed;
        CalibrationButton.Content       = "← Back to instrument";
    }

    private void CloseCalibration()
    {
        _calibrationView                 = null;
        CalibrationContent.Content       = null;
        CalibrationContent.Visibility    = Visibility.Collapsed;
        PluginSpecificContent.Visibility = Visibility.Visible;
        CalibrationButton.Content        = "Calibration";
    }

    // ── Firmware Update (of the connected device) ─────────────────────────────

    private async void OnFirmwareUpdateClicked(object sender, RoutedEventArgs e)
    {
        var connected = _host?.ConnectedDevice;
        if (connected is null) return;

        var updater = ViewModel.Plugin.CreateFirmwareUpdater(connected);
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
