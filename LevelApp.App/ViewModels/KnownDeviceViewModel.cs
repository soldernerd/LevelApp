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

    /// <summary>
    /// The technical identifier shown next to a custom name: the name the device was found under
    /// (e.g. <c>Leveltronic_4651</c>), or for a Bluetooth device registered without one its address
    /// (<c>8C:71:12:93:46:51</c>). Empty when it adds nothing.
    /// </summary>
    public string TechnicalLabel
    {
        get
        {
            string? technical = Device.TechnicalName;
            if (string.IsNullOrEmpty(technical) && Device.TransportId == "ble")
                technical = FormatMac(Device.TransportAddress);
            return string.IsNullOrEmpty(technical) || technical == Device.DisplayName ? "" : technical;
        }
    }

    public Visibility TechnicalVisibility =>
        TechnicalLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    private static string FormatMac(string address) =>
        address.Length == 12 && address.All(Uri.IsHexDigit)
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => address.Substring(i * 2, 2)))
            : address;

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
