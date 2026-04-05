using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Navigation;
using LevelApp.App.Services;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.App.ViewModels;

public sealed partial class ProjectSetupViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly ProjectFileService _fileService;
    private readonly FullGridStrategy   _strategy = new();

    public ProjectSetupViewModel(INavigationService navigation, ProjectFileService fileService)
    {
        _navigation  = navigation;
        _fileService = fileService;
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

    // ── Open-project feedback ─────────────────────────────────────────────────

    [ObservableProperty]
    private string _loadErrorText = string.Empty;

    [ObservableProperty]
    private bool _loadErrorVisible;

    // ── Derived display ───────────────────────────────────────────────────────

    /// <summary>Live step-count preview shown below the grid size inputs.</summary>
    public string StepCountText
    {
        get
        {
            int cols = (int)Math.Round(ColumnsCount);
            int rows = (int)Math.Round(RowsCount);
            if (cols < 2 || rows < 2) return "—";
            int count = rows * (cols - 1) + cols * (rows - 1);
            return $"{count} steps";
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

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

        var session = new MeasurementSession
        {
            Label        = "Measurement 1",
            TakenAt      = DateTime.UtcNow,
            Operator     = OperatorName,
            InstrumentId = "manual-entry",
            StrategyId   = "FullGrid",
            InitialRound = new MeasurementRound { Steps = steps }
        };

        var project = new Project
        {
            Name             = ProjectName,
            Operator         = OperatorName,
            Notes            = Notes,
            ObjectDefinition = definition
        };
        project.Measurements.Add(session);

        _navigation.NavigateTo(PageKey.Measurement, new MeasurementArgs(project, session));
    }

    private bool CanStartMeasurement() =>
        !string.IsNullOrWhiteSpace(ProjectName) &&
        WidthMm  > 0 &&
        HeightMm > 0 &&
        (int)Math.Round(ColumnsCount) >= 2 &&
        (int)Math.Round(RowsCount)    >= 2;

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        LoadErrorVisible = false;

        Project? project;
        try
        {
            project = await _fileService.OpenAsync();
        }
        catch (Exception ex)
        {
            LoadErrorText    = $"Failed to open file: {ex.Message}";
            LoadErrorVisible = true;
            return;
        }

        if (project is null) return; // user cancelled picker

        // Navigate to Results if the project has a completed session
        var completedSession = project.Measurements
            .LastOrDefault(m => m.InitialRound.Result is not null);

        if (completedSession is not null)
        {
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(project, completedSession));
            return;
        }

        // No completed measurements — show a message (project may be partially measured)
        LoadErrorText    = "No completed measurements found in this project file.";
        LoadErrorVisible = true;
    }
}
