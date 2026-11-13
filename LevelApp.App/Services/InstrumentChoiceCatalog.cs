using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Instruments.Manual;

namespace LevelApp.App.Services;

/// <inheritdoc cref="IInstrumentChoiceCatalog"/>
public sealed class InstrumentChoiceCatalog : IInstrumentChoiceCatalog
{
    private readonly IReadOnlyList<IInstrumentPlugin> _plugins;
    private readonly IDeviceRegistry                  _registry;

    // One provider per device, kept alive: the instrument stays connected from one measurement to
    // the next instead of being closed and reopened (which takes seconds, especially over Bluetooth).
    private readonly Dictionary<string, (string PluginId, IInstrumentProvider Provider)> _providers = [];
    private readonly object _gate = new();

    public InstrumentChoiceCatalog(IEnumerable<IInstrumentPlugin> plugins, IDeviceRegistry registry)
    {
        _plugins  = plugins.ToList();
        _registry = registry;
    }

    /// <summary>A plugin whose only transport is "manual" needs no device.</summary>
    private static bool IsManual(IInstrumentPlugin plugin) =>
        plugin.SupportedTransports.All(t => t.TransportId == "manual");

    public IReadOnlyList<InstrumentChoice> GetChoices()
    {
        var choices = new List<InstrumentChoice>();

        foreach (var plugin in _plugins.Where(IsManual))
            choices.Add(new InstrumentChoice(plugin.PluginId, null, plugin.DisplayName));

        foreach (var plugin in _plugins.Where(p => !IsManual(p)))
        {
            foreach (var device in _registry.GetKnownDevices(plugin.PluginId))
            {
                choices.Add(new InstrumentChoice(
                    plugin.PluginId,
                    device,
                    $"{plugin.DisplayName} — {device.DisplayName} ({device.TransportId.ToUpperInvariant()})"));
            }
        }

        return choices;
    }

    public int GetDefaultIndex(IReadOnlyList<InstrumentChoice> choices)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            var device = choices[i].Device;
            if (device is not null &&
                _registry.GetPreferredDevice(device.PluginId)?.DeviceId == device.DeviceId)
                return i;
        }

        for (int i = 0; i < choices.Count; i++)
            if (choices[i].Device is not null) return i;

        return 0;
    }

    public void Select(InstrumentChoice choice)
    {
        if (choice.Device is not null)
            _registry.SetPreferredDevice(choice.PluginId, choice.Device.DeviceId);
    }

    public IInstrumentProvider ResolveProvider(string pluginId, out string? warning)
    {
        warning = null;

        var plugin = _plugins.FirstOrDefault(p => p.PluginId == pluginId);
        if (plugin is not null)
        {
            if (IsManual(plugin))
                return ProviderFor(plugin, ManualEntryProvider.BuiltInDevice);

            var device = _registry.GetPreferredDevice(plugin.PluginId)
                      ?? _registry.GetKnownDevices(plugin.PluginId).FirstOrDefault();
            if (device is not null)
                return ProviderFor(plugin, device);

            warning = $"No {plugin.DisplayName} instrument is registered — add one on the " +
                      "Instruments tab. Readings are typed in by hand until then.";
        }
        else
        {
            warning = $"The instrument plugin “{pluginId}” is not available. " +
                      "Readings are typed in by hand.";
        }

        return ProviderFor(_plugins.First(IsManual), ManualEntryProvider.BuiltInDevice);
    }

    private IInstrumentProvider ProviderFor(IInstrumentPlugin plugin, KnownDevice device)
    {
        lock (_gate)
        {
            if (!_providers.TryGetValue(device.DeviceId, out var entry))
            {
                entry = (plugin.PluginId, plugin.CreateProvider(device));
                _providers[device.DeviceId] = entry;
            }
            return entry.Provider;
        }
    }

    public string? PluginDisplayName(string pluginId) =>
        _plugins.FirstOrDefault(p => p.PluginId == pluginId)?.DisplayName;

    public async Task DisconnectProvidersAsync(string pluginId)
    {
        List<IInstrumentProvider> doomed;
        lock (_gate)
        {
            var keys = _providers.Where(kv => kv.Value.PluginId == pluginId).Select(kv => kv.Key).ToList();
            doomed = keys.Select(k => _providers[k].Provider).ToList();
            foreach (var k in keys) _providers.Remove(k);
        }

        foreach (var provider in doomed)
        {
            try { await provider.DisconnectAsync(); }
            catch { /* going away anyway */ }
        }
    }
}
