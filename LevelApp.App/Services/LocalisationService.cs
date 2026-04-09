using Microsoft.Windows.ApplicationModel.Resources;

namespace LevelApp.App.Services;

public sealed class LocalisationService : ILocalisationService
{
    private readonly ResourceLoader _loader = new ResourceLoader();

    public string Get(string key) => _loader.GetString(key);
}
