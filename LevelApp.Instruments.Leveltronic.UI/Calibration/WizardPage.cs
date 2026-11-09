using System.Globalization;
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
    IReadOnlyList<WizardAction>? Actions = null);

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

    protected void Refresh() => Post(() =>
    {
        Page = BuildPage();
        OnPropertyChanged(nameof(MaskS1));
        OnPropertyChanged(nameof(MaskS2));
        OnPropertyChanged(nameof(MaskBoth));
        OnPageRefreshed();
    });

    /// <summary>Lets a wizard raise change notifications for its own bound properties.</summary>
    protected virtual void OnPageRefreshed() { }

    protected void RequestClose() => Post(() => CloseRequested?.Invoke(this, EventArgs.Empty));

    private void OnFeedUpdated(object? sender, InstrumentSnapshot e) => Refresh();

    protected void Post(Action a)
    {
        if (_ui is null || ReferenceEquals(SynchronizationContext.Current, _ui)) a();
        else _ui.Post(_ => a(), null);
    }

    protected static WizardAction Act(string label, Func<Task> run, bool primary = false, bool enabled = true) =>
        new(label, new AsyncRelayCommand(run), primary, enabled);

    protected static WizardAction Act(string label, Action run, bool primary = false, bool enabled = true) =>
        new(label, new RelayCommand(run), primary, enabled);

    public virtual void Dispose() => Session.Feed.Updated -= OnFeedUpdated;
}
