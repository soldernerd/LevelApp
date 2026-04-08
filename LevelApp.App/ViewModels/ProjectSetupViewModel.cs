using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.DisplayModules.StrategyPreview;
using LevelApp.App.Navigation;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

// Alias so the short name 'UJRings' is available without ambiguity
using UJRings = LevelApp.Core.Geometry.SurfacePlate.Strategies.UnionJackRings;

namespace LevelApp.App.ViewModels;

public sealed partial class ProjectSetupViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly MainViewModel      _mainViewModel;
    private Project? _loadedProject;

    // Debounce timer for live preview
    private DispatcherQueueTimer? _previewDebounce;

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

    // ── Strategy selection ────────────────────────────────────────────────────

    /// <summary>0 = Full Grid, 1 = Union Jack</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFullGrid))]
    [NotifyPropertyChangedFor(nameof(IsUnionJack))]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private int _selectedStrategyIndex = 0;

    public bool IsFullGrid  => SelectedStrategyIndex == 0;
    public bool IsUnionJack => SelectedStrategyIndex == 1;

    // ── Full Grid parameters ──────────────────────────────────────────────────

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

    // ── Union Jack parameters ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private double _segments = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyPropertyChangedFor(nameof(UnionJackRingsIndex))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private UJRings _unionJackRingsOption = UJRings.Circumference;

    /// <summary>
    /// 0 = None, 1 = Circumference, 2 = Full — used for direct ComboBox SelectedIndex binding.
    /// </summary>
    public int UnionJackRingsIndex
    {
        get => (int)UnionJackRingsOption;
        set => UnionJackRingsOption = (UJRings)value;
    }

    // ── Initialisation from a loaded project ─────────────────────────────────

    public void Initialize(Project project)
    {
        _loadedProject = project;
        ProjectName    = project.Name;
        OperatorName   = project.Operator;
        Notes          = project.Notes;

        var p = project.ObjectDefinition.Parameters;
        if (p.TryGetValue("widthMm",  out var w)) WidthMm  = Convert.ToDouble(w);
        if (p.TryGetValue("heightMm", out var h)) HeightMm = Convert.ToDouble(h);

        // Detect strategy from existing session (if any)
        var lastSession = project.Measurements.LastOrDefault();
        if (lastSession?.StrategyId == "UnionJack")
        {
            SelectedStrategyIndex = 1;
            if (p.TryGetValue("segments", out var seg)) Segments = Convert.ToDouble(seg);
            if (p.TryGetValue("rings",    out var rng)) UnionJackRingsOption = UnionJackStrategy.ParseRingsOption(rng);
        }
        else
        {
            SelectedStrategyIndex = 0;
            if (p.TryGetValue("columnsCount", out var c)) ColumnsCount = Convert.ToDouble(c);
            if (p.TryGetValue("rowsCount",    out var r)) RowsCount    = Convert.ToDouble(r);
        }
    }

    // ── Derived display ───────────────────────────────────────────────────────

    public string StepCountText
    {
        get
        {
            try
            {
                var (strategy, def) = BuildStrategyAndDefinition();
                int count = strategy.GenerateSteps(def).Count;
                return $"{count} steps";
            }
            catch
            {
                return "\u2014";
            }
        }
    }

    // ── Live preview canvas ───────────────────────────────────────────────────

    public object? PreviewCanvas { get; private set; }

    /// <summary>
    /// Called from the View whenever parameters change (debounced ~300 ms).
    /// </summary>
    public void SchedulePreviewUpdate(DispatcherQueue queue)
    {
        if (_previewDebounce == null)
        {
            _previewDebounce = queue.CreateTimer();
            _previewDebounce.Interval  = TimeSpan.FromMilliseconds(300);
            _previewDebounce.IsRepeating = false;
            _previewDebounce.Tick += (_, _) => RenderPreview();
        }

        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private void RenderPreview()
    {
        try
        {
            var (strategy, def) = BuildStrategyAndDefinition();
            PreviewCanvas = StrategyPreviewRenderer.Render(strategy, def, previewWidth: 440);
        }
        catch
        {
            PreviewCanvas = new Canvas();
        }
        OnPropertyChanged(nameof(PreviewCanvas));
    }

    // ── Command ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartMeasurement))]
    private void StartMeasurement()
    {
        var (strategy, definition) = BuildStrategyAndDefinition();
        var steps = strategy.GenerateSteps(definition).ToList();

        Project project;
        if (_loadedProject is not null)
        {
            project = _loadedProject;
            var session = new MeasurementSession
            {
                Label        = $"Measurement {project.Measurements.Count + 1}",
                TakenAt      = DateTime.UtcNow,
                Operator     = OperatorName,
                InstrumentId = "manual-entry",
                StrategyId   = strategy.StrategyId,
                InitialRound = new MeasurementRound { Steps = steps }
            };
            project.Measurements.Add(session);
            _mainViewModel.MarkDirty();
            _navigation.NavigateTo(PageKey.Measurement, new MeasurementArgs(project, session));
        }
        else
        {
            var session = new MeasurementSession
            {
                Label        = "Measurement 1",
                TakenAt      = DateTime.UtcNow,
                Operator     = OperatorName,
                InstrumentId = "manual-entry",
                StrategyId   = strategy.StrategyId,
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

    private bool CanStartMeasurement()
    {
        if (string.IsNullOrWhiteSpace(ProjectName)) return false;
        if (WidthMm <= 0 || HeightMm <= 0) return false;
        if (IsFullGrid)
            return (int)Math.Round(ColumnsCount) >= 2 && (int)Math.Round(RowsCount) >= 2;
        // Union Jack: any rings option is valid; only segments needs checking
        int seg = (int)Math.Round(Segments);
        return seg >= 1;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private (IMeasurementStrategy strategy, ObjectDefinition definition) BuildStrategyAndDefinition()
    {
        var parameters = new Dictionary<string, object>
        {
            ["widthMm"]  = WidthMm,
            ["heightMm"] = HeightMm
        };

        IMeasurementStrategy strategy;

        if (IsUnionJack)
        {
            int seg = (int)Math.Round(Segments);
            if (seg < 1) throw new InvalidOperationException("segments must be ≥ 1.");
            parameters["segments"] = seg;
            parameters["rings"]    = UnionJackRingsOption.ToString();
            strategy = new UnionJackStrategy();
        }
        else
        {
            int cols = (int)Math.Round(ColumnsCount);
            int rows = (int)Math.Round(RowsCount);
            if (cols < 2 || rows < 2) throw new InvalidOperationException("Grid too small.");
            parameters["columnsCount"] = cols;
            parameters["rowsCount"]    = rows;
            strategy = new FullGridStrategy();
        }

        var definition = new ObjectDefinition
        {
            GeometryModuleId = "SurfacePlate",
            Parameters       = parameters
        };

        return (strategy, definition);
    }
}
