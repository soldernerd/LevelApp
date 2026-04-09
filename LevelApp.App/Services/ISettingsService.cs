using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

public interface ISettingsService
{
    string DefaultProjectFolder { get; set; }
    ElementTheme AppTheme { get; set; }
    string? AppLanguage { get; set; }   // null = follow system; "en-US" / "de-DE" = explicit override
    void Load();
    void Save();
}
