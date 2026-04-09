using Microsoft.Windows.ApplicationModel.Resources;

namespace LevelApp.App.Services;

public sealed class LocalisationService : ILocalisationService
{
    private readonly ResourceMap _map;

    public LocalisationService()
    {
        _map = new ResourceManager().MainResourceMap.GetSubtree("Resources");
    }

    public string Get(string key) => _map.GetValue(key)?.ValueAsString ?? key;
}
