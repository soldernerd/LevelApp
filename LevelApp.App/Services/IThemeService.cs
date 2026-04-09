using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

/// <summary>
/// Applies the application theme to the root visual element.
/// Registered as a singleton; <see cref="SetTarget"/> must be called by
/// <see cref="LevelApp.App.MainWindow"/> before any call to <see cref="Apply"/>.
/// </summary>
public interface IThemeService
{
    /// <summary>The theme currently applied to the root element.</summary>
    ElementTheme CurrentTheme { get; }

    /// <summary>Sets the root element whose <c>RequestedTheme</c> will be updated.</summary>
    void SetTarget(FrameworkElement root);

    /// <summary>Applies <paramref name="theme"/> to the root element immediately.</summary>
    void Apply(ElementTheme theme);
}
