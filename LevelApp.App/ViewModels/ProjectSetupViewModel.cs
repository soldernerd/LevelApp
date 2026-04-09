using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.DisplayModules.StrategyPreview;
using LevelApp.App.Navigation;
using LevelApp.Core.Geometry;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

// Alias so the short name 'UJRings' is available without ambiguity
using UJRings = LevelApp.Core.Geometry.SurfacePlate.Strategies.UnionJackRings;

namespace LevelApp.App.ViewModels;

// ── Rail editor item ──────────────────────────────────────────────────────────

/// <summary>Observable wrapper for a single rail entry in the editor.</summary>
public sealed partial class RailViewModel : ObservableObject
{
    [ObservableProperty] private string _label              = string.Empty;
    [ObservableProperty] private double _lengthMm           = 1000.0;
    [ObservableProperty] private double _axialOffsetMm      = 0.0;
    [ObservableProperty] private double _lateralSeparationMm = 0.0;
    [ObservableProperty] private double _verticalOffsetMm   = 0.0;
    [ObservableProperty] private bool   _isReference        = false;

    public RailDefinition ToDefinition() => new()
    {
        Label               = Label,
        LengthMm            = LengthMm,
        AxialOffsetMm       = AxialOffsetMm,
        LateralSeparationMm = LateralSeparationMm,
        VerticalOffsetMm    = VerticalOffsetMm
    };
}

// ── Task editor item ──────────────────────────────────────────────────────────

/// <summary>Observable wrapper for a single task entry in the editor.</summary>
public sealed partial class TaskViewModel : ObservableObject
{
    /// <summary>0 = Along Rail, 1 = Bridge</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBridge))]
    [NotifyPropertyChangedFor(nameof(IsAlongRail))]
    private int    _taskTypeIndex      = 0;
    /// <summary>Stored as double so NumberBox can bind directly.</summary>
    [ObservableProperty] private double _railIndexA         = 0;
    /// <summary>Stored as double so NumberBox can bind directly.</summary>
    [ObservableProperty] private double _railIndexB         = 1;
    [ObservableProperty] private int    _passDirectionIndex = 0;  // 0 = Single, 1 = Forward+Return
    [ObservableProperty] private double _stepDistanceMm     = 200.0;

    public bool          IsBridge      => TaskTypeIndex == 1;
    public bool          IsAlongRail   => TaskTypeIndex == 0;
    public TaskType      TaskType      => (TaskType)TaskTypeIndex;
    public PassDirection PassDirection => (PassDirection)PassDirectionIndex;

    public ParallelWaysTask ToTask() => new()
    {
        TaskType       = TaskType,
        RailIndexA     = (int)Math.Round(RailIndexA),
        RailIndexB     = (int)Math.Round(RailIndexB),
        PassDirection  = PassDirection,
        StepDistanceMm = StepDistanceMm
    };
}

