using Windows.ApplicationModel.Resources.Core;

namespace LevelApp.App.Services;

public sealed class LocalisationService : ILocalisationService
{
    // Use ResourceManager.Current — the singleton that WinUI 3 initialises for x:Uid.
    // This is the only resource-loading mechanism that works for unpackaged apps.
    // GetSubtree can throw FileNotFoundException in unpackaged apps, so guard it.
    private readonly ResourceMap? _map;

    /// <summary>
    /// English text of every key looked up from code. Used when the resource lookup fails, so the
    /// UI never shows a raw key such as <c>Measurement_ProgressTitle.Text</c>.
    /// </summary>
    private static readonly Dictionary<string, string> Fallback = new()
    {
        ["Measurement_ProgressTitle.Text"] = "Step {0} of {1}",
        ["Correction_ProgressTitle.Text"]  = "Flagged step {0} of {1}",
        ["Instrument_Measuring.Text"]      = "Measuring – keep the table still…",
        ["Instrument_NoReading.Text"]      = "No reading yet",
        ["Instrument_Unsteady.Text"]       = "Unsteady – wait for the reading to settle",
        ["Instrument_ZeroedAt.Text"]       = "Zeroed at {0} µm/m",
    };

    public LocalisationService()
    {
        try { _map = ResourceManager.Current.MainResourceMap.GetSubtree("Resources"); }
        catch { _map = null; }
    }

    public string Get(string key)
    {
        // A resw name "Group_Item.Text" is stored in the PRI as "Group_Item/Text".
        foreach (var candidate in new[] { key, key.Replace('.', '/') })
        {
            try
            {
                string? value = _map?.GetValue(candidate)?.ValueAsString;
                if (!string.IsNullOrEmpty(value)) return value;
            }
            catch { /* try the next form */ }
        }

        return Fallback.TryGetValue(key, out var text) ? text : key;
    }
}
