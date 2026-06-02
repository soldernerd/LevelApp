using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

/// <summary>
/// Singleton implementation of <see cref="IWindowContext"/>.
/// Registered in DI before the window is created; MainWindow populates
/// the handles via <see cref="SetHwnd"/> and <see cref="SetXamlRoot"/>
/// as they become available (Hwnd after GetWindowHandle, XamlRoot in the Loaded handler).
/// </summary>
internal sealed class WindowContext : IWindowContext
{
    public XamlRoot? XamlRoot { get; private set; }
    public nint      Hwnd     { get; private set; }

    public void SetHwnd(nint hwnd)         => Hwnd     = hwnd;
    public void SetXamlRoot(XamlRoot root) => XamlRoot = root;
}
