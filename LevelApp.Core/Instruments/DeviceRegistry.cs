using System.Text.Json;
using LevelApp.Core.Interfaces;

namespace LevelApp.Core.Instruments;

/// <summary>
/// In-memory device registry with optional JSON persistence to a file.
/// Pass a file path to the constructor for persisted operation (App), or
/// omit it for in-memory-only use (unit tests).
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly string? _filePath;
    private readonly List<KnownDevice> _devices = [];
    private readonly Dictionary<string, string> _preferred = new(); // pluginId → deviceId

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DeviceRegistry(string? filePath = null)
    {
        _filePath = filePath;
        if (filePath is not null && File.Exists(filePath))
            Load(filePath);
    }

    // ── IDeviceRegistry ───────────────────────────────────────────────────────

    public IReadOnlyList<KnownDevice> GetKnownDevices(string pluginId) =>
        _devices.Where(d => d.PluginId == pluginId).ToList();

    public IReadOnlyList<KnownDevice> GetAllKnownDevices() =>
        _devices.AsReadOnly();

    public void RegisterDevice(KnownDevice device)
    {
        var existing = _devices.FindIndex(d => d.DeviceId == device.DeviceId);
        if (existing >= 0)
            _devices[existing] = device;
        else
            _devices.Add(device);
        Save();
    }

    public void ForgetDevice(string deviceId)
    {
        _devices.RemoveAll(d => d.DeviceId == deviceId);
        // Remove from preferred if needed
        var toRemove = _preferred
            .Where(kv => kv.Value == deviceId)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in toRemove) _preferred.Remove(key);
        Save();
    }

    public KnownDevice? GetPreferredDevice(string pluginId)
    {
        if (!_preferred.TryGetValue(pluginId, out var deviceId)) return null;
        return _devices.FirstOrDefault(d => d.DeviceId == deviceId);
    }

    public void SetPreferredDevice(string pluginId, string deviceId)
    {
        _preferred[pluginId] = deviceId;
        Save();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private sealed record RegistryData(
        List<KnownDevice> Devices,
        Dictionary<string, string> Preferred);

    private void Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<RegistryData>(json, _json);
            if (data is null) return;
            _devices.AddRange(data.Devices);
            foreach (var kv in data.Preferred) _preferred[kv.Key] = kv.Value;
        }
        catch (JsonException) { /* corrupt file — start fresh */ }
    }

    private void Save()
    {
        if (_filePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var data = new RegistryData([.. _devices], new(_preferred));
            File.WriteAllText(_filePath, JsonSerializer.Serialize(data, _json));
        }
        catch (IOException) { /* best-effort */ }
    }
}
