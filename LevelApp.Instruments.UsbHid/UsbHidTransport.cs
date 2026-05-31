using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;

namespace LevelApp.Instruments.UsbHid;

/// <summary>
/// Identifies and describes the USB HID transport.
/// </summary>
public sealed class UsbHidTransport : ITransport
{
    public string TransportId  => "usb-hid";
    public string DisplayName  => "USB";

    public TransportCapabilities Capabilities =>
        TransportCapabilities.SingleReading    |
        TransportCapabilities.ContinuousStream |
        TransportCapabilities.Bidirectional;
}
