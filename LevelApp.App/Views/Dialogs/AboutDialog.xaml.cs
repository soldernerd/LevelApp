using LevelApp.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog()
    {
        InitializeComponent();

        var loc = App.Services.GetRequiredService<ILocalisationService>();
        Title           = loc.Get("About_Title.Text");
        CloseButtonText = loc.Get("About_CloseButton.Content");

        VersionText.Text = LevelApp.Core.AppVersion.Display;
    }
}
