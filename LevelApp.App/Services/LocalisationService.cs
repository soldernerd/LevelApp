using Windows.ApplicationModel.Resources;

namespace LevelApp.App.Services;

public sealed class LocalisationService : ILocalisationService
{
    private readonly ResourceLoader _loader = ResourceLoader.GetForViewIndependentUse();

    public string Get(string key) => _loader.GetString(key);
}
