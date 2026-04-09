using System.Text.Json;
using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

/// <summary>
/// Persists application settings to settings.json in ApplicationData.Current.LocalFolder.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private string _defaultProjectFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private ElementTheme? _appTheme;

    public string DefaultProjectFolder
    {
        get => _defaultProjectFolder;
        set => _defaultProjectFolder = value;
    }

    public ElementTheme AppTheme
    {
        get => _appTheme ?? ElementTheme.Default;
        set => _appTheme = value;
    }

    public void Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data?.DefaultProjectFolder is { Length: > 0 } folder)
                _defaultProjectFolder = folder;
            if (data?.AppTheme is { Length: > 0 } ts
                && Enum.TryParse<ElementTheme>(ts, out var t))
                _appTheme = t;
        }
        catch { /* ignore corrupt or missing settings file */ }
    }

    public void Save()
    {
        try
        {
            string path = GetSettingsPath();
            var data = new SettingsData
            {
                DefaultProjectFolder = _defaultProjectFolder,
                AppTheme             = _appTheme?.ToString()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch { /* best effort */ }
    }

    private static string GetSettingsPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LevelApp");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    private sealed class SettingsData
    {
        public string? DefaultProjectFolder { get; set; }
        public string? AppTheme             { get; set; }
    }
}
