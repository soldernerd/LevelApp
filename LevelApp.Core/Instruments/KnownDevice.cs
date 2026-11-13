namespace LevelApp.Core.Instruments;

public record KnownDevice(
    string DeviceId,          // stable hardware identifier
    string PluginId,          // matches IInstrumentPlugin.PluginId
    string TransportId,       // matches ITransport.TransportId
    string DisplayName,       // user-visible name
    string TransportAddress,  // BLE MAC, USB path, etc.
    string? TechnicalName = null // name the device was found under (BLE name, USB name + serial); kept when the user renames it
)
{
    /// <summary>
    /// The technical identifier(s) of the device next to its given name: the name it was found under
    /// (e.g. <c>Leveltronic_4651</c>) and, for Bluetooth, its address (<c>8C:71:12:93:46:51</c>).
    /// A method, not a property, so it does not end up in the registry file.
    /// </summary>
    public string GetTechnicalId()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(TechnicalName) && TechnicalName != DisplayName)
            parts.Add(TechnicalName);
        if (TransportId == "ble" && TransportAddress.Length > 0)
            parts.Add(FormatAddress(TransportAddress));
        return string.Join(" \u00b7 ", parts);
    }

    private static string FormatAddress(string address) =>
        address.Length == 12 && address.All(Uri.IsHexDigit)
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => address.Substring(i * 2, 2)))
            : address;
}
