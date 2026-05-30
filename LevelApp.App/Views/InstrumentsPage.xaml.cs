using LevelApp.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LevelApp.App.Views;

public sealed partial class InstrumentsPage : Page
{
    public InstrumentsViewModel ViewModel { get; }

    public InstrumentsPage()
    {
        ViewModel = App.Services.GetRequiredService<InstrumentsViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        PluginTabs.TabItems.Clear();

        foreach (var pluginVm in ViewModel.Plugins)
        {
            var tabContent = new InstrumentPluginTabView(pluginVm) { XamlRoot = this.XamlRoot };

            var tab = new TabViewItem
            {
                Header     = pluginVm.DisplayName,
                Content    = tabContent,
                IsClosable = false
            };

            PluginTabs.TabItems.Add(tab);
        }

        if (PluginTabs.TabItems.Count > 0)
            PluginTabs.SelectedIndex = 0;
    }
}
