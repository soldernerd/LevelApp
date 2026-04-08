using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Navigation;
using LevelApp.Core.Geometry.SurfacePlate;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml;

namespace LevelApp.App.ViewModels;

public sealed partial class MeasurementViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly MainViewModel      _mainViewModel;

    private Project            _project    = null!;
    private MeasurementSession _session    = null!;
    private List<MeasurementStep> _steps   = [];
    private ObjectDefinition   _definition = null!;

    public MeasurementViewModel(INavigationService navigation, MainViewModel mainViewModel)
    {
        _navigation    = navigation;
        _mainViewModel = mainViewModel;
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
        _definition = _project.ObjectDefinition;

        GridColumns = _definition.Parameters.TryGetValue("columnsCount", out var c) ? Convert.ToInt32(c) : 0;
        GridRows    = _definition.Parameters.TryGetValue("rowsCount",    out var r) ? Convert.ToInt32(r) : 0;
        WidthMm     = _definition.Parameters.TryGetValue("widthMm",      out var w) ? Convert.ToDouble(w) : 0.0;
        HeightMm    = _definition.Parameters.TryGetValue("heightMm",     out var h) ? Convert.ToDouble(h) : 0.0;

        CurrentStepIndex = 0;
        Reading          = double.NaN;
        IsCalculating    = false;

        OnPropertyChanged(string.Empty); // refresh all computed properties at once
    }

    // ── Grid geometry (set during Initialise) ─────────────────────────────────

    public int    GridColumns { get; private set; }
    public int    GridRows    { get; private set; }
    public double WidthMm     { get; private set; }
    public double HeightMm    { get; private set; }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(OrientationArrow))]
    [NotifyPropertyChangedFor(nameof(CurrentInstructionText))]
    private int _currentStepIndex;

    /// <summary>
    /// The operator's reading in mm/m.  <see cref="double.NaN"/> means "not yet entered"
    /// — NumberBox renders NaN as an empty/placeholder field.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptReadingCommand))]
    private double _reading = double.NaN;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    [NotifyPropertyChangedFor(nameof(CalculatingVisibility))]
    [NotifyCanExecuteChangedFor(nameof(AcceptReadingCommand))]
    private bool _isCalculating;

    // ── Computed display properties ───────────────────────────────────────────

    public MeasurementStep? CurrentStep =>
        _steps.Count > 0 && CurrentStepIndex < _steps.Count
            ? _steps[CurrentStepIndex] : null;

    public int    TotalSteps    => _steps.Count;
    public string ProgressText  => TotalSteps > 0 ? $"Step {CurrentStepIndex + 1} of {TotalSteps}" : string.Empty;
    public int    ProgressPercent => TotalSteps > 0 ? CurrentStepIndex * 100 / TotalSteps : 0;

    public string OrientationArrow => CurrentStep?.Orientation switch
    {
        Orientation.North => "\u2191",
        Orientation.South => "\u2193",
        Orientation.East  => "\u2192",
        Orientation.West  => "\u2190",
        _ => "?"
    };

    public string CurrentInstructionText => CurrentStep?.InstructionText ?? string.Empty;

    public bool       IsInputEnabled       => !IsCalculating;
    public Visibility CalculatingVisibility => IsCalculating ? Visibility.Visible : Visibility.Collapsed;

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

        _steps[CurrentStepIndex].Reading = Reading;
        _mainViewModel.MarkDirty();
        Reading = double.NaN;

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

            var strategy = ResultsViewModel.CreateStrategy(_session.StrategyId);
            var result = await Task.Run(() =>
                new SurfacePlateCalculator(definition, strategy).Calculate(round));

            round.Result      = result;
            round.CompletedAt = DateTime.UtcNow;
            _project.ModifiedAt = DateTime.UtcNow;

            IsCalculating = false;
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(_project, _session));
        }
    }

    private bool CanAcceptReading() => !IsCalculating && !double.IsNaN(Reading);
}
