using System.Text.Json;

namespace LevelApp.Instruments.Leveltronic.Preferences;

/// <summary>Shim height under one end of a baseline of known length (gain calibration).</summary>
public sealed record ShimSetup(double HeightMm, double BaselineMm);

/// <summary>
/// Per-instrument (keyed by serial) values the app remembers: the Zero Diff
/// offset, the selected reading channel and the gain-calibration shim setup.
/// </summary>
public interface ILeveltronicPreferences
{
    /// <summary>Offset subtracted from Diff, mm/m; null = not zeroed.</summary>
    double? GetTare(string serial);
    void SetTare(string serial, double? mmPerM);

    string? GetChannel(string serial);
    void SetChannel(string serial, string channelId);

    ShimSetup? GetShim(string serial, int sensor);
    void SetShim(string serial, int sensor, ShimSetup setup);
}

/// <summary>The remembered values of one instrument (the JSON file's record).</summary>
public sealed class PreferenceEntry
{
    public double? Tare { get; set; }
    public string? Channel { get; set; }
    public ShimSetup?[] Shim { get; set; } = new ShimSetup?[2];
}

/// <summary>Process-lifetime store (tests, and the fallback when the file cannot be used).</summary>
public class InMemoryLeveltronicPreferences : ILeveltronicPreferences
{
    protected readonly Dictionary<string, PreferenceEntry> Entries = [];
    protected readonly object Gate = new();

    protected PreferenceEntry For(string serial)
    {
        if (!Entries.TryGetValue(serial, out var e)) Entries[serial] = e = new PreferenceEntry();
        if (e.Shim.Length < 2) e.Shim = [.. e.Shim, null, null];
        return e;
    }

    protected virtual void Changed() { }

    public double? GetTare(string serial) { lock (Gate) return For(serial).Tare; }
    public void SetTare(string serial, double? v) { lock (Gate) { For(serial).Tare = v; Changed(); } }

    public string? GetChannel(string serial) { lock (Gate) return For(serial).Channel; }
    public void SetChannel(string serial, string channelId) { lock (Gate) { For(serial).Channel = channelId; Changed(); } }

    public ShimSetup? GetShim(string serial, int sensor) { lock (Gate) return For(serial).Shim[sensor]; }
    public void SetShim(string serial, int sensor, ShimSetup s) { lock (Gate) { For(serial).Shim[sensor] = s; Changed(); } }
}

/// <summary>
/// JSON file store (<c>%LOCALAPPDATA%\LevelApp\leveltronic.json</c> by default).
/// Like the phone app it is best effort: an unreadable or unwritable file just
/// means the values are not remembered.
/// </summary>
public sealed class JsonLeveltronicPreferences : InMemoryLeveltronicPreferences
{
    private readonly string _path;

    public JsonLeveltronicPreferences(string path)
    {
        _path = path;
        Load();
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LevelApp", "leveltronic.json");

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, PreferenceEntry>>(File.ReadAllText(_path));
            if (data is null) return;
            lock (Gate)
                foreach (var (k, v) in data) Entries[k] = v;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // not remembered
        }
    }

    protected override void Changed()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not remembered
        }
    }
}
