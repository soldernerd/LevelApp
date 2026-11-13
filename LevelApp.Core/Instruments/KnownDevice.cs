namespace LevelApp.Core.Instruments;

public record KnownDevice(
    string DeviceId,          // stable hardware identifier
    string PluginId,          // matches IInstrumentPlugin.PluginId
    string TransportId,       // matches ITransport.TransportId
    string DisplayName,       // user-visible name
    string TransportAddress,  // BLE MAC, USB path, etc.
    string? TechnicalName = null // name the device was found under (BLE name, USB name + serial); kept when the user renames it
);
