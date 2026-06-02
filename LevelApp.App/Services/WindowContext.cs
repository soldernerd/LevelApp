using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

/// <summary>
/// Singleton implementation of <see cref="IWindowContext"/>.
/// Registered in DI before the window is created; MainWindow sets the
/// properties as they become available (Hwnd after GetWindowHandle,
/// XamlRoot in the Loaded handler).
/// </summary>
internal sealed class WindowContext : IWindowContext
{
    public XamlRoot? XamlRoot { get; set; }
    public nint      Hwnd     { get; set; }
}
