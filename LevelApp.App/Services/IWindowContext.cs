using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

/// <summary>
/// Provides access to the main window's UI context handles.
/// Injected into ViewModels that need to show ContentDialogs.
/// XamlRoot is null until the window's Loaded event fires; callers must guard.
/// </summary>
public interface IWindowContext
{
    XamlRoot? XamlRoot { get; }
    nint      Hwnd     { get; }

    /// <summary>Called once by MainWindow immediately after the HWND is available.</summary>
    void SetHwnd(nint hwnd);

    /// <summary>Called once by MainWindow from the Loaded handler when XamlRoot becomes available.</summary>
    void SetXamlRoot(XamlRoot xamlRoot);
}
