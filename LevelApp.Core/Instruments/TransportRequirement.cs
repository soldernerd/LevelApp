namespace LevelApp.Core.Instruments;

public enum TransportRequirement
{
    None,       // no transport needed (e.g. manual entry)
    Any,        // any connected transport suffices
    BleOnly,    // requires Bluetooth LE
    UsbOnly,    // requires USB (e.g. DFU firmware update)
    UsbOrBle,   // either will do
}
