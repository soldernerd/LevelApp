using CommunityToolkit.Mvvm.Input;
using LevelApp.App.DisplayModules.SurfacePlot3D;
using LevelApp.App.Navigation;
using LevelApp.Core.Geometry.SurfacePlate;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml;

namespace LevelApp.App.ViewModels;

public sealed partial class ResultsViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly MainViewModel      _mainViewModel;

    private Project            _project = null!;
    private MeasurementSession _session = null!;
    private SurfaceResult?     _currentResult;

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

        SurfaceResult result =
            _session.Corrections.Count > 0 && _session.Corrections.Last().Result is { } cr
                ? cr
                : _session.InitialRound.Result
                  ?? throw new InvalidOperationException("Session has no result to display.");

        var strategy   = CreateStrategy(_session.StrategyId);
        var definition = _project.ObjectDefinition;

        UpdateDisplay(result, GetCurrentCalculationParameters(result));
        PlotContent = new SurfacePlot3DDisplay().Render(result, strategy, definition, _session.InitialRound.Steps);

        OnPropertyChanged(string.Empty);
    }

    private void UpdateDisplay(SurfaceResult result, CalculationParameters parameters)
    {
        _currentResult = result;
        var strategy   = CreateStrategy(_session.StrategyId);
        var definition = _project.ObjectDefinition;

        // ── Info panel ────────────────────────────────────────────────────────
        ProjectName        = _project.Name;
        ObjectTypeText     = "Surface Plate";
        DimensionsText     = FormatDimensions(definition);
        StrategyText       = strategy.DisplayName;
        StrategyParamsText = FormatStrategyParams(definition, _session.StrategyId);
        CalcMethodText     = parameters.MethodId == "SequentialIntegration"
            ? "Sequential Integration"
            : "Least Squares";
        SigmaThresholdText = parameters.AutoExcludeOutliers
            ? $"{parameters.SigmaThreshold:G}σ (auto-exclude on)"
            : "off";
        ExcludedStepsText  = parameters.ManuallyExcludedStepIndices.Count > 0
            ? $"{parameters.ManuallyExcludedStepIndices.Count} manual"
            : result.FlaggedStepIndices.Count > 0
                ? $"{result.FlaggedStepIndices.Count} auto"
                : "None";

        // ── Flatness / sigma ──────────────────────────────────────────────────
        FlatnessText     = $"{result.FlatnessValueMm * 1000.0:F3} \u00b5m";
        SigmaText        = $"\u03c3 = {result.Sigma * 1000.0:F3} \u00b5m";
        FlaggedCountText = result.FlaggedStepIndices.Count == 0
            ? "No flagged steps"
            : $"{result.FlaggedStepIndices.Count} flagged step(s)";

        HasFlaggedSteps         = result.FlaggedStepIndices.Count > 0;
        CorrectButtonVisibility = HasFlaggedSteps ? Visibility.Visible : Visibility.Collapsed;

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

        // ── Closure error stats ───────────────────────────────────────────────
        if (result.PrimitiveLoops.Length > 0)
        {
            ClosureErrorMeanText   = $"{result.ClosureErrorMean   * 1000.0:F3} \u00b5m";
            ClosureErrorMedianText = $"{result.ClosureErrorMedian * 1000.0:F3} \u00b5m";
            ClosureErrorMaxText    = $"{result.ClosureErrorMax    * 1000.0:F3} \u00b5m";
            ClosureErrorRmsText    = $"{result.ClosureErrorRms    * 1000.0:F3} \u00b5m";
            ClosureStatsVisibility = Visibility.Visible;
        }
        else
        {
            ClosureStatsVisibility = Visibility.Collapsed;
        }
    }

    // ── Info panel properties ─────────────────────────────────────────────────

    public string ProjectName        { get; private set; } = string.Empty;
    public string ObjectTypeText     { get; private set; } = string.Empty;
    public string DimensionsText     { get; private set; } = string.Empty;
    public string StrategyText       { get; private set; } = string.Empty;
    public string StrategyParamsText { get; private set; } = string.Empty;
    public string CalcMethodText     { get; private set; } = string.Empty;
    public string SigmaThresholdText { get; private set; } = string.Empty;
    public string ExcludedStepsText  { get; private set; } = string.Empty;

    // ── Measurement result properties ─────────────────────────────────────────

    public string FlatnessText     { get; private set; } = string.Empty;
    public string SigmaText        { get; private set; } = string.Empty;
    public string FlaggedCountText { get; private set; } = string.Empty;

    public bool       HasFlaggedSteps         { get; private set; }
    public Visibility CorrectButtonVisibility { get; private set; } = Visibility.Collapsed;

    public List<FlaggedStepItem> FlaggedSteps         { get; private set; } = [];
    public Visibility            FlaggedListVisibility { get; private set; } = Visibility.Collapsed;

    // ── Closure error stats properties ────────────────────────────────────────

    public string     ClosureErrorMeanText   { get; private set; } = string.Empty;
    public string     ClosureErrorMedianText { get; private set; } = string.Empty;
    public string     ClosureErrorMaxText    { get; private set; } = string.Empty;
    public string     ClosureErrorRmsText    { get; private set; } = string.Empty;
    public Visibility ClosureStatsVisibility { get; private set; } = Visibility.Collapsed;

    public object? PlotContent { get; private set; }

    // ── Exposed for dialogs ───────────────────────────────────────────────────

    public ObjectDefinition? ActiveObjectDefinition => _project?.ObjectDefinition;
    public string            ActiveOperator         => _project?.Operator ?? string.Empty;

    /// <summary>Current CalculationParameters for initialising the RecalculateDialog.</summary>
    public CalculationParameters CurrentCalculationParameters
        => GetCurrentCalculationParameters(_currentResult);

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void StartCorrectionSession()
        => _navigation.NavigateTo(PageKey.Correction, new CorrectionArgs(_project, _session));

    public void StartNewMeasurement(string operatorName, string notes, string strategyId)
    {
        var strategy   = CreateStrategy(strategyId);
        var definition = _project.ObjectDefinition;
        var steps      = strategy.GenerateSteps(definition).ToList();

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

    /// <summary>
    /// Re-runs the solver with <paramref name="parameters"/> and refreshes the display.
    /// If <paramref name="saveParameters"/> is true, persists parameters to the project.
    /// </summary>
    public async Task RecalculateAsync(CalculationParameters parameters, bool saveParameters)
    {
        var strategy   = CreateStrategy(_session.StrategyId);
        var definition = _project.ObjectDefinition;
        var excluded   = new HashSet<int>(parameters.ManuallyExcludedStepIndices);

        // Effective sigma: 0 = auto-exclude off means effectively infinite threshold
        double sigmaThreshold = parameters.AutoExcludeOutliers
            ? parameters.SigmaThreshold
            : double.MaxValue;

        // Merge original steps with any correction replacements
        var mergedSteps = GetMergedSteps();

        // Filter manually excluded steps
        var effectiveSteps = mergedSteps
            .Where(s => !excluded.Contains(s.Index))
            .ToList();

        var tempRound = new MeasurementRound { Steps = effectiveSteps };

        var result = await Task.Run(() => parameters.MethodId == "SequentialIntegration"
            ? new SequentialIntegrationCalculator(strategy)
                .Calculate(effectiveSteps, definition, new CalculationParameters
                {
                    MethodId            = parameters.MethodId,
                    SigmaThreshold      = sigmaThreshold,
                    AutoExcludeOutliers = parameters.AutoExcludeOutliers
                })
            : new SurfacePlateCalculator(definition, strategy, sigmaThreshold)
                .Calculate(tempRound));

        UpdateDisplay(result, parameters);
        PlotContent = new SurfacePlot3DDisplay().Render(result, strategy, definition, _session.InitialRound.Steps);
        OnPropertyChanged(string.Empty);

        if (saveParameters)
        {
            _session.InitialRound.CalculationParameters = parameters;
            _project.ModifiedAt = DateTime.UtcNow;
            _mainViewModel.MarkDirty();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static IMeasurementStrategy CreateStrategy(string strategyId) =>
        strategyId == "UnionJack"
            ? new UnionJackStrategy()
            : new FullGridStrategy();

    private CalculationParameters GetCurrentCalculationParameters(SurfaceResult? result)
    {
        // Prefer persisted params from the round
        if (_session?.InitialRound.CalculationParameters is { } saved)
            return saved;

        // Otherwise derive defaults from the last computed result
        return new CalculationParameters
        {
            MethodId            = "LeastSquares",
            SigmaThreshold      = result?.SigmaThreshold ?? 2.5,
            AutoExcludeOutliers = true
        };
    }

    /// <summary>
    /// Returns the initial round steps with all correction replacements applied.
    /// </summary>
    private List<MeasurementStep> GetMergedSteps()
    {
        if (_session.Corrections.Count == 0)
            return _session.InitialRound.Steps;

        var replacedMap = new Dictionary<int, double>();
        foreach (var correction in _session.Corrections)
            foreach (var r in correction.ReplacedSteps)
                replacedMap[r.OriginalStepIndex] = r.Reading;

        return _session.InitialRound.Steps
            .Select(s => new MeasurementStep
            {
                Index           = s.Index,
                GridCol         = s.GridCol,
                GridRow         = s.GridRow,
                Orientation     = s.Orientation,
                InstructionText = s.InstructionText,
                NodeId          = s.NodeId,
                ToNodeId        = s.ToNodeId,
                PassId          = s.PassId,
                Reading         = replacedMap.TryGetValue(s.Index, out double nr) ? nr : s.Reading
            })
            .ToList();
    }

    private static string FormatDimensions(ObjectDefinition def)
    {
        if (!def.Parameters.TryGetValue("widthMm",  out var w)) return string.Empty;
        if (!def.Parameters.TryGetValue("heightMm", out var h)) return string.Empty;
        return $"{Convert.ToDouble(w):G} \u00d7 {Convert.ToDouble(h):G} mm";
    }

    private static string FormatStrategyParams(ObjectDefinition def, string strategyId)
    {
        if (strategyId == "UnionJack")
        {
            int    seg   = def.Parameters.TryGetValue("segments", out var s) ? Convert.ToInt32(s) : 0;
            string rings = def.Parameters.TryGetValue("rings",    out var r)
                ? UnionJackStrategy.ParseRingsOption(r).ToString()
                : UnionJackRings.None.ToString();
            return $"segments: {seg}, rings: {rings}";
        }
        else
        {
            int cols = def.Parameters.TryGetValue("columnsCount", out var c) ? Convert.ToInt32(c) : 0;
            int rows = def.Parameters.TryGetValue("rowsCount",    out var r) ? Convert.ToInt32(r) : 0;
            return $"{cols} \u00d7 {rows}";
        }
    }
}
