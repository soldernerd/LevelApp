using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.App.Navigation;
using LevelApp.App.Services;
using LevelApp.Core.Geometry;
using LevelApp.Core.Geometry.ParallelWays;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;
using LevelApp.Instruments.Manual;
using Microsoft.UI.Xaml;
using InfoBarSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity;

namespace LevelApp.App.ViewModels;

public sealed partial class MeasurementViewModel : ViewModelBase
{
    private readonly INavigationService              _navigation;
    private readonly MainViewModel                   _mainViewModel;
    private readonly ParallelWaysCalculator          _pwCalculator;
    private readonly ILocalisationService            _loc;
    private readonly IEnumerable<IInstrumentPlugin>  _plugins;
    private readonly IDeviceRegistry                 _registry;

    // Set in Initialize() once the session's instrumentId is known.
    private IInstrumentProvider? _provider;

    private Project               _project    = null!;
    private MeasurementSession    _session    = null!;
    private List<MeasurementStep> _steps      = [];
    private ObjectDefinition      _definition = null!;

    public MeasurementViewModel(INavigationService navigation, MainViewModel mainViewModel,
                                ParallelWaysCalculator pwCalculator, ILocalisationService loc,
                                IEnumerable<IInstrumentPlugin> plugins, IDeviceRegistry registry)
    {
        _navigation    = navigation;
        _mainViewModel = mainViewModel;
        _pwCalculator  = pwCalculator;
        _loc           = loc;
        _plugins       = plugins;
        _registry      = registry;
    }

    private void OnConnectionStateChanged(object? sender, InstrumentConnectionState state)
    {
        OnPropertyChanged(nameof(ShowConnectionWarning));
        OnPropertyChanged(nameof(ConnectionSeverity));
        OnPropertyChanged(nameof(ConnectionStatusMessage));
        AcceptReadingCommand.NotifyCanExecuteChanged();
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

        // Resolve the active provider from the plugin matching the session's instrumentId.
        var plugin = _plugins.FirstOrDefault(p => p.PluginId == _session.InstrumentId)
                  ?? _plugins.First();
        var device = _registry.GetPreferredDevice(plugin.PluginId)
                  ?? _registry.GetKnownDevices(plugin.PluginId).FirstOrDefault()
                  ?? ManualEntryProvider.BuiltInDevice;

        // Detach from previous provider (if this VM is re-used).
        if (_provider is not null)
            _provider.ConnectionStateChanged -= OnConnectionStateChanged;

        _provider = plugin.CreateProvider(device);
        _provider.ConnectionStateChanged += OnConnectionStateChanged;

        IsParallelWays = _definition.GeometryModuleId == "ParallelWays";

        if (IsParallelWays)
        {
            GridColumns = 0;
            GridRows    = 0;
            WidthMm     = 0.0;
            HeightMm    = 0.0;
        }
        else
        {
            GridColumns = _definition.Parameters.TryGetValue("columnsCount", out var c) ? Convert.ToInt32(c) : 0;
            GridRows    = _definition.Parameters.TryGetValue("rowsCount",    out var r) ? Convert.ToInt32(r) : 0;
            WidthMm     = _definition.Parameters.TryGetValue("widthMm",      out var w) ? Convert.ToDouble(w) : 0.0;
            HeightMm    = _definition.Parameters.TryGetValue("heightMm",     out var h) ? Convert.ToDouble(h) : 0.0;
        }

        CurrentStepIndex = 0;
        Reading          = double.NaN;
        IsCalculating    = false;

        OnPropertyChanged(string.Empty); // refresh all computed properties at once
    }

    // ── Grid geometry (set during Initialise) ─────────────────────────────────

    public bool             IsParallelWays { get; private set; }
    public int              GridColumns    { get; private set; }
    public int              GridRows       { get; private set; }
    public double           WidthMm        { get; private set; }
    public double           HeightMm       { get; private set; }
    public ObjectDefinition Definition     => _definition;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(OrientationArrow))]
    [NotifyPropertyChangedFor(nameof(CurrentInstructionText))]
    private int _currentStepIndex;

    /// <summary>
    /// The operator's reading in µm/m (as entered by the user).
    /// <see cref="double.NaN"/> means "not yet entered"
    /// — NumberBox renders NaN as an empty/placeholder field.
    /// Converted to mm/m (÷ 1000) before being stored on the step.
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
    public string ProgressText  => TotalSteps > 0
        ? string.Format(_loc.Get("Measurement_ProgressTitle.Text"), CurrentStepIndex + 1, TotalSteps)
        : string.Empty;
    public int    ProgressPercent => TotalSteps > 0 ? CurrentStepIndex * 100 / TotalSteps : 0;

    public string OrientationArrow => CurrentStep?.Orientation switch
    {
        Orientation.North => "↑",
        Orientation.South => "↓",
        Orientation.East  => "→",
        Orientation.West  => "←",
        _ => "?"
    };

    public string CurrentInstructionText => CurrentStep?.InstructionText ?? string.Empty;

    public bool       IsInputEnabled       => !IsCalculating;
    public Visibility CalculatingVisibility => IsCalculating ? Visibility.Visible : Visibility.Collapsed;

    // ── Connection status ─────────────────────────────────────────────────────

    private InstrumentConnectionState ProviderState =>
        _provider?.ConnectionState ?? InstrumentConnectionState.Disconnected;

    public bool ShowConnectionWarning =>
        ProviderState != InstrumentConnectionState.Connected;

    public InfoBarSeverity ConnectionSeverity =>
        ProviderState == InstrumentConnectionState.Error
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Warning;

    public string ConnectionStatusMessage =>
        ProviderState switch
        {
            InstrumentConnectionState.Disconnected => "Instrument disconnected",
            InstrumentConnectionState.Connecting   => "Connecting to instrument…",
            InstrumentConnectionState.Degraded     => "Instrument signal degraded",
            InstrumentConnectionState.Error        => "Instrument connection error",
            _                                      => string.Empty
        };

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

        // Reading is entered by the user in µm/m; convert to mm/m for storage
        _steps[CurrentStepIndex].Reading = Reading / 1000.0;
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

            if (IsParallelWays)
            {
                var pwParams = new CalculationParameters();
                var pwResult = await Task.Run(
                    () => _pwCalculator.Calculate(round.Steps, definition, pwParams));
                round.ParallelWaysResult = pwResult;
            }
            else
            {
                var parameters = round.CalculationParameters ?? new CalculationParameters();
                var strategy   = StrategyFactory.Create(_session.StrategyId);
                var calculator = CalculatorFactory.Create(parameters.MethodId, strategy);

                var result = await Task.Run(() => calculator.Calculate(round.Steps, definition, parameters));
                round.Result = result;
            }

            round.CompletedAt   = DateTime.UtcNow;
            _project.ModifiedAt = DateTime.UtcNow;

            IsCalculating = false;
            _navigation.NavigateTo(PageKey.Results, new ResultsArgs(_project, _session));
        }
    }

    private bool CanAcceptReading()
    {
        var state = ProviderState;
        bool connectionOk = state != InstrumentConnectionState.Disconnected
                         && state != InstrumentConnectionState.Error;
        return !IsCalculating && !double.IsNaN(Reading) && connectionOk;
    }
}
