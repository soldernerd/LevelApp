namespace LevelApp.Core.Instruments;

public record KnownDevice(
    string DeviceId,          // stable hardware identifier
    string PluginId,          // matches IInstrumentPlugin.PluginId
    string TransportId,       // matches ITransport.TransportId
    string DisplayName,       // user-visible name
    string TransportAddress   // BLE MAC, USB path, etc.
);
