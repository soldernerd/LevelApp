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
}
