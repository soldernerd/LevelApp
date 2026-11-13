using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.Instruments.Leveltronic.UI.Views;

/// <summary>
/// Device-management surface for a registered Leveltronic: connection, info and
/// health, settings, actions, calibration values and signal diagnostics — all the
/// phone app offers besides the live screen and the wizards. Embedded by
/// <c>InstrumentPluginTabView</c> via <see cref="LeveltronicPlugin.CreateDeviceManagementView"/>.
/// </summary>
public sealed partial class LeveltronicManagementView : UserControl, IDeviceManagementHost
{
    public LeveltronicManagementViewModel ViewModel { get; }

    public LeveltronicManagementView(
        LeveltronicSessionManager sessions, IDeviceRegistry registry, ILeveltronicPreferences preferences)
    {
        ViewModel = new LeveltronicManagementViewModel(sessions, registry, preferences)
        {
            ConfirmAsync = ConfirmAsync,
        };
        InitializeComponent();

        // The tab views take their view model in the constructor, so they are created here.
        LiveTab.Content        = new LiveView(ViewModel.Live);
        ScopeTab.Content       = new ScopeView(ViewModel.Scope);
        DiagnosticsTab.Content = new DiagnosticsView(ViewModel.Diagnostics);

        Loaded += (_, _) =>
        {
            ViewModel.UpdateTargetDevice();
            OnTabChanged(Tabs, null!);
        };
        // No dispose on Unloaded: the Instruments tab swaps this view out of the tree when the
        // operator looks at another plugin tab, which must not drop the connection. The
        // connection ends with Disconnect, a lost link, or the process.
        ViewModel.ConnectionChanged += (_, _) => ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── IDeviceManagementHost ────────────────────────────────────────────────

    public KnownDevice? ConnectedDevice => ViewModel.ConnectedDevice;

    public event EventHandler? ConnectionChanged;

    public Task<string?> ConnectAsync(KnownDevice device) => ViewModel.ConnectToAsync(device);

    public Task DisconnectAsync() => ViewModel.DisconnectDeviceAsync();

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.ActiveTab = (Tabs.SelectedItem as TabViewItem)?.Tag as string ?? "";
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (XamlRoot is null) return false;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
