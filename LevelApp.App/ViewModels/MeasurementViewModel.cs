using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Navigation;
using LevelApp.App.Services;
using LevelApp.Core.Geometry;
using LevelApp.Core.Geometry.ParallelWays;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using LevelApp.Instruments.Manual;
using Microsoft.UI.Xaml;
using ContentDialog = Microsoft.UI.Xaml.Controls.ContentDialog;
using ContentDialogButton = Microsoft.UI.Xaml.Controls.ContentDialogButton;
using ContentDialogResult = Microsoft.UI.Xaml.Controls.ContentDialogResult;
using InfoBarSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity;

namespace LevelApp.App.ViewModels;

public sealed partial class MeasurementViewModel : ViewModelBase
{
    private readonly INavigationService              _navigation;
    private readonly MainViewModel                   _mainViewModel;
    private readonly ParallelWaysCalculator          _pwCalculator;
    private readonly ILocalisationService            _loc;
    private readonly IInstrumentChoiceCatalog        _catalog;

    // Set in Initialize() once the session's instrumentId is known.
    private IInstrumentProvider? _provider;

    private Project               _project    = null!;
    private MeasurementSession    _session    = null!;
    private List<MeasurementStep> _steps      = [];
    private ObjectDefinition      _definition = null!;
    private readonly IWindowContext _windowContext;

    public MeasurementViewModel(INavigationService navigation, MainViewModel mainViewModel,
                                ParallelWaysCalculator pwCalculator, ILocalisationService loc,
                                IInstrumentChoiceCatalog catalog,
                                InstrumentReadingViewModel instrumentReading,
                                IWindowContext windowContext)
    {
        _windowContext = windowContext;
        InstrumentReading = instrumentReading;
        InstrumentReading.ReadingTaken      += OnInstrumentReadingTaken;
        InstrumentReading.ReadingAccepted   += OnInstrumentReadingAccepted;
        InstrumentReading.AutoAcceptEnabled  = true;
        InstrumentReading.ConnectionChanged += OnConnectionStateChanged;
        _navigation    = navigation;
        _mainViewModel = mainViewModel;
        _pwCalculator  = pwCalculator;
        _loc           = loc;
        _catalog       = catalog;
    }

    /// <summary>The instrument half of the reading entry (live value, channel, take reading, zero).</summary>
    public InstrumentReadingViewModel InstrumentReading { get; }

    // A settled measurement from the instrument lands in the reading box (µm/m).
    private void OnInstrumentReadingTaken(object? sender, double microns) =>
        Reading = Math.Round(microns, 2);

    // "Take reading and accept": the settled value is entered and accepted in one go.
    private void OnInstrumentReadingAccepted(object? sender, double microns)
    {
        Reading = Math.Round(microns, 2);
        if (AcceptReadingCommand.CanExecute(null))
            _ = AcceptReadingCommand.ExecuteAsync(null);
    }

    /// <summary>Lets go of the instrument when the page is left; it stays connected.</summary>
    public void Shutdown() => _ = InstrumentReading.DetachAsync(disconnect: false);

