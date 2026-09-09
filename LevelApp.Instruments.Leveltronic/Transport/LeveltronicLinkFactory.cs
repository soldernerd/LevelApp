using LevelApp.Core.Instruments;
using LevelApp.Instruments.BLE;
using LevelApp.Instruments.Leveltronic.Protocol;
using LevelApp.Instruments.UsbHid;

namespace LevelApp.Instruments.Leveltronic.Transport;

/// <summary>
/// Builds the right <see cref="ILeveltronicLink"/> for a registered device from
/// its <see cref="KnownDevice.TransportId"/>.
/// </summary>
public static class LeveltronicLinkFactory
{
    /// <summary>The BLE transport id (<c>"ble"</c>).</summary>
    public static readonly string BleTransportId = new BleTransport().TransportId;

    /// <summary>The USB HID transport id (<c>"usb-hid"</c>).</summary>
    public static readonly string UsbTransportId = new UsbHidTransport().TransportId;

    /// <exception cref="NotSupportedException">The device's transport is not USB or BLE.</exception>
    public static ILeveltronicLink Create(KnownDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.TransportId == BleTransportId)
            return new BleLeveltronicLink(device.TransportAddress);
        if (device.TransportId == UsbTransportId)
            return new UsbHidLeveltronicLink(device.TransportAddress);

        throw new NotSupportedException(
            $"Leveltronic does not support transport '{device.TransportId}'.");
    }
}
