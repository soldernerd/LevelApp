using Microsoft.UI.Xaml;

namespace LevelApp.App.Services;

public sealed class ThemeService : IThemeService
{
    private FrameworkElement? _root;

    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    public void SetTarget(FrameworkElement root) => _root = root;

    public void Apply(ElementTheme theme)
    {
        CurrentTheme = theme;
        if (_root is not null)
            _root.RequestedTheme = theme;
    }
}
