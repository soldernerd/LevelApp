using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Calibration;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.UI.Calibration;

public enum VerdictSeverity { Good, Warning, Bad }

/// <summary>A determinate or indeterminate progress panel ("Hold still…").</summary>
public sealed record WizardProgress(double? Value, string Title, string? Subtitle = null, string? Warning = null);

public sealed record ResultRow(string Label, string Before, string After, string Delta);

public sealed record ResultTable(string Title, IReadOnlyList<ResultRow> Rows);

/// <summary>One sensor's computed result with a plain-language verdict.</summary>
public sealed record VerdictCard(string Sensor, VerdictSeverity Severity, string Headline, string Detail, string Verdict);

/// <summary>What the instrument diagram shows (as the phone app's <c>DiagramPose</c>).</summary>
public enum DiagramPose
{
    /// <summary>On a level plate, the mark facing you.</summary>
    Level,

    /// <summary>The same, turned by 180° (the mark now faces away).</summary>
    Rotated,

    /// <summary>The plate raised at the back end (the end without the red mark) by a shim: the front is low, reads negative.</summary>
    Shim,

    /// <summary>The plate raised at the front end (the red mark): reads positive.</summary>
    ShimFront,
}

public sealed record WizardAction(string Label, ICommand Command, bool Primary = false, bool Enabled = true);

/// <summary>
/// Everything one screen of a wizard shows. The wizard view-models rebuild it from
/// the wizard's state; one generic <c>WizardPageView</c> renders it, so the three
/// wizards read and flow like the phone app's screens without three XAML copies.
/// </summary>
public sealed record WizardPage(
    string Title,
    string? StepText = null,
    string? Intro = null,
    IReadOnlyList<string>? Instructions = null,
    bool ShowSensorSelector = false,
    bool ShowShimInputs = false,
    bool ShowPhaseModeSelector = false,
    string? ModeHint = null,
    string? SummaryTitle = null,
    IReadOnlyList<string>? SummaryLines = null,
    string? ShimHint = null,
    WizardProgress? Progress = null,
    bool Busy = false,
    string? BusyText = null,
    string? Headline = null,
    IReadOnlyList<ResultTable>? Tables = null,
    IReadOnlyList<VerdictCard>? Verdicts = null,
    string? Footer = null,
    string? Error = null,
    bool ShowServiceCard = false,
    IReadOnlyList<WizardAction>? Actions = null,
    DiagramPose? Pose = null,
    string? PoseLabel = null)
{
    private static readonly Regex StepPattern = new(@"^Step (\d+) of (\d+) \u2014 (.*)$", RegexOptions.Compiled);

    private Match? StepMatch => StepText is null ? null : StepPattern.Match(StepText);

    /// <summary>"Step 2 of 3 — Title" split up for the step header (the phone app's <c>StepHeader</c>).</summary>
    public bool HasStep => StepMatch is { Success: true };
    public int StepNumber => StepMatch is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    public int StepTotal  => StepMatch is { Success: true } m ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
    public string StepTitle => StepMatch is { Success: true } m ? m.Groups[3].Value : StepText ?? string.Empty;

    /// <summary>"STEP 2 OF 3".</summary>
    public string StepCaption => HasStep ? $"STEP {StepNumber} OF {StepTotal}" : string.Empty;

    /// <summary>Progress line of the step header, 0..1.</summary>
    public double StepFraction => HasStep && StepTotal > 0 ? (double)StepNumber / StepTotal : 0;

    /// <summary>The instructions, numbered "1.  ..." (a bulleted list in the phone app).</summary>
    public IReadOnlyList<string> NumberedInstructions =>
        Instructions is null
            ? []
            : [.. Instructions.Select((text, i) => $"{i + 1}.  {text}")];

    /// <summary>Percentage text inside the progress ring.</summary>
    public string ProgressPercentText =>
        Progress is { Value: { } v } ? $"{Math.Round(Math.Clamp(v, 0, 1) * 100):0}%" : string.Empty;
}

/// <summary>Display formatting shared by the wizards. Signs use a real minus (U+2212).</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Signed(double? v, int decimals = 2, string unit = "")
    {
        if (v is not { } d || !double.IsFinite(d)) return "—";
        string s = (d < 0 ? "−" : "+") + Math.Abs(d).ToString("F" + decimals, Inv);
        return unit.Length == 0 ? s : $"{s} {unit}";
    }

    public static string CentiDegrees(long cdeg) => Signed(cdeg / 100.0, 2) + "°";

    public static string Zero(long zeroPpm, long kMicro) => Signed((double)zeroPpm / kMicro, 4) + " mm/m";

    public static string Num(double v) => v.ToString("0.###", Inv);
}

