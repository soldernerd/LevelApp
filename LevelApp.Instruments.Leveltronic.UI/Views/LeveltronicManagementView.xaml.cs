using LevelApp.Core.Interfaces;
using LevelApp.Instruments.Leveltronic.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.Instruments.Leveltronic.UI.Views;

/// <summary>
/// Device-management surface for a registered Leveltronic: connection, identity,
/// RTC, every Settings parameter, and the one-shot commands. Embedded by
/// <c>InstrumentPluginTabView</c> via
/// <see cref="LeveltronicPlugin.CreateDeviceManagementView"/>.
/// </summary>
public sealed partial class LeveltronicManagementView : UserControl
{
    public LeveltronicManagementViewModel ViewModel { get; }

    public LeveltronicManagementView(IDeviceRegistry registry)
    {
        ViewModel = new LeveltronicManagementViewModel(registry)
        {
            ConfirmAsync = ConfirmAsync,
        };
        DataContext = ViewModel;
        InitializeComponent();

        Loaded += (_, _) => ViewModel.UpdateTargetDevice();
        Unloaded += async (_, _) => await ViewModel.DisposeAsync();
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (XamlRoot is null) return false;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
