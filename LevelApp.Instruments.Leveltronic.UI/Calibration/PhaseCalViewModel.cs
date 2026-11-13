using System.Globalization;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Calibration;

namespace LevelApp.Instruments.Leveltronic.UI.Calibration;

/// <summary>The phase calibration screens — the phone app's <c>PhaseCalScreen</c>.</summary>
public sealed class PhaseCalViewModel : WizardViewModelBase
{
    private const string Title = "Phase calibration";

    private readonly PhaseCalWizard _w;

    public PhaseCalViewModel(LeveltronicSession session, TimeProvider? time = null) : base(session)
    {
        _w = new PhaseCalWizard(session.Api, time);
        _w.StateChanged += (_, _) => Refresh();
        Refresh();
        _ = _w.RefreshValuesAsync();
    }

    protected override int CurrentMask => _w.State.Mask;
    protected override void ApplyMask(int mask) => _w.SelectSensors(mask);

    // ── Tilt mode (bound to the radio buttons of the selection page) ─────────

    public bool ModeBoth { get => _w.State.Mode == PhaseMode.Both; set { if (value) _w.SelectMode(PhaseMode.Both); } }
    public bool ModePositive { get => _w.State.Mode == PhaseMode.PositiveOnly; set { if (value) _w.SelectMode(PhaseMode.PositiveOnly); } }
    public bool ModeNegative { get => _w.State.Mode == PhaseMode.NegativeOnly; set { if (value) _w.SelectMode(PhaseMode.NegativeOnly); } }