/// <summary>
/// Base of the wizard view-models: owns the page, keeps it in step with the wizard's
/// state and with service mode, and marshals to the UI thread.
/// </summary>
public abstract partial class WizardViewModelBase : ObservableObject, IDisposable
{
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    protected WizardViewModelBase(LeveltronicSession session)
    {
        Session = session;
        session.Feed.Updated += OnFeedUpdated;
    }

    protected LeveltronicSession Session { get; }

    protected bool ServiceMode => Session.Feed.State.ServiceMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressIndeterminate))]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    private WizardPage _page = new("");

    public bool ProgressIndeterminate => Page.Progress is { Value: null };
    public double ProgressValue => Page.Progress?.Value ?? 0;

    /// <summary>The operator finished ("Done") — go back to the hub.</summary>
    public event EventHandler? CloseRequested;

    protected abstract WizardPage BuildPage();

    /// <summary>The current sensor selection (bit0 S1, bit1 S2).</summary>
    protected abstract int CurrentMask { get; }

    protected abstract void ApplyMask(int mask);

    public bool MaskS1 { get => CurrentMask == SensorMask.S1; set { if (value) ApplyMask(SensorMask.S1); } }
    public bool MaskS2 { get => CurrentMask == SensorMask.S2; set { if (value) ApplyMask(SensorMask.S2); } }
    public bool MaskBoth { get => CurrentMask == SensorMask.Both; set { if (value) ApplyMask(SensorMask.Both); } }

    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMilliseconds(100);
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _refreshPending;
    private bool _disposed;

    /// <summary>
    /// Rebuilds the page. A wizard reports progress up to some forty times a second; the page is rebuilt
    /// at most ten times a second (a trailing rebuild follows, so the last state is never lost). Faster
    /// than that only makes the page flicker.
    /// </summary>
    protected void Refresh() => Post(() =>
    {
        var since = DateTime.UtcNow - _lastRefresh;
        if (since >= MinRefreshInterval)
        {
            RefreshNow();
            return;
        }

        if (_refreshPending) return;
        _refreshPending = true;
        _ = Task.Delay(MinRefreshInterval - since).ContinueWith(_ => Post(() =>
        {
            _refreshPending = false;
            RefreshNow();
        }), TaskScheduler.Default);
    });

    private void RefreshNow()
    {
        if (_disposed) return;
        _lastRefresh = DateTime.UtcNow;

        var next = BuildPage();

        // The buttons must stay the same objects while nothing about them changed: a page that is rebuilt
        // several times a second (the instrument's live feed, the measurement progress) replaces them under
        // the pointer, and a click whose press and release land on different buttons is lost.
        if (Page.Actions is { } before && next.Actions is { } after && SameActions(before, after))
            next = next with { Actions = before };

        Page = next;
        OnPropertyChanged(nameof(MaskS1));
        OnPropertyChanged(nameof(MaskS2));
        OnPropertyChanged(nameof(MaskBoth));
        OnPageRefreshed();
    }

    /// <summary>Lets a wizard raise change notifications for its own bound properties.</summary>
    protected virtual void OnPageRefreshed() { }

    protected void RequestClose() => Post(() => CloseRequested?.Invoke(this, EventArgs.Empty));

    private static bool SameActions(IReadOnlyList<WizardAction> a, IReadOnlyList<WizardAction> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Label != b[i].Label || a[i].Primary != b[i].Primary || a[i].Enabled != b[i].Enabled)
                return false;
        return true;
    }

    private bool? _lastServiceMode;

    // The feed arrives about six times a second. The page only depends on it through service mode
    // (what can be written, whether the service card shows), so only a change of that rebuilds it.
    private void OnFeedUpdated(object? sender, InstrumentSnapshot e)
    {
        bool service = e.ServiceMode;
        if (_lastServiceMode == service) return;
        _lastServiceMode = service;
        Refresh();
    }

    protected void Post(Action a)
    {
        if (_ui is null || ReferenceEquals(SynchronizationContext.Current, _ui)) a();
        else _ui.Post(_ => a(), null);
    }

    protected static WizardAction Act(string label, Func<Task> run, bool primary = false, bool enabled = true) =>
        new(label, new AsyncRelayCommand(run), primary, enabled);

    protected static WizardAction Act(string label, Action run, bool primary = false, bool enabled = true) =>
        new(label, new RelayCommand(run), primary, enabled);

    public virtual void Dispose()
    {
        _disposed = true;
        Session.Feed.Updated -= OnFeedUpdated;
    }
}
