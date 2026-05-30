using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;

namespace LevelApp.Instruments.BLE;

/// <summary>
/// Identifies and describes the Bluetooth Low Energy transport.
/// </summary>
public sealed class BleTransport : ITransport
{
    public string TransportId  => "ble";
    public string DisplayName  => "Bluetooth";

    public TransportCapabilities Capabilities =>
        TransportCapabilities.SingleReading    |
        TransportCapabilities.ContinuousStream |
        TransportCapabilities.Bidirectional;
}
