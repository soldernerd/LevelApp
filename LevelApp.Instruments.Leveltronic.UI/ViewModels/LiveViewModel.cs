using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.ViewModels;

/// <summary>
/// The live screen (port of the phone app's instrument screen): S1, S2 and Diff with their quality
/// state, battery and on-board temperature, "Zero" (a precision measurement whose value of S1, S2,
/// Diff or all three is remembered per instrument and subtracted from the shown reading: the app-side
/// "zero the current position", not a calibration) and a stand-alone "Precision" measurement, both
/// reported in an inline panel. Holds no WinUI types.
/// </summary>
public sealed partial class LiveViewModel : SessionViewModelBase
{
    private enum Purpose { Zero, Measure }

    private readonly ILeveltronicPreferences _prefs;
    private LiveFeed? _feed;
    private InstrumentSnapshot _snapshot = new();
    private double? _tareS1;
    private double? _tareS2;
    private double? _tareDiff;
    private string[] _zeroTargets = [];
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

    [ObservableProperty] private string _s1Text = LiveFormat.Placeholder(2);
    [ObservableProperty] private string _s2Text = LiveFormat.Placeholder(2);
    [ObservableProperty] private string _diffText = LiveFormat.Placeholder(2);
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

    // ---- zero ----

    [ObservableProperty] private bool _isZeroed;
    [ObservableProperty] private string _zeroButtonText = "Zero ▾";
    [ObservableProperty] private string _s1SubLabel = "";
    [ObservableProperty] private string _s2SubLabel = "";
    [ObservableProperty] private string _diffSubLabel = "S1 − S2";
    [ObservableProperty] private string _zeroNote = "";

    // ---- measurement panel ----