    protected override void OnPageRefreshed()
    {
        OnPropertyChanged(nameof(ModeBoth));
        OnPropertyChanged(nameof(ModePositive));
        OnPropertyChanged(nameof(ModeNegative));
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    protected override WizardPage BuildPage()
    {
        var s = _w.State;
        bool service = ServiceMode;

        switch (s.Stage)
        {
            case PhaseStage.Select:
                return new WizardPage(Title,
                    Pose: DiagramPose.ShimFront,
                    Intro: "Each sensor's signal is rotated by a calibrated phase so that a tilt falls entirely in the " +
                           "in-phase part. To find it the app records the signal with a clear tilt: a positive tilt points " +
                           "along the phase, a negative one the opposite way. The size of the tilt does not matter — only " +
                           "that it is clearly larger than the noise (0.1 mm under a 1 m edge is plenty).",
                    ShowPhaseModeSelector: true,
                    ModeHint: s.Mode == PhaseMode.Both
                        ? "Most accurate: the sensor's zero offset cancels out."
                        : "One tilt is quicker, but the sensor's zero offset then adds a small error unless the tilt is large.",
                    ShowSensorSelector: true,
                    SummaryTitle: "Stored phase",
                    SummaryLines: [.. Enumerable.Range(0, 2).Select(i =>
                        $"S{i + 1}   {(s.Current is { } c ? Fmt.CentiDegrees(c[i]) : "—")}")],
                    ShowServiceCard: true,
                    Actions: [Act("Start", () => _w.BeginAsync(), primary: true, enabled: service)]);

            case PhaseStage.Ready:
            {
                bool positive = s.Tilt == PhaseTilt.Positive;
                return new WizardPage(Title, StepText: StepText(s),
                    Pose: positive ? DiagramPose.ShimFront : DiagramPose.Shim,
                    Instructions:
                    [
                        positive
                            ? "Raise the front (the end with the red mark) with a shim — at least 0.1 mm under a 1 m edge. " +
                              "The instrument should read a positive tilt."
                            : "Raise the back (the end opposite the red mark) with a shim — at least 0.1 mm under a 1 m edge. " +
                              "The instrument should read a negative tilt.",
                        "Let it settle, keep it still, then capture (about 8 seconds).",
                    ],
                    Actions:
                    [
                        s.Step == 1 ? Act("Back", () => _w.BackToStart()) : Act("Redo", () => _w.Redo()),
                        Act(positive ? "Capture positive" : "Capture negative", () => _w.CaptureAsync(), primary: true),
                    ]);
            }

            case PhaseStage.Capturing:
                return new WizardPage(Title, StepText: StepText(s),
                    Pose: s.Tilt == PhaseTilt.Positive ? DiagramPose.ShimFront : DiagramPose.Shim,
                    Progress: new WizardProgress(s.CaptureProgress, "Capturing… hold still", $"{s.Samples} batches"),
                    Actions: [Act("Cancel", () => _w.BackToStart())]);

            case PhaseStage.Review:
            {
                var cards = new List<VerdictCard>();
                bool canApply = false;
                for (int i = 0; i < 2; i++)
                {
                    if (!SensorMask.Has(s.Mask, i)) continue;
                    var e = s.PhaseEstimates[i];
                    canApply |= e is { InRange: true };
                    cards.Add(Card($"S{i + 1}", e, s.Current?[i]));
                }
                return new WizardPage(Title, StepText: $"Step {s.Steps + 1} of {s.Steps + 1} — Result",
                    Verdicts: cards,
                    ShowServiceCard: !service,
                    Actions:
                    [
                        Act("Redo", () => _w.Redo()),
                        Act("Apply", () => _w.ApplyAsync(), primary: true, enabled: service && canApply),
                    ]);
            }

            case PhaseStage.Applying:
                return new WizardPage(Title, Busy: true, BusyText: "Writing the phase…");

            case PhaseStage.Done:
            {
                var rows = new List<ResultRow>();
                for (int i = 0; i < 2; i++)
                {
                    if (!SensorMask.Has(s.Mask, i) || s.Before is null || s.After is null) continue;
                    rows.Add(new ResultRow($"S{i + 1}", Fmt.CentiDegrees(s.Before[i]), Fmt.CentiDegrees(s.After[i]),
                        Fmt.Signed((s.After[i] - s.Before[i]) / 100.0, 2) + "°"));
                }
                return new WizardPage(Title,
                    Headline: "Phase stored",
                    Tables: [new ResultTable("Phase", rows)],
                    Footer: "Next in the usual order: gain (sensitivity k), then the zero (flip) calibration.",
                    Actions:
                    [
                        Act("Again", () => _w.BackToStart()),
                        Act("Done", RequestClose, primary: true),
                    ]);
            }

            default: // Error
                return new WizardPage(Title,
                    Error: s.Message ?? "Something went wrong.",
                    ShowServiceCard: s.NeedsService,
                    Actions: [Act("Try again", () => _w.BackToStart(), primary: true)]);
        }
    }

    private static string StepText(PhaseCalState s) =>
        $"Step {s.Step} of {s.Steps} — {(s.Tilt == PhaseTilt.Positive ? "Positive tilt" : "Negative tilt")}";

    private static VerdictCard Card(string sensor, PhaseEstimate? e, long? storedCdeg)
    {
        if (e is null)
            return new VerdictCard(sensor, VerdictSeverity.Bad, "—", "",
                "No usable signal was seen. Use a larger shim and redo.");

        VerdictSeverity sev;
        string verdict;
        if (!e.InRange)
        {
            sev = VerdictSeverity.Bad;
            verdict = "The phase is outside ±180°. Check sensor cabling and orientation.";
        }
        else if (!e.Confident)
        {
            sev = VerdictSeverity.Warning;
            verdict = "Low confidence — a larger tilt or a quieter table would give a better value. You can still apply it.";
        }
        else
        {
            sev = VerdictSeverity.Good;
            verdict = "Good.";
        }

        string snr = e.Snr >= 1e6 ? "∞" : e.Snr.ToString("0", CultureInfo.InvariantCulture);
        return new VerdictCard(sensor, sev,
            $"{Fmt.Signed(e.AngleDeg, 2)}°  ± {e.UncertaintyDeg.ToString("0.00", CultureInfo.InvariantCulture)}°",
            $"Signal-to-noise {snr}" + (storedCdeg is null ? "" : $"   ·   stored now {Fmt.CentiDegrees(storedCdeg.Value)}") +
            (e.SingleSided ? "   ·   one tilt only" : ""),
            verdict);
    }

    public override void Dispose()
    {
        _w.Dispose();
        base.Dispose();
    }
}
