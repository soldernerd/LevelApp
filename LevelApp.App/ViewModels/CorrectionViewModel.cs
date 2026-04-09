using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Navigation;
using LevelApp.Core.Geometry;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml;

namespace LevelApp.App.ViewModels;

public sealed partial class CorrectionViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly MainViewModel      _mainViewModel;

    private Project            _project    = null!;
    private MeasurementSession _session    = null!;
    private ObjectDefinition   _definition = null!;

    /// <summary>Flagged steps paired with the new reading collected during this session.</summary>
    private List<(MeasurementStep Step, double? NewReading)> _flagged = [];

    public CorrectionViewModel(INavigationService navigation, MainViewModel mainViewModel)
    {
        _navigation    = navigation;
        _mainViewModel = mainViewModel;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    public void Initialize(CorrectionArgs args)
    {
        _project    = args.Project;
        _session    = args.Session;
        _definition = _project.ObjectDefinition;

        // Determine the effective (latest) result
        SurfaceResult effectiveResult = _session.Corrections.Count > 0 &&
            _session.Corrections.Last().Result is { } cr ? cr : _session.InitialRound.Result!;

        var allSteps   = _session.InitialRound.Steps;
        var stepLookup = allSteps.ToDictionary(s => s.Index);
        _flagged = effectiveResult.FlaggedStepIndices
            .Where(idx => stepLookup.ContainsKey(idx))
            .Select(idx => (Step: stepLookup[idx], NewReading: (double?)null))
            .OrderBy(f => f.Step.Index)
            .ToList();

        GridColumns      = _definition.Parameters.TryGetValue("columnsCount", out var c) ? Convert.ToInt32(c) : 0;
        GridRows         = _definition.Parameters.TryGetValue("rowsCount",    out var r) ? Convert.ToInt32(r) : 0;
        CurrentStepIndex = 0;
        Reading          = double.NaN;
        IsCalculating    = false;

        OnPropertyChanged(string.Empty);
    }

    // ── Grid geometry ─────────────────────────────────────────────────────────

    public int GridColumns { get; private set; }
    public int GridRows    { get; private set; }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(OrientationArrow))]
    [NotifyPropertyChangedFor(nameof(CurrentInstructionText))]
    [NotifyPropertyChangedFor(nameof(OriginalReadingText))]
    private int _currentStepIndex;

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
        _flagged.Count > 0 && CurrentStepIndex < _flagged.Count
            ? _flagged[CurrentStepIndex].Step : null;

    public int    TotalFlaggedSteps => _flagged.Count;
    public string ProgressText      => TotalFlaggedSteps > 0
        ? $"Correction step {CurrentStepIndex + 1} of {TotalFlaggedSteps}" : string.Empty;

    public string OrientationArrow => CurrentStep?.Orientation switch
    {
        Orientation.North => "\u2191",
        Orientation.South => "\u2193",
        Orientation.East  => "\u2192",
        Orientation.West  => "\u2190",
        _ => "?"
    };

    public string CurrentInstructionText => CurrentStep?.InstructionText ?? string.Empty;

    public string OriginalReadingText => CurrentStep?.Reading is double orig
        ? $"Original reading: {orig:+0.000;-0.000} mm/m"
        : string.Empty;

    public bool       IsInputEnabled        => !IsCalculating;
    public Visibility CalculatingVisibility => IsCalculating ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>All steps (for canvas context colouring).</summary>
    public IReadOnlyList<MeasurementStep> AllSteps => _session?.InitialRound.Steps ?? [];

    /// <summary>The flagged steps being re-measured.</summary>
    public IReadOnlyList<MeasurementStep> FlaggedSteps => _flagged.Select(f => f.Step).ToList();

    // ── Command ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAcceptReading))]
    private async Task AcceptReadingAsync()
    {
        if (CurrentStep is null) return;

        _flagged[CurrentStepIndex] = (_flagged[CurrentStepIndex].Step, Reading);
        Reading = double.NaN;

        if (CurrentStepIndex < TotalFlaggedSteps - 1)
        {
            CurrentStepIndex++;
        }
        else
        {
            // All corrections collected — build round and recalculate
            IsCalculating = true;

            var correctionRound = new CorrectionRound
            {
                TriggeredAt   = DateTime.UtcNow,
                Operator      = _project.Operator,
                ReplacedSteps = _flagged
                    .Select(f => new ReplacedStep
                    {
                        OriginalStepIndex = f.Step.Index,
                        Reading           = f.NewReading!.Value
                    })
                    .ToList()
            };

            var allReplacements = _session.Corrections
                .SelectMany(c => c.ReplacedSteps)
                .Concat(correctionRound.ReplacedSteps);

            var mergedSteps = MeasurementRound.MergeWithReplacements(
                _session.InitialRound.Steps, allReplacements);

            var definition = _definition;
            var parameters = _session.InitialRound.CalculationParameters ?? new CalculationParameters();
            var strategy   = StrategyFactory.Create(_session.StrategyId);
            var calculator = CalculatorFactory.Create(parameters.MethodId, strategy);

            var result = await Task.Run(() => calculator.Calculate(mergedSteps, definition, parameters));

            correctionRound.Result = result;
            _session.Corrections.Add(correctionRound);
            _project.ModifiedAt = DateTime.UtcNow;
            _mainViewModel.MarkDirty();

            IsCalculating = false;
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(_project, _session));
        }
    }

    private bool CanAcceptReading() => !IsCalculating && !double.IsNaN(Reading);
}
