using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;

namespace LevelApp.App.Services;

/// <summary>
/// One entry in an "Instrument" drop-down: the manual-entry plugin, or one
/// registered device of a hardware plugin.
/// </summary>
/// <param name="PluginId">The plugin that measures.</param>
/// <param name="Device">The registered device, or null for the manual-entry plugin.</param>
/// <param name="Label">User-visible text.</param>
public sealed record InstrumentChoice(string PluginId, KnownDevice? Device, string Label)
{
    /// <summary>The device's technical identifier (found-under name, Bluetooth address); empty for manual entry.</summary>
    public string Technical => Device?.GetTechnicalId() ?? string.Empty;
}

/// <summary>
/// Builds the instrument choices offered when a measurement is started and
/// turns the stored <c>MeasurementSession.InstrumentId</c> back into a provider.
/// </summary>
public interface IInstrumentChoiceCatalog
{
    /// <summary>
    /// Manual entry first, then one entry per registered device of every hardware plugin.
    /// Re-evaluated on each call, so devices added on the Instruments tab show up immediately.
    /// </summary>
    IReadOnlyList<InstrumentChoice> GetChoices();

    /// <summary>
    /// Index into <paramref name="choices"/> to pre-select: the preferred registered
    /// device if there is one, otherwise the first registered device, otherwise manual entry.
    /// </summary>
    int GetDefaultIndex(IReadOnlyList<InstrumentChoice> choices);

    /// <summary>Remembers <paramref name="choice"/>'s device as the preferred one of its plugin.</summary>
    void Select(InstrumentChoice choice);

    /// <summary>
    /// Creates the provider for <paramref name="pluginId"/> (the value stored on a session).
    /// When a hardware plugin has no registered device the manual-entry provider is returned
    /// instead and <paramref name="warning"/> says why.
    /// </summary>
    IInstrumentProvider ResolveProvider(string pluginId, out string? warning);

    /// <summary>The user-visible name of a plugin (for measurements that did not record a device), or null.</summary>
    string? PluginDisplayName(string pluginId);

    /// <summary>
    /// Disconnects and forgets the providers this catalog handed out for <paramref name="pluginId"/>.
    /// Providers stay connected between measurements; this is for an explicit Disconnect.
    /// </summary>
    Task DisconnectProvidersAsync(string pluginId);
}
