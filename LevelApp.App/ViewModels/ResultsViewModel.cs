using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using LevelApp.App.DisplayModules.ParallelWaysDisplay;
using LevelApp.App.DisplayModules.SurfacePlot3D;
using LevelApp.App.Navigation;
using LevelApp.App.Services;
using LevelApp.Core.Geometry;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;
using Microsoft.UI.Xaml;

namespace LevelApp.App.ViewModels;

public sealed partial class ResultsViewModel : ViewModelBase
{
    private readonly INavigationService  _navigation;
    private readonly MainViewModel       _mainViewModel;
    private readonly IInstrumentChoiceCatalog _catalog;

    private Project            _project = null!;
    private MeasurementSession _session = null!;
    private SurfaceResult?     _currentResult;

    public ResultsViewModel(INavigationService navigation, MainViewModel mainViewModel,
                            IInstrumentChoiceCatalog catalog)
    {
        _navigation    = navigation;
        _mainViewModel = mainViewModel;
        _catalog       = catalog;
    }

    /// <summary>Instruments offered by the New Measurement dialog (manual entry + registered devices).</summary>
    public IReadOnlyList<InstrumentChoice> InstrumentChoices => _catalog.GetChoices();

    /// <summary>Index of the instrument to pre-select in the New Measurement dialog.</summary>
    public int GetDefaultInstrumentIndex(IReadOnlyList<InstrumentChoice> choices)
        => _catalog.GetDefaultIndex(choices);

    // ── Initialisation ────────────────────────────────────────────────────────

    // ── Measurement selector (all measurements of the project) ───────────────

    /// <summary>One entry of the "Measurement" pull-down.</summary>
    public sealed record SessionItem(MeasurementSession Session, string Text)
    {
        public override string ToString() => Text;
    }

    /// <summary>Every measurement in the project, finished or not.</summary>
    public IReadOnlyList<SessionItem> SessionItems { get; private set; } = [];

    private SessionItem? _selectedSessionItem;

    /// <summary>
    /// The measurement shown. Picking another one opens it: its results when it is finished,
    /// the measurement screen (resuming where it stopped) when it is not.
    /// </summary>
    public SessionItem? SelectedSessionItem
    {
        get => _selectedSessionItem;
        set
        {
            // A ComboBox reports null while its list is rebuilt; that is not a choice.
            if (value is null || ReferenceEquals(value, _selectedSessionItem)) return;
            bool another = !ReferenceEquals(value.Session, _session);
            _selectedSessionItem = value;
            OnPropertyChanged();
            if (another) OpenSession(value.Session);
        }
    }

    private void BuildSessionItems()
    {
        SessionItems = _project.Measurements.Select(m => new SessionItem(m, DescribeSession(m))).ToList();
        _selectedSessionItem = SessionItems.FirstOrDefault(i => ReferenceEquals(i.Session, _session));
    }

    private static string DescribeSession(MeasurementSession s)
    {
        string strategy;
        try { strategy = StrategyFactory.Create(s.StrategyId).DisplayName; }
        catch { strategy = s.StrategyId; }

        string when = s.TakenAt == default
            ? ""
            : " \u00b7 " + s.TakenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string state = s.IsUnfinished()
            ? $" \u00b7 unfinished {s.ReadingCount()}/{s.InitialRound.Steps.Count}"
            : "";
        return $"{s.Label} \u00b7 {strategy}{when}{state}";
    }

