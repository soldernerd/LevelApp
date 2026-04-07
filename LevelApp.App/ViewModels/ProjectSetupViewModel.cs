using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Navigation;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.App.ViewModels;

public sealed partial class ProjectSetupViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly MainViewModel      _mainViewModel;
    private readonly FullGridStrategy   _strategy = new();

    /// <summary>Non-null when this VM was initialised from a loaded project rather
    /// than a blank new-project form. <see cref="StartMeasurement"/> adds the new
    /// session to this project instead of creating one from scratch.</summary>
    private Project? _loadedProject;

    public ProjectSetupViewModel(INavigationService navigation, MainViewModel mainViewModel)
    {
        _navigation    = navigation;
        _mainViewModel = mainViewModel;
    }

    // ── Project information ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    // ── Object parameters ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private double _widthMm = 1200;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private double _heightMm = 800;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private double _columnsCount = 8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private double _rowsCount = 5;

    // ── Initialisation from a loaded project ─────────────────────────────────

    /// <summary>
    /// Pre-populates all fields from an existing <see cref="Project"/>.
    /// Called from <c>ProjectSetupView.OnNavigatedTo</c> when the navigation
    /// parameter is a loaded project rather than a new-project blank form.
    /// </summary>
    public void Initialize(Project project)
    {
        _loadedProject = project;
        ProjectName    = project.Name;
        OperatorName   = project.Operator;
        Notes          = project.Notes;

        var p = project.ObjectDefinition.Parameters;
        if (p.TryGetValue("widthMm",      out var w)) WidthMm      = Convert.ToDouble(w);
        if (p.TryGetValue("heightMm",     out var h)) HeightMm     = Convert.ToDouble(h);
        if (p.TryGetValue("columnsCount", out var c)) ColumnsCount = Convert.ToDouble(c);
        if (p.TryGetValue("rowsCount",    out var r)) RowsCount    = Convert.ToDouble(r);
    }

    // ── Derived display ───────────────────────────────────────────────────────

    /// <summary>Live step-count preview shown below the grid size inputs.</summary>
    public string StepCountText
    {
        get
        {
            int cols = (int)Math.Round(ColumnsCount);
            int rows = (int)Math.Round(RowsCount);
            if (cols < 2 || rows < 2) return "\u2014";
            int count = rows * (cols - 1) + cols * (rows - 1);
            return $"{count} steps";
        }
    }

    // ── Command ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartMeasurement))]
    private void StartMeasurement()
    {
        int cols = (int)Math.Round(ColumnsCount);
        int rows = (int)Math.Round(RowsCount);

        var definition = new ObjectDefinition
        {
            GeometryModuleId = "SurfacePlate",
            Parameters = new Dictionary<string, object>
            {
                ["widthMm"]      = WidthMm,
                ["heightMm"]     = HeightMm,
                ["columnsCount"] = cols,
                ["rowsCount"]    = rows
            }
        };

        var steps = _strategy.GenerateSteps(definition).ToList();

        Project project;
        if (_loadedProject is not null)
        {
            // Adding a new measurement session to an already-active loaded project.
            // SetActiveProject was already called by OpenProjectAsync; just add the session.
            project = _loadedProject;
            var sessionLabel = $"Measurement {project.Measurements.Count + 1}";
            var session = new MeasurementSession
            {
                Label        = sessionLabel,
                TakenAt      = DateTime.UtcNow,
                Operator     = OperatorName,
                InstrumentId = "manual-entry",
                StrategyId   = "FullGrid",
                InitialRound = new MeasurementRound { Steps = steps }
            };
            project.Measurements.Add(session);
            _mainViewModel.MarkDirty();
            _navigation.NavigateTo(PageKey.Measurement, new MeasurementArgs(project, session));
        }
        else
        {
            // Brand-new project created from the blank form.
            var session = new MeasurementSession
            {
                Label        = "Measurement 1",
                TakenAt      = DateTime.UtcNow,
                Operator     = OperatorName,
                InstrumentId = "manual-entry",
                StrategyId   = "FullGrid",
                InitialRound = new MeasurementRound { Steps = steps }
            };
            project = new Project
            {
                Name             = ProjectName,
                Operator         = OperatorName,
                Notes            = Notes,
                ObjectDefinition = definition
            };
            project.Measurements.Add(session);
            _mainViewModel.SetActiveProject(project);
            _mainViewModel.MarkDirty();
            _navigation.NavigateTo(PageKey.Measurement, new MeasurementArgs(project, session));
        }
    }

    private bool CanStartMeasurement() =>
        !string.IsNullOrWhiteSpace(ProjectName) &&
        WidthMm  > 0 &&
        HeightMm > 0 &&
        (int)Math.Round(ColumnsCount) >= 2 &&
        (int)Math.Round(RowsCount)    >= 2;
}
