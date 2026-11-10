using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>
/// The live screen (port of the phone app's instrument screen): S1, S2 and Diff with their quality
/// state, battery and on-board temperature, "Zero Diff" (a precision measurement whose Diff is
/// remembered per instrument and subtracted from the shown Diff) and a stand-alone "Precision"
/// measurement, both reported in an inline panel. Holds no WinUI types.
/// </summary>
public sealed partial class LiveViewModel : SessionViewModelBase
{
    private enum Purpose { Zero, Measure }

    private readonly ILeveltronicPreferences _prefs;
    private LiveFeed? _feed;
    private InstrumentSnapshot _snapshot = new();
    private double? _tare;
    private bool _attached;
    private PrecisionRun? _run;
    private Purpose _purpose;
    private double _resultDiffRaw;

    public LiveViewModel(ILeveltronicPreferences preferences)
    {
        _prefs = preferences;
        ApplySnapshot(_snapshot);
    }

    // ---- readings ----

    [ObservableProperty] private string _s1Text = LiveFormat.Placeholder();
    [ObservableProperty] private string _s2Text = LiveFormat.Placeholder();
    [ObservableProperty] private string _diffText = LiveFormat.Placeholder();
    [ObservableProperty] private ReadingState _s1State = ReadingState.None;
    [ObservableProperty] private ReadingState _s2State = ReadingState.None;
    [ObservableProperty] private ReadingState _diffState = ReadingState.None;

    /// <summary>A valid reading exists (the demodulation is delivering).</summary>
    [ObservableProperty] private bool _hasReading;

    /// <summary>The tilt demodulation is stopped (shows a notice).</summary>
    [ObservableProperty] private bool _demodStopped;

    /// <summary>1.0 while connected, dimmed while the link is gone (last values stay visible).</summary>
    [ObservableProperty] private double _displayOpacity = 0.4;

    // ---- top bar ----

    [ObservableProperty] private bool _hasTemperature;
    [ObservableProperty] private string _temperatureText = "—";

    [ObservableProperty] private bool _hasBattery;

    /// <summary>State of charge in percent, or -1 while unknown.</summary>
    [ObservableProperty] private int _batteryPercent = -1;
    [ObservableProperty] private string _batteryText = "—";
    [ObservableProperty] private bool _charging;
    [ObservableProperty] private BatteryState _batteryChargeState = BatteryState.Unknown;

    // ---- zero diff ----

    [ObservableProperty] private bool _isZeroed;
    [ObservableProperty] private string _zeroButtonText = "Zero Diff";
    [ObservableProperty] private string _diffSubLabel = "S1 − S2";
    [ObservableProperty] private string _zeroNote = "";

    // ---- measurement panel ----

    /// <summary>The Zero / Precision buttons are usable: a session is attached and no measurement panel is open.</summary>
    [ObservableProperty] private bool _canStart;

    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private bool _isPanelRunning;
    [ObservableProperty] private bool _isPanelDone;
    [ObservableProperty] private bool _isPanelFailed;
    [ObservableProperty] private string _panelHeading = "Precision measurement";
    [ObservableProperty] private string _panelSubtitle = "Starting";
    [ObservableProperty] private string _panelWarning = "";
    [ObservableProperty] private bool _hasPanelWarning;
    [ObservableProperty] private string _panelMessage = "";

    /// <summary>Progress 0..100 of the running measurement.</summary>
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isProgressIndeterminate = true;

    [ObservableProperty] private string _resultS1 = "";
    [ObservableProperty] private string _resultS2 = "";
    [ObservableProperty] private string _resultDiff = "";
    [ObservableProperty] private string _resultDiffNote = "";

    // ---- session plumbing ----

    protected override void OnAttached(LeveltronicSession session)
    {
        _attached = true;
        _tare = _prefs.GetTare(session.Serial);
        _feed = session.Feed;
        _feed.Updated += OnFeedUpdated;
        DisplayOpacity = 1.0;
        UpdateCanStart();
        ApplySnapshot(_feed.State);
    }

    protected override void OnDetached()
    {
        if (_feed is not null)
        {
            _feed.Updated -= OnFeedUpdated;
            _feed = null;
        }
        _attached = false;
        DisplayOpacity = 0.4;

        var run = _run;
        _run = null;
        if (run is not null)
        {
            // Fire and forget: the link is going away, so the device cancel is best effort.
            _ = CancelQuietlyAsync(run);
        }
        ClosePanelCore();
    }

    private static async Task CancelQuietlyAsync(PrecisionRun run)
    {
        try { await run.CancelAsync().ConfigureAwait(false); }
        catch { /* best effort */ }
    }

    private void OnFeedUpdated(object? sender, InstrumentSnapshot snapshot) => Post(() =>
    {
        if (_attached) ApplySnapshot(snapshot);
    });

    private void ApplySnapshot(InstrumentSnapshot s)
    {
        _snapshot = s;
        var live = s.Live;
        bool has = s.HasReading;

        bool show1 = has && live is not null;
        S1Text = show1 ? LiveFormat.Tilt(live!.TiltS1, 5) : LiveFormat.Placeholder();
        S2Text = show1 ? LiveFormat.Tilt(live!.TiltS2, 5) : LiveFormat.Placeholder();
        double? shownDiff = LiveFormat.ApplyTare(live?.TiltDiff, _tare);
        DiffText = has && shownDiff is not null ? LiveFormat.Tilt(shownDiff, 5) : LiveFormat.Placeholder();

        bool s1Doubtful = live?.S1Doubtful ?? false;
        bool s2Doubtful = live?.S2Doubtful ?? false;
        S1State = LiveFormat.StateOf(show1, s1Doubtful);
        S2State = LiveFormat.StateOf(show1, s2Doubtful);
        DiffState = LiveFormat.StateOf(has && shownDiff is not null, s1Doubtful || s2Doubtful);

        HasReading = has;
        DemodStopped = !s.DemodRunning;

        HasTemperature = s.Env is not null;
        TemperatureText = LiveFormat.Signed(s.Env?.OnboardTempC, 1, "°C");

        var st = s.Status;
        HasBattery = st is not null;
        BatteryPercent = st is null ? -1 : Math.Clamp(st.BatterySocPct, 0, 100);
        BatteryText = LiveFormat.Percent(st is null ? null : BatteryPercent);
        Charging = st?.Charging ?? false;
        BatteryChargeState = st?.Battery ?? BatteryState.Unknown;

        UpdateZeroTexts();
    }

