namespace LevelApp.App.Navigation;

/// <summary>
/// Abstracts Frame-based navigation so ViewModels can trigger page transitions
/// without taking a compile-time dependency on concrete View types.
/// </summary>
public interface INavigationService
{
    bool CanGoBack { get; }
    void NavigateTo(PageKey page, object? parameter = null);
    void GoBack();
}