// ── ProjectSetupViewModel ─────────────────────────────────────────────────────

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

        // Initialise with two default rails
        _railItems.Add(new RailViewModel { Label = "Front", LengthMm = 1000, IsReference = true });
        _railItems.Add(new RailViewModel { Label = "Rear",  LengthMm = 1000, LateralSeparationMm = 400 });

        // Initialise with one default task
        _taskItems.Add(new TaskViewModel { TaskTypeIndex = 0, RailIndexA = 0, StepDistanceMm = 200 });
        _taskItems.Add(new TaskViewModel { TaskTypeIndex = 0, RailIndexA = 1, StepDistanceMm = 200 });
    }

    // ── Project information ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    // ── Geometry type selection ───────────────────────────────────────────────

    /// <summary>0 = Surface Plate, 1 = Parallel Ways</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSurfacePlate))]
    [NotifyPropertyChangedFor(nameof(IsParallelWays))]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private int _selectedGeometryIndex = 0;

    public bool IsSurfacePlate => SelectedGeometryIndex == 0;
    public bool IsParallelWays => SelectedGeometryIndex == 1;

    // ── Strategy selection (Surface Plate only) ───────────────────────────────

    /// <summary>0 = Full Grid, 1 = Union Jack</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFullGrid))]
    [NotifyPropertyChangedFor(nameof(IsUnionJack))]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private int _selectedStrategyIndex = 0;

    public bool IsFullGrid  => IsSurfacePlate && SelectedStrategyIndex == 0;
    public bool IsUnionJack => IsSurfacePlate && SelectedStrategyIndex == 1;

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

    // ── Parallel Ways parameters ──────────────────────────────────────────────

    /// <summary>0 = Horizontal, 1 = Vertical</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepCountText))]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    private int _waysOrientationIndex = 0;

    /// <summary>0 = First Station Anchor, 1 = Linear Drift Correction, 2 = Least Squares</summary>
    [ObservableProperty] private int _driftCorrectionIndex = 2;

    /// <summary>0 = Independent then Reconcile, 1 = Global Least Squares</summary>
    [ObservableProperty] private int _solverModeIndex = 1;

    private readonly ObservableCollection<RailViewModel> _railItems = [];
    private readonly ObservableCollection<TaskViewModel> _taskItems = [];

    public ObservableCollection<RailViewModel> RailItems => _railItems;
    public ObservableCollection<TaskViewModel> TaskItems => _taskItems;

    // ── Rail commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddRail()
    {
        double lateral = _railItems.Count > 0
            ? _railItems.Max(r => r.LateralSeparationMm) + 400
            : 0;
        _railItems.Add(new RailViewModel
        {
            Label               = $"Rail {_railItems.Count + 1}",
            LengthMm            = _railItems.FirstOrDefault()?.LengthMm ?? 1000,
            LateralSeparationMm = lateral
        });
        NotifyStepCountChanged();
        StartMeasurementCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveRail(RailViewModel rail)
    {
        if (_railItems.Count <= 2) return;
        _railItems.Remove(rail);
        // If the removed rail was reference, assign reference to the first rail
        if (!_railItems.Any(r => r.IsReference))
            _railItems[0].IsReference = true;
        NotifyStepCountChanged();
        StartMeasurementCommand.NotifyCanExecuteChanged();
    }

    public void SetRailAsReference(RailViewModel selected)
    {
        foreach (var r in _railItems) r.IsReference = (r == selected);
    }

    // ── Task commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddTask()
    {
        _taskItems.Add(new TaskViewModel
        {
            StepDistanceMm = _taskItems.FirstOrDefault()?.StepDistanceMm ?? 200
        });
        NotifyStepCountChanged();
    }

    [RelayCommand]
    private void RemoveTask(TaskViewModel task)
    {
        if (_taskItems.Count <= 1) return;
        _taskItems.Remove(task);
        NotifyStepCountChanged();
    }

    [RelayCommand]
    private void MoveTaskUp(TaskViewModel task)
    {
        int idx = _taskItems.IndexOf(task);
        if (idx <= 0) return;
        _taskItems.Move(idx, idx - 1);
        NotifyStepCountChanged();
    }

    [RelayCommand]
    private void MoveTaskDown(TaskViewModel task)
    {
        int idx = _taskItems.IndexOf(task);
        if (idx < 0 || idx >= _taskItems.Count - 1) return;
        _taskItems.Move(idx, idx + 1);
        NotifyStepCountChanged();
    }

    private void NotifyStepCountChanged()
    {
        OnPropertyChanged(nameof(StepCountText));
        SchedulePreviewUpdate(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
    }

    // ── Initialisation from a loaded project ─────────────────────────────────

    public void Initialize(Project project)
    {
        _loadedProject = project;
        ProjectName    = project.Name;
        OperatorName   = project.Operator;
        Notes          = project.Notes;

        var def = project.ObjectDefinition;
        var p   = def.Parameters;

        if (def.GeometryModuleId == "ParallelWays")
        {
            SelectedGeometryIndex = 1;

            var pwp   = ParallelWaysParameters.From(p);
            var strat = ParallelWaysStrategyParameters.From(p);

            WaysOrientationIndex = pwp.Orientation == WaysOrientation.Vertical ? 1 : 0;
            DriftCorrectionIndex = (int)strat.DriftCorrection;
            SolverModeIndex      = (int)strat.SolverMode;

            _railItems.Clear();
            for (int i = 0; i < pwp.Rails.Count; i++)
            {
                var rd = pwp.Rails[i];
                _railItems.Add(new RailViewModel
                {
                    Label               = rd.Label,
                    LengthMm            = rd.LengthMm,
                    AxialOffsetMm       = rd.AxialOffsetMm,
                    LateralSeparationMm = rd.LateralSeparationMm,
                    VerticalOffsetMm    = rd.VerticalOffsetMm,
                    IsReference         = (i == pwp.ReferenceRailIndex)
                });
            }

            _taskItems.Clear();
            foreach (var t in strat.Tasks)
            {
                _taskItems.Add(new TaskViewModel
                {
                    TaskTypeIndex      = (int)t.TaskType,
                    RailIndexA         = t.RailIndexA,
                    RailIndexB         = (double)t.RailIndexB,
                    PassDirectionIndex = (int)t.PassDirection,
                    StepDistanceMm     = t.StepDistanceMm
                });
            }
        }
        else
        {
            SelectedGeometryIndex = 0;
            if (p.TryGetValue("widthMm",  out var w)) WidthMm  = Convert.ToDouble(w);
            if (p.TryGetValue("heightMm", out var h)) HeightMm = Convert.ToDouble(h);

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
    public void SchedulePreviewUpdate(DispatcherQueue? queue)
    {
        if (queue is null) return;

        if (_previewDebounce == null)
        {
            _previewDebounce = queue.CreateTimer();
            _previewDebounce.Interval    = TimeSpan.FromMilliseconds(300);
            _previewDebounce.IsRepeating = false;
            _previewDebounce.Tick       += (_, _) => RenderPreview();
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

        if (IsParallelWays)
        {
            if (_railItems.Count < 2) return false;
            if (!_railItems.Any(r => r.IsReference)) return false;
            if (_taskItems.Count == 0) return false;
            if (_railItems.Any(r => r.LengthMm <= 0)) return false;
            if (_taskItems.Any(t => t.StepDistanceMm <= 0)) return false;
            return true;
        }

        // Surface Plate
        if (WidthMm <= 0 || HeightMm <= 0) return false;
        if (IsFullGrid)
            return (int)Math.Round(ColumnsCount) >= 2 && (int)Math.Round(RowsCount) >= 2;
        int seg = (int)Math.Round(Segments);
        return seg >= 1;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private (IMeasurementStrategy strategy, ObjectDefinition definition) BuildStrategyAndDefinition()
    {
        if (IsParallelWays)
            return BuildParallelWaysDefinition();

        // ── Surface Plate ─────────────────────────────────────────────────────
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
            strategy = StrategyFactory.Create("UnionJack");
        }
        else
        {
            int cols = (int)Math.Round(ColumnsCount);
            int rows = (int)Math.Round(RowsCount);
            if (cols < 2 || rows < 2) throw new InvalidOperationException("Grid too small.");
            parameters["columnsCount"] = cols;
            parameters["rowsCount"]    = rows;
            strategy = StrategyFactory.Create("FullGrid");
        }

        var definition = new ObjectDefinition
        {
            GeometryModuleId = "SurfacePlate",
            Parameters       = parameters
        };

        return (strategy, definition);
    }

    private (IMeasurementStrategy, ObjectDefinition) BuildParallelWaysDefinition()
    {
        if (_railItems.Count < 2)
            throw new InvalidOperationException("Parallel Ways requires at least 2 rails.");
        if (_taskItems.Count == 0)
            throw new InvalidOperationException("At least one task is required.");

        int refIdx = _railItems.IndexOf(_railItems.FirstOrDefault(r => r.IsReference) ?? _railItems[0]);

        var parameters = new Dictionary<string, object>
        {
            ["orientation"]        = ((WaysOrientation)WaysOrientationIndex).ToString(),
            ["referenceRailIndex"] = refIdx,
            ["rails"]              = _railItems.Select(r => r.ToDefinition()).ToList(),
            ["tasks"]              = _taskItems.Select(t => t.ToTask()).ToList(),
            ["driftCorrection"]    = ((DriftCorrectionMethod)DriftCorrectionIndex).ToString(),
            ["solverMode"]         = ((SolverMode)SolverModeIndex).ToString()
        };

        var definition = new ObjectDefinition
        {
            GeometryModuleId = "ParallelWays",
            Parameters       = parameters
        };

        var strategy = StrategyFactory.Create("ParallelWays");
        return (strategy, definition);
    }
}