    private void UpdateZeroTexts()
    {
        IsZeroed = _tare is not null;
        ZeroButtonText = _tare is null ? "Zero Diff" : "Re-zero Diff";
        DiffSubLabel = _tare is null ? "S1 − S2" : "zeroed";
        ZeroNote = _tare is null ? "" : $"Diff is zeroed at {LiveFormat.Tilt(_tare)} mm/m";
    }

    private void SetTare(double? value)
    {
        _tare = value;
        var session = Session;
        if (session is not null) _prefs.SetTare(session.Serial, value);
        ApplySnapshot(_snapshot);
    }

    private void UpdateCanStart() => CanStart = _attached && !IsPanelOpen;

    // ---- commands ----

    /// <summary>Clears the Zero Diff offset.</summary>
    [RelayCommand]
    private void ClearZero() => SetTare(null);

    /// <summary>Takes a precision measurement and remembers its Diff as the new zero.</summary>
    [RelayCommand]
    private Task ZeroDiffAsync() => RunPrecisionAsync(Purpose.Zero);

    /// <summary>Takes a precision measurement and shows its result in the panel.</summary>
    [RelayCommand]
    private Task PrecisionAsync() => RunPrecisionAsync(Purpose.Measure);

    /// <summary>Repeats the measurement of the open panel (after a failure, or "Measure again").</summary>
    [RelayCommand]
    private Task MeasureAgainAsync() => RunPrecisionAsync(_purpose);

    /// <summary>Cancels a running measurement and closes the panel.</summary>
    [RelayCommand]
    private async Task CancelPrecisionAsync()
    {
        var run = _run;
        _run = null;
        ClosePanelCore();
        if (run is not null)
        {
            try { await run.CancelAsync(); }
            catch { /* best effort */ }
        }
    }

    /// <summary>Closes the panel after a result or a failure.</summary>
    [RelayCommand]
    private void ClosePanel() => ClosePanelCore();

    private async Task RunPrecisionAsync(Purpose purpose)
    {
        var session = Session;
        if (session is null || _run is not null) return;

        var run = new PrecisionRun(session.Api);
        _run = run;
        _purpose = purpose;

        PanelHeading = purpose == Purpose.Zero ? "Zero Diff" : "Precision measurement";
        PanelSubtitle = "Starting";
        PanelWarning = "";
        HasPanelWarning = false;
        Progress = 0;
        IsProgressIndeterminate = true;
        SetPanel(running: true, done: false, failed: false);

        run.Progress += (_, s) => Post(() =>
        {
            if (!ReferenceEquals(_run, run) || s.Phase != PrecisionPhase.Running) return;
            Progress = LiveFormat.PrecisionPercent(s.Count, s.Target);
            IsProgressIndeterminate = s.Target <= 0 || s.Count <= 0;
            PanelSubtitle = LiveFormat.PrecisionSubtitle(s.Count, s.Target, s.ElapsedMs);
            HasPanelWarning = s.Disturbed;
            PanelWarning = s.Disturbed ? "The table is not quiet yet — wait for it to calm down." : "";
        });

        try
        {
            var result = await run.StartAsync();
            if (!ReferenceEquals(_run, run)) return; // cancelled / detached meanwhile
            _run = null;

            if (result.Failed)
            {
                PanelMessage = "No quiet 2 s window within 5 s. Keep the table still and try again.";
                SetPanel(running: false, done: false, failed: true);
            }
            else if (purpose == Purpose.Zero)
            {
                SetTare(result.TiltDiff);
                ClosePanelCore();
            }
            else
            {
                _resultDiffRaw = result.TiltDiff;
                ResultS1 = LiveFormat.Tilt(result.TiltS1, 5);
                ResultS2 = LiveFormat.Tilt(result.TiltS2, 5);
                ResultDiff = LiveFormat.Tilt(LiveFormat.ApplyTare(_resultDiffRaw, _tare), 5);
                ResultDiffNote = _tare is not null ? "zeroed" : "";
                SetPanel(running: false, done: true, failed: false);
            }
        }
        catch (PrecisionCancelledException)
        {
            // cancelled via the button or by a detach; the panel is already closed
        }
        catch (Exception)
        {
            if (!ReferenceEquals(_run, run)) return;
            _run = null;
            PanelMessage = "Could not complete the measurement.";
            SetPanel(running: false, done: false, failed: true);
        }
    }

    private void SetPanel(bool running, bool done, bool failed)
    {
        IsPanelRunning = running;
        IsPanelDone = done;
        IsPanelFailed = failed;
        IsPanelOpen = true;
        UpdateCanStart();
    }

    private void ClosePanelCore()
    {
        IsPanelRunning = false;
        IsPanelDone = false;
        IsPanelFailed = false;
        IsPanelOpen = false;
        UpdateCanStart();
    }
}
