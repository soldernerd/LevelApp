using CommunityToolkit.Mvvm.ComponentModel;
using LevelApp.Core.Instruments;
using Microsoft.UI.Xaml;

namespace LevelApp.App.ViewModels;

/// <summary>
/// Display wrapper for a <see cref="KnownDevice"/> in the instrument management UI:
/// the device, whether the plugin can connect it, and its connection state.
/// </summary>
public sealed partial class KnownDeviceViewModel : ObservableObject
{
    public KnownDevice Device { get; }

    public string DisplayName    => Device.DisplayName;
    public string TransportLabel => Device.TransportId.ToUpperInvariant();

    public KnownDeviceViewModel(KnownDevice device) => Device = device;

    /// <summary>The plugin has a live connection to offer (shows the Connect button).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectVisibility))]
    private bool _canConnect;

    /// <summary>This device is the one currently connected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    [NotifyPropertyChangedFor(nameof(ConnectedVisibility))]
    private bool _isConnected;

    /// <summary>A connect or disconnect is in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(BusyVisibility))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";

    public Visibility ConnectVisibility   => CanConnect ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ConnectedVisibility => IsConnected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BusyVisibility      => IsBusy ? Visibility.Visible : Visibility.Collapsed;
}
