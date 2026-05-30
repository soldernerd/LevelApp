using LevelApp.Core.Instruments;

namespace LevelApp.Core.Interfaces;

public interface ITransport
{
    string TransportId { get; }       // "ble", "usb-hid", "manual"
    string DisplayName { get; }       // "Bluetooth", "USB", "Manual"
    TransportCapabilities Capabilities { get; }
}
