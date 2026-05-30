using LevelApp.Core.Instruments;

namespace LevelApp.Core.Interfaces;

public interface IDeviceRegistry
{
    IReadOnlyList<KnownDevice> GetKnownDevices(string pluginId);
    IReadOnlyList<KnownDevice> GetAllKnownDevices();
    void RegisterDevice(KnownDevice device);
    void ForgetDevice(string deviceId);
    KnownDevice? GetPreferredDevice(string pluginId);
    void SetPreferredDevice(string pluginId, string deviceId);
}
