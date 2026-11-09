using LevelApp.Core.Interfaces;
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
public sealed partial class LeveltronicManagementView : UserControl
{
    public LeveltronicManagementViewModel ViewModel { get; }

    public LeveltronicManagementView(LeveltronicSessionManager sessions, IDeviceRegistry registry)
    {
        ViewModel = new LeveltronicManagementViewModel(sessions, registry)
        {
            ConfirmAsync = ConfirmAsync,
        };
        InitializeComponent();

        Loaded += (_, _) =>
        {
            ViewModel.UpdateTargetDevice();
            OnTabChanged(Tabs, null!);
        };
        Unloaded += async (_, _) => await ViewModel.DisposeAsync();
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        string? tag = (Tabs.SelectedItem as PivotItem)?.Tag as string;
        ViewModel.InfoVisible = tag == "info";
        ViewModel.DiagnosticsVisible = tag == "diagnostics";
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
