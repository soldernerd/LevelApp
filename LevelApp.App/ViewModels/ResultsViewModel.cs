using CommunityToolkit.Mvvm.Input;
using LevelApp.App.DisplayModules.SurfacePlot3D;
using LevelApp.App.Navigation;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml;

namespace LevelApp.App.ViewModels;

public sealed partial class ResultsViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly MainViewModel      _mainViewModel;

    private Project            _project = null!;
    private MeasurementSession _session = null!;

    public ResultsViewModel(INavigationService navigation, MainViewModel mainViewModel)
    {
        _navigation    = navigation;
        _mainViewModel = mainViewModel;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    public void Initialize(ResultsArgs args)
    {
        _project = args.Project;
        _session = args.Session;

        // Use the latest correction round result if available, else the initial round result
        SurfaceResult result =
            _session.Corrections.Count > 0 && _session.Corrections.Last().Result is { } cr
                ? cr
                : _session.InitialRound.Result
                  ?? throw new InvalidOperationException("Session has no result to display.");

        ProjectName      = _project.Name;
        FlatnessText     = $"{result.FlatnessValueMm * 1000.0:F1} \u00b5m";
        SigmaText        = $"\u03c3 = {result.Sigma * 1000.0:F2} \u00b5m";
        FlaggedCountText = result.FlaggedStepIndices.Count == 0
            ? "No flagged steps"
            : $"{result.FlaggedStepIndices.Count} flagged step(s)";

        HasFlaggedSteps         = result.FlaggedStepIndices.Count > 0;
        CorrectButtonVisibility = HasFlaggedSteps ? Visibility.Visible : Visibility.Collapsed;

        // Build per-flagged-step display items
        var steps = _session.InitialRound.Steps;
        FlaggedSteps = result.FlaggedStepIndices
            .Select(stepIdx =>
            {
                var step    = steps.FirstOrDefault(s => s.Index == stepIdx);
                int listPos = step is not null ? steps.IndexOf(step) : -1;
                double res  = listPos >= 0 ? result.Residuals[listPos] : 0.0;
                return new FlaggedStepItem
                {
                    StepIndex   = stepIdx,
                    GridCol     = step?.GridCol ?? 0,
                    GridRow     = step?.GridRow ?? 0,
                    Orientation = step?.Orientation.ToString() ?? string.Empty,
                    Residual    = res
                };
            })
            .ToList();

        FlaggedListVisibility = FlaggedSteps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        PlotContent = new SurfacePlot3DDisplay().Render(result);

        ActiveResult     = result;
        ActiveSteps      = _session.InitialRound.Steps;
        ActiveDefinition = _project.ObjectDefinition;

        OnPropertyChanged(string.Empty);
    }

    // ── Display properties ────────────────────────────────────────────────────

    public string ProjectName      { get; private set; } = string.Empty;
    public string FlatnessText     { get; private set; } = string.Empty;
    public string SigmaText        { get; private set; } = string.Empty;
    public string FlaggedCountText { get; private set; } = string.Empty;

    public bool       HasFlaggedSteps         { get; private set; }
    public Visibility CorrectButtonVisibility { get; private set; } = Visibility.Collapsed;

    public List<FlaggedStepItem> FlaggedSteps         { get; private set; } = [];
    public Visibility            FlaggedListVisibility { get; private set; } = Visibility.Collapsed;

    public object? PlotContent { get; private set; }

    // ── Data exposed to the Measurements canvas renderer ─────────────────────

    public SurfaceResult?                 ActiveResult     { get; private set; }
    public IReadOnlyList<MeasurementStep> ActiveSteps      { get; private set; } = [];
    public ObjectDefinition?              ActiveDefinition { get; private set; }

    // ── Exposed for NewMeasurementDialog (used from ResultsView code-behind) ──

    public ObjectDefinition? ActiveObjectDefinition => _project?.ObjectDefinition;
    public string            ActiveOperator         => _project?.Operator ?? string.Empty;

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void StartCorrectionSession()
        => _navigation.NavigateTo(PageKey.Correction, new CorrectionArgs(_project, _session));

    // ── New measurement (called from ResultsView code-behind after dialog) ────

    /// <summary>
    /// Creates a new <see cref="MeasurementSession"/> on the existing project
    /// and navigates to <see cref="MeasurementView"/>.
    /// Called by <c>ResultsView.OnNewMeasurementClicked</c> after the dialog confirms.
    /// </summary>
    public void StartNewMeasurement(string operatorName, string notes, string strategyId)
    {
        var steps = new FullGridStrategy().GenerateSteps(_project.ObjectDefinition).ToList();

        var session = new MeasurementSession
        {
            Label        = $"Measurement {_project.Measurements.Count + 1}",
            TakenAt      = DateTime.UtcNow,
            Operator     = operatorName,
            Notes        = notes,
            InstrumentId = "manual-entry",
            StrategyId   = strategyId,
            InitialRound = new MeasurementRound { Steps = steps }
        };

        _project.Measurements.Add(session);
        _mainViewModel.MarkDirty();
        _navigation.NavigateTo(PageKey.Measurement, new MeasurementArgs(_project, session));
    }
}