    private void OnConnectionStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ShowRetry));
        OnPropertyChanged(nameof(ShowConnectionWarning));
        OnPropertyChanged(nameof(ConnectionSeverity));
        OnPropertyChanged(nameof(ConnectionStatusMessage));
        AcceptReadingCommand.NotifyCanExecuteChanged();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>
    /// Called from <c>MeasurementView.OnNavigatedTo</c> once the navigation
    /// parameter is available.
    /// </summary>
    public void Initialize(MeasurementArgs args)
    {
        _project    = args.Project;
        _session    = args.Session;
        _steps      = _session.InitialRound.Steps;
        _definition = _project.DefinitionFor(_session);

        // Resolve the provider for the session's instrument. A hardware instrument that is
        // not registered falls back to manual entry with a visible warning.
        _provider        = _catalog.ResolveProvider(_session.InstrumentId, out var fallbackWarning);
        _fallbackWarning = fallbackWarning;

        // The reading panel owns the provider's connection from here on (connects
        // it, follows its state, releases it in Shutdown).
        _ = InstrumentReading.AttachAsync(_provider);

        IsParallelWays = _definition.GeometryModuleId == "ParallelWays";

        if (IsParallelWays)
        {
            GridColumns = 0;
            GridRows    = 0;
            WidthMm     = 0.0;
            HeightMm    = 0.0;
        }
        else
        {
            GridColumns = _definition.Parameters.TryGetValue("columnsCount", out var c) ? Convert.ToInt32(c) : 0;
            GridRows    = _definition.Parameters.TryGetValue("rowsCount",    out var r) ? Convert.ToInt32(r) : 0;
            WidthMm     = _definition.Parameters.TryGetValue("widthMm",      out var w) ? Convert.ToDouble(w) : 0.0;
            HeightMm    = _definition.Parameters.TryGetValue("heightMm",     out var h) ? Convert.ToDouble(h) : 0.0;
        }

        // Resume where the measurement stopped: at the first step without a reading. A session whose
        // readings are all in (the calculation never ran) opens on its last step, reading filled in.
        int firstOpen = _steps.FindIndex(s => !s.Reading.HasValue);
        CurrentStepIndex = firstOpen >= 0 ? firstOpen : Math.Max(0, _steps.Count - 1);
        Reading = firstOpen < 0 && _steps.Count > 0 && _steps[^1].Reading is { } last
            ? Math.Round(last * 1000.0, 2)
            : double.NaN;
        IsCalculating    = false;

        OnPropertyChanged(string.Empty); // refresh all computed properties at once
    }

    // ── Grid geometry (set during Initialise) ─────────────────────────────────

    public bool             IsParallelWays { get; private set; }
    public int              GridColumns    { get; private set; }
    public int              GridRows       { get; private set; }
    public double           WidthMm        { get; private set; }
    public double           HeightMm       { get; private set; }
    public ObjectDefinition Definition     => _definition;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(OrientationArrow))]
    [NotifyPropertyChangedFor(nameof(CurrentInstructionText))]
    [NotifyPropertyChangedFor(nameof(HasLastReading))]
    [NotifyPropertyChangedFor(nameof(LastReadingText))]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private int _currentStepIndex;

    /// <summary>
    /// The operator's reading in µm/m (as entered by the user).
    /// <see cref="double.NaN"/> means "not yet entered"
    /// — NumberBox renders NaN as an empty/placeholder field.
    /// Converted to mm/m (÷ 1000) before being stored on the step.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptReadingCommand))]
    private double _reading = double.NaN;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    [NotifyPropertyChangedFor(nameof(CalculatingVisibility))]
    [NotifyCanExecuteChangedFor(nameof(AcceptReadingCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _isCalculating;

    // ── Computed display properties ───────────────────────────────────────────

    public MeasurementStep? CurrentStep =>
        _steps.Count > 0 && CurrentStepIndex < _steps.Count
            ? _steps[CurrentStepIndex] : null;

    public int    TotalSteps    => _steps.Count;
    public string ProgressText  => TotalSteps > 0
        ? string.Format(_loc.Get("Measurement_ProgressTitle.Text"), CurrentStepIndex + 1, TotalSteps)
        : string.Empty;
    public int    ProgressPercent => TotalSteps > 0 ? CurrentStepIndex * 100 / TotalSteps : 0;

    public string OrientationArrow => CurrentStep?.Orientation switch
    {
        Orientation.North => "↑",
        Orientation.South => "↓",
        Orientation.East  => "→",
        Orientation.West  => "←",
        _ => "?"
    };

    /// <summary>The previous step has a reading to show (and to go back to).</summary>
    public bool HasLastReading =>
        CurrentStepIndex > 0 && CurrentStepIndex <= _steps.Count && _steps[CurrentStepIndex - 1].Reading.HasValue;

    /// <summary>The reading accepted for the previous step, in µm/m.</summary>
    public string LastReadingText => HasLastReading
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "Step {0}: {1:+0.0;-0.0;0.0} \u00b5m/m", CurrentStepIndex, _steps[CurrentStepIndex - 1].Reading!.Value * 1000.0)
        : string.Empty;

    public string CurrentInstructionText => CurrentStep?.InstructionText ?? string.Empty;

    public bool       IsInputEnabled       => !IsCalculating;
    public Visibility CalculatingVisibility => IsCalculating ? Visibility.Visible : Visibility.Collapsed;

    // ── Connection status ─────────────────────────────────────────────────────

    private InstrumentConnectionState ProviderState =>
        _provider?.ConnectionState ?? InstrumentConnectionState.Disconnected;

    /// <summary>A failed or lost connection to an instrument that reports readings can be retried.</summary>
    public bool ShowRetry =>
        InstrumentReading.IsAvailable && ProviderState != InstrumentConnectionState.Connected
                                      && ProviderState != InstrumentConnectionState.Connecting;

    /// <summary>Set when the session's instrument could not be used and manual entry took over.</summary>
    private string? _fallbackWarning;

    public bool ShowConnectionWarning =>
        _fallbackWarning is not null || ProviderState != InstrumentConnectionState.Connected;

    public InfoBarSeverity ConnectionSeverity =>
        _fallbackWarning is null && ProviderState == InstrumentConnectionState.Error
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Warning;

    public string ConnectionStatusMessage =>
        _fallbackWarning ??
        ProviderState switch
        {
            InstrumentConnectionState.Disconnected => "Instrument disconnected",
            InstrumentConnectionState.Connecting   => "Connecting to instrument…",
            InstrumentConnectionState.Degraded     => "Instrument signal degraded",
            InstrumentConnectionState.Error        => InstrumentReading.ConnectionError ?? "Instrument connection error",
            _                                      => string.Empty
        };

    /// <summary>Read-only view of the step list for the canvas renderer.</summary>
    public IReadOnlyList<MeasurementStep> Steps => _steps;

    // ── Command ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores the current reading, advances to the next step, or — when all
    /// steps are done — runs the calculator and navigates to Results.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAcceptReading))]
    private async Task AcceptReadingAsync()
    {
        if (CurrentStep is null) return;

        // Reading is entered by the user in µm/m; convert to mm/m for storage
        _steps[CurrentStepIndex].Reading = Reading / 1000.0;
        _mainViewModel.MarkDirty();
        Reading = double.NaN;

        // Every accepted reading goes to disk at once, so an interruption loses nothing.
        _ = _mainViewModel.AutoSaveAsync();

        if (CurrentStepIndex < TotalSteps - 1)
        {
            CurrentStepIndex++;
        }
        else
        {
            // All readings collected — calculate on a background thread
            IsCalculating = true;

            var definition = _definition;
            var round      = _session.InitialRound;

            if (IsParallelWays)
            {
                var pwParams = new CalculationParameters();
                var pwResult = await Task.Run(
                    () => _pwCalculator.Calculate(round.Steps, definition, pwParams));
                round.ParallelWaysResult = pwResult;
            }
            else
            {
                var parameters = round.CalculationParameters ?? new CalculationParameters();
                var strategy   = StrategyFactory.Create(_session.StrategyId);
                var calculator = CalculatorFactory.Create(parameters.MethodId, strategy);

                var result = await Task.Run(() => calculator.Calculate(round.Steps, definition, parameters));
                round.Result = result;
            }

            round.CompletedAt   = DateTime.UtcNow;
            _project.ModifiedAt = DateTime.UtcNow;
            await _mainViewModel.AutoSaveAsync();

            IsCalculating = false;
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(_project, _session));
        }
    }

    /// <summary>
    /// Leaves this measurement: discard it, keep it unfinished to resume later (its readings are
    /// already on disk), or carry on. Works at any step.
    /// </summary>
    [RelayCommand]
    private async Task CancelMeasurementAsync()
    {
        if (IsCalculating || _windowContext.XamlRoot is null) return;

        int taken = _session.ReadingCount();
        var dialog = new ContentDialog
        {
            Title = "Cancel this measurement?",
            Content = taken == 0
                ? "No readings have been taken yet."
                : $"{taken} of {TotalSteps} readings have been taken. They are kept unless you discard the measurement.",
            PrimaryButtonText   = "Discard it",
            SecondaryButtonText = "Keep it to resume later",
            CloseButtonText     = "Continue measuring",
            DefaultButton       = ContentDialogButton.Close,
            XamlRoot            = _windowContext.XamlRoot
        };

        switch (await dialog.ShowAsync())
        {
            case ContentDialogResult.Primary:
                _project.Measurements.Remove(_session);
                _mainViewModel.MarkDirty();
                await _mainViewModel.AutoSaveAsync();
                LeaveToProject();
                break;
            case ContentDialogResult.Secondary:
                await _mainViewModel.AutoSaveAsync();
                LeaveToProject();
                break;
        }
    }

    /// <summary>Back to the project: the results of its last finished measurement, else the setup page.</summary>
    private void LeaveToProject()
    {
        var last = _project.Measurements.LastOrDefault(m => !m.IsUnfinished());
        if (last is not null)
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(_project, last));
        else
            _navigation.NavigateTo(PageKey.ProjectSetup, _project);
    }

    /// <summary>
    /// Returns to the previous step so its reading can be taken again. The old value is put in
    /// the reading box; it stays stored until a new one is accepted.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        CurrentStepIndex--;
        var previous = _steps[CurrentStepIndex].Reading;
        Reading = previous.HasValue ? Math.Round(previous.Value * 1000.0, 2) : double.NaN;
    }

    private bool CanGoBack() => CurrentStepIndex > 0 && !IsCalculating;

    private bool CanAcceptReading()
    {
        var state = ProviderState;
        bool connectionOk = state != InstrumentConnectionState.Disconnected
                         && state != InstrumentConnectionState.Error;
        return !IsCalculating && !double.IsNaN(Reading) && connectionOk;
    }
}
