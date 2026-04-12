using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

public interface ISettingsService
{
    string       DefaultProjectFolder    { get; set; }
    ElementTheme AppTheme                { get; set; }
    bool         ActivityLoggingEnabled  { get; set; }

    void Load();
    void Save();
}
