using LevelApp.Core.Instruments;

namespace LevelApp.App.ViewModels;

/// <summary>
/// Display wrapper for a <see cref="KnownDevice"/> in the instrument management UI.
/// </summary>
public sealed class KnownDeviceViewModel
{
    public KnownDevice Device { get; }

    public string DisplayName    => Device.DisplayName;
    public string TransportLabel => Device.TransportId.ToUpperInvariant();

    public KnownDeviceViewModel(KnownDevice device) => Device = device;
}
