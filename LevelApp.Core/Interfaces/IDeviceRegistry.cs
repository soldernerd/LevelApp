using LevelApp.Core.Instruments;

namespace LevelApp.Core.Interfaces;

public interface IDeviceRegistry
{
    /// <summary>Non-null if the registry file could not be read on startup; null otherwise.</summary>
    string? LoadError { get; }

    IReadOnlyList<KnownDevice> GetKnownDevices(string pluginId);
    IReadOnlyList<KnownDevice> GetAllKnownDevices();
    void RegisterDevice(KnownDevice device);
    void ForgetDevice(string deviceId);
    KnownDevice? GetPreferredDevice(string pluginId);
    void SetPreferredDevice(string pluginId, string deviceId);
}
