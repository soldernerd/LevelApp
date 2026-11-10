using LevelApp.App.Views;
using Microsoft.UI.Xaml.Controls;

namespace LevelApp.App.Navigation;

/// <summary>
/// Frame-backed implementation of <see cref="INavigationService"/>.
/// Maps <see cref="PageKey"/> values to concrete Page types so no other
/// class needs to reference View types directly.
///
/// The shell has two top-level tabs, each with its own Frame so that switching
/// tabs never discards the other tab's page: the <b>Project</b> tab hosts the
/// project flow (setup → measurement → results → correction) and the
/// <b>Instruments</b> tab hosts the instruments page.
/// Call <see cref="Attach"/> once from <c>MainWindow</c>.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private static readonly Dictionary<PageKey, Type> PageMap = new()
    {
        [PageKey.ProjectSetup] = typeof(ProjectSetupView),
        [PageKey.Measurement]  = typeof(MeasurementView),
        [PageKey.Results]      = typeof(ResultsView),
        [PageKey.Correction]   = typeof(CorrectionView),
        [PageKey.Instruments]  = typeof(InstrumentsPage)
    };

    private Frame? _projectFrame;
    private Frame? _instrumentsFrame;
    private Action<bool>? _selectTab;

    /// <summary>
    /// Wires the service to the shell. <paramref name="selectInstrumentsTab"/> is invoked
    /// with <c>true</c> to bring the Instruments tab to the front and <c>false</c> for the
    /// Project tab.
    /// </summary>
    public void Attach(Frame projectFrame, Frame instrumentsFrame, Action<bool> selectInstrumentsTab)
    {
        _projectFrame     = projectFrame;
        _instrumentsFrame = instrumentsFrame;
        _selectTab        = selectInstrumentsTab;
    }

    /// <summary>
    /// Loads the Instruments page into its frame the first time the tab is shown.
    /// Later visits keep the page (and its connections) alive.
    /// </summary>
    public void EnsureInstrumentsPage()
    {
        if (_instrumentsFrame is { Content: null })
            _instrumentsFrame.Navigate(PageMap[PageKey.Instruments]);
    }

    public bool CanGoBack => _projectFrame?.CanGoBack ?? false;

    public void NavigateTo(PageKey page, object? parameter = null)
    {
        if (page == PageKey.Instruments)
        {
            EnsureInstrumentsPage();
            _selectTab?.Invoke(true);
            return;
        }

        _projectFrame?.Navigate(PageMap[page], parameter);
        _selectTab?.Invoke(false);
    }

    public void GoBack()
    {
        if (_projectFrame?.CanGoBack == true)
            _projectFrame.GoBack();
    }
}
