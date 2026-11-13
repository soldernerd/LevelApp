using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

public interface ISettingsService
{
    string       DefaultProjectFolder    { get; set; }
    ElementTheme AppTheme                { get; set; }
    bool         ActivityLoggingEnabled  { get; set; }

    /// <summary>Path of the project that was open last; offered for reopening at the next start.</summary>
    string?      LastProjectPath         { get; set; }

    void Load();
    void Save();
}
