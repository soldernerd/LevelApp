using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Navigation;
using LevelApp.Core.Geometry.SurfacePlate;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml;

namespace LevelApp.App.ViewModels;

public sealed partial class CorrectionViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    private Project            _project    = null!;
    private MeasurementSession _session    = null!;
    private ObjectDefinition   _definition = null!;

    /// <summary>Subset of InitialRound.Steps whose Index appears in the latest flagged list.</summary>
    private List<MeasurementStep> _flaggedSteps = [];

    /// <summary>New readings collected during this correction session (parallel to _flaggedSteps).</summary>
    private double[] _newReadings = [];

    public CorrectionViewModel(INavigationService navigation)
    {
        _navigation = navigation;
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

        var allSteps = _session.InitialRound.Steps;
        _flaggedSteps = effectiveResult.FlaggedStepIndices
            .Select(idx => allSteps.First(s => s.Index == idx))
            .OrderBy(s => s.Index)
            .ToList();

        _newReadings = new double[_flaggedSteps.Count];

        GridColumns        = Convert.ToInt32(_definition.Parameters["columnsCount"]);
        GridRows           = Convert.ToInt32(_definition.Parameters["rowsCount"]);
        CurrentStepIndex   = 0;
        Reading            = double.NaN;
        IsCalculating      = false;

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
        _flaggedSteps.Count > 0 && CurrentStepIndex < _flaggedSteps.Count
            ? _flaggedSteps[CurrentStepIndex] : null;

    public int    TotalFlaggedSteps => _flaggedSteps.Count;
    public string ProgressText      => TotalFlaggedSteps > 0
        ? $"Correction step {CurrentStepIndex + 1} of {TotalFlaggedSteps}" : string.Empty;

    public string OrientationArrow => CurrentStep?.Orientation switch
    {
        Orientation.North => "↑",
        Orientation.South => "↓",
        Orientation.East  => "→",
        Orientation.West  => "←",
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
    public IReadOnlyList<MeasurementStep> FlaggedSteps => _flaggedSteps;

    // ── Command ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAcceptReading))]
    private async Task AcceptReadingAsync()
    {
        if (CurrentStep is null) return;

        _newReadings[CurrentStepIndex] = Reading;
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
                ReplacedSteps = _flaggedSteps
                    .Select((s, i) => new ReplacedStep
                    {
                        OriginalStepIndex = s.Index,
                        Reading           = _newReadings[i]
                    })
                    .ToList()
            };

            // Merge original readings with replacements
            var replacedMap = correctionRound.ReplacedSteps
                .ToDictionary(r => r.OriginalStepIndex, r => r.Reading);

            var mergedSteps = _session.InitialRound.Steps
                .Select(s => new MeasurementStep
                {
                    Index           = s.Index,
                    GridCol         = s.GridCol,
                    GridRow         = s.GridRow,
                    Orientation     = s.Orientation,
                    InstructionText = s.InstructionText,
                    Reading         = replacedMap.TryGetValue(s.Index, out double nr) ? nr : s.Reading
                })
                .ToList();

            var mergedRound = new MeasurementRound { Steps = mergedSteps };
            var definition  = _definition;

            var result = await Task.Run(() =>
                new SurfacePlateCalculator(definition).Calculate(mergedRound));

            correctionRound.Result = result;
            _session.Corrections.Add(correctionRound);
            _project.ModifiedAt = DateTime.UtcNow;

            IsCalculating = false;
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(_project, _session));
        }
    }

    private bool CanAcceptReading() => !IsCalculating && !double.IsNaN(Reading);
}