    private void OpenSession(MeasurementSession session)
    {
        if (session.IsUnfinished())
            _navigation.NavigateTo(PageKey.Measurement, new MeasurementArgs(_project, session));
        else
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(_project, session));
    }

    public void Initialize(ResultsArgs args)
    {
        _project = args.Project;
        _session = args.Session;
        BuildSessionItems();
        UpdateMetadata();
        UpdateProjectTab();

        if (CurrentDefinition.GeometryModuleId == "ParallelWays")
        {
            var pwResult = _session.InitialRound.ParallelWaysResult
                ?? throw new InvalidOperationException("Session has no Parallel Ways result to display.");

            var pwp   = ParallelWaysParameters.From(CurrentDefinition.Parameters);
            var strat = ParallelWaysStrategyParameters.From(CurrentDefinition.Parameters);

            UpdateParallelWaysDisplay(pwResult, pwp, strat);
            PlotContent = new ParallelWaysDisplay().Render(
                pwResult, pwp, strat, _session.InitialRound.Steps);
        }
        else
        {
            SurfaceResult result =
                _session.Corrections.Count > 0 && _session.Corrections.Last().Result is { } cr
                    ? cr
                    : _session.InitialRound.Result
                      ?? throw new InvalidOperationException("Session has no result to display.");

            var strategy   = StrategyFactory.Create(_session.StrategyId);
            var definition = CurrentDefinition;

            UpdateDisplay(result, GetCurrentCalculationParameters(result));
            PlotContent = new SurfacePlot3DDisplay().Render(result, strategy, definition, _session.InitialRound.Steps);
        }

        OnPropertyChanged(string.Empty);
    }

    private void UpdateDisplay(SurfaceResult result, CalculationParameters parameters)
    {
        _currentResult = result;
        IsParallelWays = false;
        var strategy   = StrategyFactory.Create(_session.StrategyId);
        var definition = CurrentDefinition;

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

        FlaggedListVisibility    = FlaggedSteps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoFlaggedStepsVisibility = FlaggedSteps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

        ActiveResult     = result;
        ActiveSteps      = _session.InitialRound.Steps;
        ActiveDefinition = CurrentDefinition;
    }

    // ── Measurement meta data (who, when, with what) ──────────────────────────

    public string MeasurementLabelText { get; private set; } = string.Empty;
    public string TakenText            { get; private set; } = string.Empty;
    public string CompletedText        { get; private set; } = string.Empty;
    public string OperatorText         { get; private set; } = string.Empty;
    public string InstrumentText       { get; private set; } = string.Empty;
    public string NotesText            { get; private set; } = string.Empty;
    public Visibility NotesVisibility  => NotesText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The instrument of a measurement on up to three lines: its given name, its technical identifier and
    /// what it reported about itself (serial, firmware). Measurements taken before these were recorded
    /// show the plugin only.
    /// </summary>
    private string DescribeInstrument(MeasurementSession s)
    {
        if (!string.IsNullOrWhiteSpace(s.InstrumentName))
        {
            var lines = new List<string> { s.InstrumentName };
            if (!string.IsNullOrWhiteSpace(s.InstrumentTechnicalId)) lines.Add(s.InstrumentTechnicalId);
            if (!string.IsNullOrWhiteSpace(s.InstrumentDetails))     lines.Add(s.InstrumentDetails);
            return string.Join("\n", lines);
        }

        if (s.InstrumentId == "manual-entry") return "Manual entry";
        if (s.InstrumentId.Length == 0) return "\u2014";

        string plugin = _catalog.PluginDisplayName(s.InstrumentId) ?? s.InstrumentId;
        string text = $"{plugin}\n(device not recorded for this measurement)";
        return string.IsNullOrWhiteSpace(s.InstrumentDetails) ? text : $"{text}\n{s.InstrumentDetails}";
    }

    private static string FormatTime(DateTime utc) =>
        utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private void UpdateMetadata()
    {
        var s = _session;
        MeasurementLabelText = s.Label;
        TakenText      = s.TakenAt == default ? "\u2014" : FormatTime(s.TakenAt);
        CompletedText  = s.InitialRound.CompletedAt is { } done ? FormatTime(done) : "not finished";
        OperatorText   = string.IsNullOrWhiteSpace(s.Operator) ? "\u2014" : s.Operator;
        NotesText      = s.Notes?.Trim() ?? string.Empty;

        InstrumentText = DescribeInstrument(s);
    }

    // ── Project tab: what is known about the project and its object ───────────

    public string ProjectCreatedText  { get; private set; } = string.Empty;
    public string ProjectModifiedText { get; private set; } = string.Empty;
    public string ProjectOperatorText { get; private set; } = string.Empty;
    public string ProjectNotesText    { get; private set; } = string.Empty;
    public string ProjectFileText     { get; private set; } = string.Empty;
    public string ProjectObjectText   { get; private set; } = string.Empty;
    public string ProjectDetailsText  { get; private set; } = string.Empty;
    public string ProjectMeasurementCountText { get; private set; } = string.Empty;
    public IReadOnlyList<ProjectMeasurementRow> ProjectMeasurements { get; private set; } = [];

    private void UpdateProjectTab()
    {
        var p = _project;
        ProjectCreatedText  = FormatTime(p.CreatedAt);
        ProjectModifiedText = FormatTime(p.ModifiedAt);
        ProjectOperatorText = string.IsNullOrWhiteSpace(p.Operator) ? "\u2014" : p.Operator;
        ProjectNotesText    = string.IsNullOrWhiteSpace(p.Notes) ? "\u2014" : p.Notes.Trim();
        ProjectFileText     = _mainViewModel.CurrentFilePath ?? "not saved yet";

        // The object: from the project's own definition (each measurement's own settings are in the table).
        var def = p.ObjectDefinition;
        if (def.GeometryModuleId == "ParallelWays")
        {
            ProjectObjectText  = "Parallel ways";
            ProjectDetailsText = $"{ParallelWaysParameters.From(def.Parameters).Rails.Count} rails";
        }
        else
        {
            ProjectObjectText  = "Surface plate";
            ProjectDetailsText = FormatDimensions(def);
        }

        ProjectMeasurementCountText = p.Measurements.Count == 1 ? "1 measurement" : $"{p.Measurements.Count} measurements";
        ProjectMeasurements = p.Measurements.Select(m => new ProjectMeasurementRow(
            m.Label,
            DescribeStrategy(m),
            m.TakenAt == default ? "\u2014" : FormatTime(m.TakenAt),
            FirstLine(DescribeInstrument(m)),
            DescribeOutcome(m))).ToList();
    }

    private string DescribeStrategy(MeasurementSession m)
    {
        string name;
        try { name = StrategyFactory.Create(m.StrategyId).DisplayName; }
        catch { name = m.StrategyId; }

        var def = _project.DefinitionFor(m);
        return def.GeometryModuleId == "ParallelWays" ? name : $"{name} {FormatStrategyParams(def, m.StrategyId)}";
    }

    private static string FirstLine(string text)
    {
        int i = text.IndexOf('\n');
        return i < 0 ? text : text[..i];
    }

    private static string DescribeOutcome(MeasurementSession m)
    {
        if (m.IsUnfinished())
            return $"unfinished {m.ReadingCount()}/{m.InitialRound.Steps.Count}";

        var result = m.Corrections.LastOrDefault()?.Result ?? m.InitialRound.Result;
        return result is null
            ? "done"
            : $"flatness {result.FlatnessValueMm * 1000.0:F3} \u00b5m";
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

    public List<FlaggedStepItem> FlaggedSteps             { get; private set; } = [];
    public Visibility            FlaggedListVisibility    { get; private set; } = Visibility.Collapsed;
    public Visibility            NoFlaggedStepsVisibility { get; private set; } = Visibility.Visible;

    // ── Closure error stats properties ────────────────────────────────────────

    public string     ClosureErrorMeanText   { get; private set; } = string.Empty;
    public string     ClosureErrorMedianText { get; private set; } = string.Empty;
    public string     ClosureErrorMaxText    { get; private set; } = string.Empty;
    public string     ClosureErrorRmsText    { get; private set; } = string.Empty;
    public Visibility ClosureStatsVisibility { get; private set; } = Visibility.Collapsed;

    public object? PlotContent { get; private set; }

    // ── Geometry-type visibility ──────────────────────────────────────────────

    public bool       IsParallelWays              { get; private set; }
    public bool       IsSurfacePlate              => !IsParallelWays;
    public Visibility SurfacePlateOnlyVisibility  => IsParallelWays ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ParallelWaysOnlyVisibility  => IsParallelWays ? Visibility.Visible   : Visibility.Collapsed;

    // ── Parallel Ways-specific result properties ──────────────────────────────

    public string ParallelismText      { get; private set; } = string.Empty;

    // ── Data exposed to the Measurements canvas renderer ─────────────────────

    public SurfaceResult?                 ActiveResult     { get; private set; }
    public IReadOnlyList<MeasurementStep> ActiveSteps      { get; private set; } = [];
    public ObjectDefinition?              ActiveDefinition { get; private set; }

    // ── Exposed for NewMeasurementDialog (used from ResultsView code-behind) ──

    /// <summary>The object definition of the measurement being shown (the project's, plus its own parameters).</summary>
    private ObjectDefinition CurrentDefinition => _project.DefinitionFor(_session);

    /// <summary>
    /// One line describing what "Measure again" will do: the object and the strategy of the last
    /// measurement of the project, which the new one repeats.
    /// </summary>
    public string NewMeasurementSummary
    {
        get
        {
            var basis = _project.Measurements.LastOrDefault() ?? _session;
            var def   = _project.DefinitionFor(basis);

            string strategy;
            try { strategy = StrategyFactory.Create(basis.StrategyId).DisplayName; }
            catch { strategy = basis.StrategyId; }

            if (def.GeometryModuleId == "ParallelWays")
                return $"Parallel Ways · {strategy}";

            return $"Surface plate {FormatDimensions(def)} · {strategy} {FormatStrategyParams(def, basis.StrategyId)}";
        }
    }
    public string            ActiveOperator         => _project?.Operator ?? string.Empty;

    /// <summary>Current CalculationParameters for initialising the RecalculateDialog.</summary>
    public CalculationParameters CurrentCalculationParameters
        => GetCurrentCalculationParameters(_currentResult);

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// "Measure with a different strategy": back to the setup page of this project, where the strategy,
    /// rows, columns, segments ... are chosen; Start Measurement there adds the new measurement.
    /// </summary>
    [RelayCommand]
    private void StartDifferentStrategy()
        => _navigation.NavigateTo(PageKey.ProjectSetup, _project);

    [RelayCommand]
    private void StartCorrectionSession()
        => _navigation.NavigateTo(PageKey.Correction, new CorrectionArgs(_project, _session));

    /// <summary>
    /// "Measure again": a new measurement with the strategy and geometry of the project's last one.
    /// (Another strategy goes through <see cref="StartDifferentStrategyCommand"/>, which opens the setup page.)
    /// </summary>
    public void StartNewMeasurement(string operatorName, string notes,
                                    InstrumentChoice? instrument = null, string? label = null)
    {
        if (instrument is not null)
            _catalog.Select(instrument);

        var basis      = _project.Measurements.LastOrDefault() ?? _session;
        string strategyId = basis.StrategyId;
        var strategy   = StrategyFactory.Create(strategyId);
        var definition = _project.DefinitionFor(basis);
        var steps      = strategy.GenerateSteps(definition).ToList();

        var session = new MeasurementSession
        {
            Label        = string.IsNullOrWhiteSpace(label) ? $"Measurement {_project.Measurements.Count + 1}" : label.Trim(),
            TakenAt      = DateTime.UtcNow,
            Operator     = operatorName,
            Notes        = notes,
            InstrumentId = instrument?.PluginId ?? "manual-entry",
            InstrumentName = instrument?.Label ?? string.Empty,
            InstrumentTechnicalId = instrument?.Technical ?? string.Empty,
            StrategyId   = strategyId,
            StrategyParameters = basis.StrategyParameters is null ? null : new Dictionary<string, object>(basis.StrategyParameters),
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
        var strategy   = StrategyFactory.Create(_session.StrategyId);
        var definition = CurrentDefinition;
        var excluded   = new HashSet<int>(parameters.ManuallyExcludedStepIndices);

        // Merge original steps with any correction replacements
        var allReplacements = _session.Corrections.SelectMany(c => c.ReplacedSteps);
        var mergedSteps = MeasurementRound.MergeWithReplacements(
            _session.InitialRound.Steps, allReplacements);

        // Filter manually excluded steps
        var effectiveSteps = mergedSteps
            .Where(s => !excluded.Contains(s.Index))
            .ToList();

        var calculator = CalculatorFactory.Create(parameters.MethodId, strategy);
        var result = await Task.Run(() => calculator.Calculate(effectiveSteps, definition, parameters));

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

    private void UpdateParallelWaysDisplay(
        ParallelWaysResult            pwResult,
        ParallelWaysParameters        pwp,
        ParallelWaysStrategyParameters strat)
    {
        IsParallelWays = true;

        ProjectName    = _project.Name;
        ObjectTypeText = "Parallel Ways";
        DimensionsText = pwp.Rails.Count > 0
            ? $"{pwp.Rails.Count} rails, {pwp.Rails.Max(r => r.LengthMm):G} mm"
            : string.Empty;
        StrategyText       = "Parallel Ways";
        StrategyParamsText = $"{pwp.Orientation}, {strat.Tasks.Count} task(s)";
        CalcMethodText     = strat.SolverMode.ToString();
        SigmaThresholdText = $"{pwResult.SigmaThreshold:G}\u03c3";
        ExcludedStepsText  = pwResult.FlaggedStepIndices.Length > 0
            ? $"{pwResult.FlaggedStepIndices.Length} auto"
            : "None";

        // Best (worst-case) straightness across all rails
        double bestStraightness = pwResult.RailProfiles.Count > 0
            ? pwResult.RailProfiles.Max(p => p.StraightnessValueMm)
            : 0.0;
        FlatnessText     = $"{bestStraightness * 1000.0:F3} \u00b5m";
        SigmaText        = $"\u03c3 = {pwResult.ResidualRms * 1000.0:F3} \u00b5m";
        FlaggedCountText = pwResult.FlaggedStepIndices.Length == 0
            ? "No flagged steps"
            : $"{pwResult.FlaggedStepIndices.Length} flagged step(s)";

        HasFlaggedSteps          = pwResult.FlaggedStepIndices.Length > 0;
        CorrectButtonVisibility  = Visibility.Collapsed;
        FlaggedSteps             = [];
        FlaggedListVisibility    = Visibility.Collapsed;
        NoFlaggedStepsVisibility = Visibility.Visible;
        ClosureStatsVisibility   = Visibility.Collapsed;

        // Per-rail straightness + per-pair parallelism
        var railLines = pwResult.RailProfiles
            .Select(p =>
            {
                string label = pwp.Rails.Count > p.RailIndex
                    ? pwp.Rails[p.RailIndex].Label
                    : $"Rail {p.RailIndex + 1}";
                return $"{label}: {p.StraightnessValueMm * 1000.0:F3} \u00b5m";
            });

        var pairLines = pwResult.ParallelismProfiles
            .Select(pp =>
            {
                string la = pwp.Rails.Count > pp.RailIndexA ? pwp.Rails[pp.RailIndexA].Label : $"R{pp.RailIndexA}";
                string lb = pwp.Rails.Count > pp.RailIndexB ? pwp.Rails[pp.RailIndexB].Label : $"R{pp.RailIndexB}";
                return $"{la}\u2013{lb}: {pp.ParallelismValueMm * 1000.0:F3} \u00b5m";
            });

        var allLines = railLines.Concat(new[] { "Parallelism:" }).Concat(pairLines);
        ParallelismText = string.Join("\n", allLines);

        ActiveResult     = null;
        ActiveSteps      = _session.InitialRound.Steps;
        ActiveDefinition = CurrentDefinition;
    }

    /// <summary>
    /// Rebuilds <see cref="PlotContent"/> from cached session data so that the
    /// canvas picks up new theme colours after a theme change.
    /// Call from the view's <c>ActualThemeChanged</c> handler.
    /// </summary>
    public void RebuildPlotCanvas()
    {
        if (_session is null || _project is null) return;

        if (IsParallelWays)
        {
            var pwResult = _session.InitialRound.ParallelWaysResult;
            if (pwResult is null) return;
            var pwp   = ParallelWaysParameters.From(CurrentDefinition.Parameters);
            var strat = ParallelWaysStrategyParameters.From(CurrentDefinition.Parameters);
            PlotContent = new ParallelWaysDisplay().Render(
                pwResult, pwp, strat, _session.InitialRound.Steps);
        }
        else
        {
            if (_currentResult is null) return;
            var strategy   = StrategyFactory.Create(_session.StrategyId);
            var definition = CurrentDefinition;
            PlotContent = new SurfacePlot3DDisplay().Render(
                _currentResult, strategy, definition, _session.InitialRound.Steps);
        }

        OnPropertyChanged(nameof(PlotContent));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
