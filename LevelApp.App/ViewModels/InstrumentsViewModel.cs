using CommunityToolkit.Mvvm.ComponentModel;
using LevelApp.Core.Interfaces;
using System.Collections.ObjectModel;

namespace LevelApp.App.ViewModels;

/// <summary>
/// ViewModel for the Instruments page. Exposes one
/// <see cref="InstrumentPluginTabViewModel"/> per registered plugin.
/// </summary>
public sealed class InstrumentsViewModel : ObservableObject
{
    public ObservableCollection<InstrumentPluginTabViewModel> Plugins { get; }

    public InstrumentsViewModel(
        IEnumerable<IInstrumentPlugin> plugins,
        IDeviceRegistry                registry)
    {
        Plugins = new ObservableCollection<InstrumentPluginTabViewModel>(
            plugins.Select(p => new InstrumentPluginTabViewModel(p, registry)));
    }
}