    /// <summary>The Zero / Precision buttons are usable: a session is attached and no measurement panel is open.</summary>
    [ObservableProperty] private bool _canStart;

    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private bool _isPanelRunning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSingleResult))]
    [NotifyPropertyChangedFor(nameof(ShowComparison))]
    private bool _isPanelDone;
    [ObservableProperty] private bool _isPanelFailed;
    [ObservableProperty] private string _panelHeading = "Precision measurement";
    [ObservableProperty] private string _panelSubtitle = "Starting";
    [ObservableProperty] private string _panelWarning = "";
    [ObservableProperty] private bool _hasPanelWarning;
    [ObservableProperty] private string _panelMessage = "";

    /// <summary>Progress 0..100 of the running measurement.</summary>
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isProgressIndeterminate = true;

    // The result before the current one, kept while the panel stays open so that "Measure again"
    // can show both side by side with their difference.
    private readonly record struct Result(double S1, double S2, double Diff);
    private Result? _last;
    private Result? _previous;

    /// <summary>A previous result is shown next to the new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSingleResult))]
    [NotifyPropertyChangedFor(nameof(ShowComparison))]
    private bool _hasPrevious;

    public bool ShowSingleResult => IsPanelDone && !HasPrevious;
    public bool ShowComparison => IsPanelDone && HasPrevious;

    [ObservableProperty] private string _previousS1 = "";
    [ObservableProperty] private string _previousS2 = "";
    [ObservableProperty] private string _previousDiff = "";
    [ObservableProperty] private string _deltaS1 = "";
    [ObservableProperty] private string _deltaS2 = "";
    [ObservableProperty] private string _deltaDiff = "";

    [ObservableProperty] private string _resultS1 = "";
    [ObservableProperty] private string _resultS2 = "";
    [ObservableProperty] private string _resultDiff = "";
    [ObservableProperty] private string _resultDiffNote = "";

    // ---- session plumbing ----

    protected override void OnAttached(LeveltronicSession session)
    {
        _attached = true;
        _tareS1 = _prefs.GetTare(session.Serial, LeveltronicProvider.ChannelS1);
        _tareS2 = _prefs.GetTare(session.Serial, LeveltronicProvider.ChannelS2);
        _tareDiff = _prefs.GetTare(session.Serial, LeveltronicProvider.ChannelDiff);
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
        S1Text = show1 ? LiveFormat.Micro(LiveFormat.ApplyTare(live!.TiltS1, _tareS1)) : LiveFormat.Placeholder(2);
        S2Text = show1 ? LiveFormat.Micro(LiveFormat.ApplyTare(live!.TiltS2, _tareS2)) : LiveFormat.Placeholder(2);
        double? shownDiff = LiveFormat.ApplyTare(live?.TiltDiff, _tareDiff);
        DiffText = has && shownDiff is not null ? LiveFormat.Micro(shownDiff) : LiveFormat.Placeholder(2);

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
        IsZeroed = _tareS1 is not null || _tareS2 is not null || _tareDiff is not null;
        ZeroButtonText = IsZeroed ? "Re-zero ▾" : "Zero ▾";
        S1SubLabel = _tareS1 is null ? "" : "zeroed";
        S2SubLabel = _tareS2 is null ? "" : "zeroed";
        DiffSubLabel = _tareDiff is null ? "S1 − S2" : "zeroed";

        var parts = new List<string>(3);
        if (_tareS1 is not null) parts.Add($"S1 {LiveFormat.Micro(_tareS1)}");
        if (_tareS2 is not null) parts.Add($"S2 {LiveFormat.Micro(_tareS2)}");
        if (_tareDiff is not null) parts.Add($"Diff {LiveFormat.Micro(_tareDiff)}");
        ZeroNote = parts.Count == 0 ? "" : $"Zeroed: {string.Join(", ", parts)} µm/m";
    }

    /// <summary>Remembers the zero offsets (mm/m) of the given channels; null clears one.</summary>
    private void SetTares(bool s1, bool s2, bool diff, double? v1, double? v2, double? vDiff)
    {
        var serial = Session?.Serial;
        if (s1) { _tareS1 = v1; if (serial is not null) _prefs.SetTare(serial, LeveltronicProvider.ChannelS1, v1); }
        if (s2) { _tareS2 = v2; if (serial is not null) _prefs.SetTare(serial, LeveltronicProvider.ChannelS2, v2); }
        if (diff) { _tareDiff = vDiff; if (serial is not null) _prefs.SetTare(serial, LeveltronicProvider.ChannelDiff, vDiff); }
        ApplySnapshot(_snapshot);
    }

    private string ZeroHeading() => _zeroTargets.Length > 1 ? "Zero S1, S2 and Diff"
        : _zeroTargets.FirstOrDefault() switch
        {
            LeveltronicProvider.ChannelS1 => "Zero S1",
            LeveltronicProvider.ChannelS2 => "Zero S2",
            _ => "Zero Diff",
        };

    private void UpdateCanStart() => CanStart = _attached && !IsPanelOpen;

    // ---- commands ----

    /// <summary>Clears all zero offsets (S1, S2 and Diff).</summary>
    [RelayCommand]
    private void ClearZero() => SetTares(true, true, true, null, null, null);

    /// <summary>
    /// Takes a precision measurement and remembers its value as the new zero of the given channel:
    /// "s1", "s2", "diff", or "all" for the three at once (anything else is Diff).
    /// </summary>
    [RelayCommand]
    private Task ZeroAsync(string? channel)
    {
        _zeroTargets = channel switch
        {
            "all" => [LeveltronicProvider.ChannelS1, LeveltronicProvider.ChannelS2, LeveltronicProvider.ChannelDiff],
            LeveltronicProvider.ChannelS1 => [LeveltronicProvider.ChannelS1],
            LeveltronicProvider.ChannelS2 => [LeveltronicProvider.ChannelS2],
            _ => [LeveltronicProvider.ChannelDiff],
        };
        return RunPrecisionAsync(Purpose.Zero);
    }

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

        // Measuring again from a shown result: that result becomes the "previous" one.
        _previous = purpose == Purpose.Measure && IsPanelOpen ? _last : null;

        PanelHeading = purpose == Purpose.Zero ? ZeroHeading() : "Precision measurement";
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
                bool s1 = _zeroTargets.Contains(LeveltronicProvider.ChannelS1);
                bool s2 = _zeroTargets.Contains(LeveltronicProvider.ChannelS2);
                bool diff = _zeroTargets.Contains(LeveltronicProvider.ChannelDiff);
                SetTares(s1, s2, diff, result.TiltS1, result.TiltS2, result.TiltDiff);
                ClosePanelCore();
            }
            else
            {
                _resultDiffRaw = result.TiltDiff;
                var current = new Result(result.TiltS1, result.TiltS2, result.TiltDiff);
                ResultS1 = LiveFormat.Micro(LiveFormat.ApplyTare(result.TiltS1, _tareS1));
                ResultS2 = LiveFormat.Micro(LiveFormat.ApplyTare(result.TiltS2, _tareS2));
                ResultDiff = LiveFormat.Micro(LiveFormat.ApplyTare(_resultDiffRaw, _tareDiff));
                ResultDiffNote = _tareDiff is not null ? "zeroed" : "";
                ShowComparisonWith(current);
                _last = current;
                SetPanel(running: false, done: true, failed: false);
            }
        }
        catch (PrecisionCancelledException)
        {
            // cancelled via the button or by a detach; the panel is already closed
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_run, run)) return;
            _run = null;
            // Say what went wrong (the instrument's refusal or the link problem), not just that it did.
            string reason = ex is ApiException api ? api.UserMessage : ex.Message;
            PanelMessage = $"Could not complete the measurement: {reason}";
            SetPanel(running: false, done: false, failed: true);
        }
    }

    /// <summary>Fills the previous / difference texts when there is a previous result.</summary>
    private void ShowComparisonWith(Result current)
    {
        if (_previous is not { } old)
        {
            HasPrevious = false;
            return;
        }

        PreviousS1   = LiveFormat.Micro(LiveFormat.ApplyTare(old.S1, _tareS1));
        PreviousS2   = LiveFormat.Micro(LiveFormat.ApplyTare(old.S2, _tareS2));
        PreviousDiff = LiveFormat.Micro(LiveFormat.ApplyTare(old.Diff, _tareDiff));
        DeltaS1      = LiveFormat.Micro(current.S1 - old.S1);
        DeltaS2      = LiveFormat.Micro(current.S2 - old.S2);
        DeltaDiff    = LiveFormat.Micro(current.Diff - old.Diff);
        HasPrevious  = true;
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
        _last = null;
        _previous = null;
        HasPrevious = false;
        IsPanelRunning = false;
        IsPanelDone = false;
        IsPanelFailed = false;
        IsPanelOpen = false;
        UpdateCanStart();
    }
}
