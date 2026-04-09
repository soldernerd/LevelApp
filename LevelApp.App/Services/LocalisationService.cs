using Windows.ApplicationModel.Resources.Core;

namespace LevelApp.App.Services;

public sealed class LocalisationService : ILocalisationService
{
    // Use ResourceManager.Current — the singleton that WinUI 3 initialises for x:Uid.
    // This is the only resource-loading mechanism that works for unpackaged apps.
    // GetSubtree can throw FileNotFoundException in unpackaged apps, so guard it.
    private readonly ResourceMap? _map;

    public LocalisationService()
    {
        try { _map = ResourceManager.Current.MainResourceMap.GetSubtree("Resources"); }
        catch { _map = null; }
    }

    public string Get(string key)
    {
        try   { return _map?.GetValue(key)?.ValueAsString ?? key; }
        catch { return key; }
    }
}
