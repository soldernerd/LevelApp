using LevelApp.Core.Instruments;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// Implemented by the object returned from <see cref="IInstrumentPlugin.CreateDeviceManagementView"/>
/// when the plugin's management surface owns a live connection to one registered device. The
/// Instruments tab uses it to put a Connect / Disconnect button next to each device and to show
/// the connected-device surface and actions only while a device is connected.
/// </summary>
public interface IDeviceManagementHost
{
    /// <summary>The device that is connected right now, or null.</summary>
    KnownDevice? ConnectedDevice { get; }

    /// <summary>Raised (on the UI thread) when a device connects, disconnects or the link is lost.</summary>
    event EventHandler? ConnectionChanged;

    /// <summary>
    /// Connects <paramref name="device"/> (first disconnecting another one).
    /// Returns null on success, otherwise a message fit for the operator.
    /// </summary>
    Task<string?> ConnectAsync(KnownDevice device);

    /// <summary>Disconnects the connected device, if any.</summary>
    Task DisconnectAsync();
}
