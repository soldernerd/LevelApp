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

    protected override WizardPage BuildPage()
    {
        var s = _w.State;
        bool service = ServiceMode;

        switch (s.Stage)
        {
            case PhaseStage.Select:
                return new WizardPage(Title,
                    Intro: "Each sensor's signal is rotated by a calibrated phase so that a tilt falls entirely in the " +
                           "in-phase part. To find it the app records the raw signal level and again with a known tilt " +
                           "step. The size of the step does not matter for the phase — only that it is clearly larger " +
                           "than the noise (0.1 mm under a 1 m edge is plenty).",
                    ShowSensorSelector: true,
                    SummaryTitle: "Stored phase",
                    SummaryLines: [.. Enumerable.Range(0, 2).Select(i =>
                        $"S{i + 1}   {(s.Current is { } c ? Fmt.CentiDegrees(c[i]) : "—")}")],
                    ShowServiceCard: true,
                    Actions: [Act("Start", () => _w.Begin(), primary: true)]);

            case PhaseStage.Reference:
                return new WizardPage(Title, StepText: "Step 1 of 3 — Reference",
                    Instructions:
                    [
                        "Put the instrument on the surface without any shim.",
                        "Keep the table still and do not touch the instrument.",
                        "Capture takes about 8 seconds.",
                    ],
                    Actions:
                    [
                        Act("Back", () => _w.BackToStart()),
                        Act("Capture reference", () => _w.CaptureReferenceAsync(), primary: true),
                    ]);

            case PhaseStage.CapturingReference:
            case PhaseStage.CapturingTilted:
                return new WizardPage(Title,
                    StepText: s.Stage == PhaseStage.CapturingReference ? "Step 1 of 3 — Reference" : "Step 2 of 3 — Tilt step",
                    Progress: new WizardProgress(s.CaptureProgress, "Capturing… hold still", $"{s.Samples} batches"),
                    Actions: [Act("Cancel", () => _w.BackToStart())]);

            case PhaseStage.Tilted:
                return new WizardPage(Title, StepText: "Step 2 of 3 — Tilt step",
                    Instructions:
                    [
                        "Raise one end of the surface (or the instrument's base) with a shim — at least 0.1 mm under a 1 m edge.",
                        "Let it settle, keep it still, then capture.",
                    ],
                    Actions:
                    [
                        Act("Redo reference", () => _w.RedoReference()),
                        Act("Capture tilt", () => _w.CaptureTiltedAsync(), primary: true),
                    ]);

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
                return new WizardPage(Title, StepText: "Step 3 of 3 — Result",
                    Verdicts: cards,
                    ShowServiceCard: !service,
                    Actions:
                    [
                        Act("Redo tilt", () => _w.RedoTilted()),
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

    private static VerdictCard Card(string sensor, PhaseEstimate? e, long? storedCdeg)
    {
        if (e is null)
            return new VerdictCard(sensor, VerdictSeverity.Bad, "—", "",
                "No usable step was seen. Use a larger shim and redo the tilt.");

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
            verdict = "Low confidence — a larger step or a quieter table would give a better value. You can still apply it.";
        }
        else
        {
            sev = VerdictSeverity.Good;
            verdict = "Good.";
        }

        string snr = e.Snr >= 1e6 ? "∞" : e.Snr.ToString("0", CultureInfo.InvariantCulture);
        return new VerdictCard(sensor, sev,
            $"{Fmt.Signed(e.AngleDeg, 2)}°  ± {e.UncertaintyDeg.ToString("0.00", CultureInfo.InvariantCulture)}°",
            $"Signal-to-noise {snr}" + (storedCdeg is null ? "" : $"   ·   stored now {Fmt.CentiDegrees(storedCdeg.Value)}"),
            verdict);
    }

    public override void Dispose()
    {
        _w.Dispose();
        base.Dispose();
    }
}
