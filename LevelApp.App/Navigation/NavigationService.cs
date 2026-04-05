using LevelApp.App.Views;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Navigation;

/// <summary>
/// Frame-backed implementation of <see cref="INavigationService"/>.
/// Maps <see cref="PageKey"/> values to concrete Page types so no other
/// class needs to reference View types directly.
/// Call <see cref="Attach"/> once from <c>MainWindow</c>.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private static readonly Dictionary<PageKey, Type> PageMap = new()
    {
        [PageKey.ProjectSetup] = typeof(ProjectSetupView),
        [PageKey.Measurement]  = typeof(MeasurementView),
        [PageKey.Results]      = typeof(ResultsView),
        [PageKey.Correction]   = typeof(CorrectionView)
    };

    private Frame? _frame;

    /// <summary>Wires the service to the shell's root Frame. Must be called before navigating.</summary>
    public void Attach(Frame frame) => _frame = frame;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void NavigateTo(PageKey page, object? parameter = null)
        => _frame?.Navigate(PageMap[page], parameter);

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }
}
