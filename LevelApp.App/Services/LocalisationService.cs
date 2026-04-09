using Windows.ApplicationModel.Resources.Core;

namespace LevelApp.App.Services;

public sealed class LocalisationService : ILocalisationService
{
    // Use ResourceManager.Current — the singleton that WinUI 3 initialises for x:Uid.
    // This is the only resource-loading mechanism that works for unpackaged apps.
    private readonly ResourceMap _map =
        ResourceManager.Current.MainResourceMap.GetSubtree("Resources");

    public string Get(string key)
    {
        try   { return _map.GetValue(key)?.ValueAsString ?? key; }
        catch { return key; }
    }
}
