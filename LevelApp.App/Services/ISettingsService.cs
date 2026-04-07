namespace LevelApp.App.Services;

public interface ISettingsService
{
    string DefaultProjectFolder { get; set; }
    void Load();
    void Save();
}
