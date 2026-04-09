using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Views.Dialogs;

public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog()
    {
        InitializeComponent();

        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        Title           = loader.GetString("About_Title.Text");
        CloseButtonText = loader.GetString("About_CloseButton.Content");

        VersionText.Text = LevelApp.Core.AppVersion.Display;
    }
}
