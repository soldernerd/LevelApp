using System.Globalization;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Calibration;
using LevelApp.Instruments.Leveltronic.Preferences;

namespace LevelApp.Instruments.Leveltronic.UI.Calibration;

/// <summary>The gain (sensitivity k) calibration screens — the phone app's <c>GainCalScreen</c>.</summary>
public sealed class GainCalViewModel : WizardViewModelBase
{
    private const string Title = "Gain calibration";

    private readonly GainCalWizard _w;

    public GainCalViewModel(LeveltronicSession session, ILeveltronicPreferences prefs, TimeProvider? time = null)
        : base(session)
    {
        _w = new GainCalWizard(session.Api, time, prefs, session.Serial);
        _w.StateChanged += (_, _) => Refresh();
        Refresh();
        _ = _w.RefreshValuesAsync();
    }

    protected override int CurrentMask => _w.State.Mask;
    protected override void ApplyMask(int mask) => _w.SelectSensors(mask);

    // ── Shim inputs (bound to NumberBoxes) ───────────────────────────────────

    public double Shim1Height { get => _w.State.ShimHeights[0]; set => _w.SetShimForSensor(0, heightMm: Clean(value)); }
    public double Shim1Edge { get => _w.State.Baselines[0]; set => _w.SetShimForSensor(0, baselineMm: Clean(value)); }
    public double Shim2Height { get => _w.State.ShimHeights[1]; set => _w.SetShimForSensor(1, heightMm: Clean(value)); }
    public double Shim2Edge { get => _w.State.Baselines[1]; set => _w.SetShimForSensor(1, baselineMm: Clean(value)); }

    private static double Clean(double v) => double.IsFinite(v) && v >= 0 ? v : 0;

    protected override void OnPageRefreshed()
    {
        OnPropertyChanged(nameof(Shim1Height));
        OnPropertyChanged(nameof(Shim1Edge));
        OnPropertyChanged(nameof(Shim2Height));
        OnPropertyChanged(nameof(Shim2Edge));
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    protected override WizardPage BuildPage()
    {
        var s = _w.State;
        bool service = ServiceMode;

        switch (s.Stage)
        {
            case GainStage.Setup:
            {
                var tilts = s.KnownTilts;
                bool ready = Enumerable.Range(0, 2).All(i => !SensorMask.Has(s.Mask, i) || tilts[i] > 0);
                string hint = ready
                    ? string.Join("   ·   ", Enumerable.Range(0, 2).Where(i => SensorMask.Has(s.Mask, i))
                        .Select(i => $"S{i + 1}: {tilts[i].ToString("0.0000", CultureInfo.InvariantCulture)} mm/m"))
                    : "Enter shim height and edge length for each sensor.";
                return new WizardPage(Title,
                    Intro: "Sets the sensitivity k: how many mm/m a given signal means. It is found by measuring a tilt step " +
                           "you know exactly — a shim of known height under one end of an edge of known length.",
                    ShowSensorSelector: true,
                    ShowShimInputs: true,
                    ShimHint: hint,
                    SummaryTitle: "Stored sensitivity k (1e-6 per mm/m)",
                    SummaryLines: [.. Enumerable.Range(0, 2).Select(i =>
                        $"S{i + 1}   {(s.CurrentK is { } k ? k[i].ToString(CultureInfo.InvariantCulture) : "—")}")],
                    ShowServiceCard: true,
                    Actions: [Act("Start", () => _w.Begin(), primary: true, enabled: ready)]);
            }

            case GainStage.Baseline:
                return new WizardPage(Title, StepText: "Step 1 of 3 — Baseline",
                    Intro: s.Message,
                    Instructions:
                    [
                        "Put the instrument on the edge / surface without the shim.",
                        "Keep the table still, then measure. Takes about 3 seconds.",
                    ],
                    Actions:
                    [
                        Act("Back", () => _w.BackToStart()),
                        Act("Measure baseline", () => _w.MeasureBaselineAsync(), primary: true),
                    ]);

            case GainStage.MeasuringBaseline:
            case GainStage.MeasuringShim:
            case GainStage.Verifying:
            {
                bool baseline = s.Stage == GainStage.MeasuringBaseline;
                bool verifying = s.Stage == GainStage.Verifying;
                return new WizardPage(Title,
                    StepText: $"Step {(baseline ? 1 : verifying ? 3 : 2)} of 3 — " +
                              (baseline ? "Baseline" : verifying ? "Checking the new k" : "With the shim"),
                    Progress: new WizardProgress(Math.Clamp(s.Progress, 0.0, 1.0), "Measuring… hold still",
                        Warning: s.Disturbed ? "The table is not quiet yet — wait for it to calm down." : null),
                    Actions: [Act("Cancel", () => _w.BackToStart())]);
            }

            case GainStage.Shim:
                return new WizardPage(Title, StepText: "Step 2 of 3 — With the shim",
                    Intro: s.Message,
                    Instructions:
                    [
                        .. Enumerable.Range(0, 2).Where(i => SensorMask.Has(s.Mask, i)).Select(i =>
                            $"S{i + 1}: put the {Fmt.Num(s.ShimHeights[i])} mm shim under one end of the {Fmt.Num(s.Baselines[i])} mm edge."),
                        "Let it settle, keep it still, then measure.",
                    ],
                    Actions:
                    [
                        Act("Redo baseline", () => _w.MeasureAgainBaseline()),
                        Act("Measure with shim", () => _w.MeasureShimAsync(), primary: true),
                    ]);

            case GainStage.Review:
            {
                var cards = new List<VerdictCard>();
                bool canApply = false;
                for (int i = 0; i < 2; i++)
                {
                    if (!SensorMask.Has(s.Mask, i)) continue;
                    var e = s.GainEstimates[i];
                    canApply |= e is { InRange: true };
                    cards.Add(Card($"S{i + 1}", e, s.BaselineReading![i], s.ShimReading![i]));
                }
                return new WizardPage(Title, StepText: "Step 3 of 3 — Result",
                    Verdicts: cards,
                    ShowServiceCard: !service,
                    Actions:
                    [
                        Act("Redo shim", () => _w.MeasureAgainShim()),
                        Act("Apply", () => _w.ApplyAsync(), primary: true, enabled: service && canApply),
                    ]);
            }

            case GainStage.Applying:
                return new WizardPage(Title, Busy: true, BusyText: "Writing k…");

            case GainStage.Done:
            {
                var tables = new List<ResultTable>();
                if (s.KBefore is not null && s.KAfter is not null)
                    tables.Add(new ResultTable("k  (1e-6 per mm/m)",
                        [.. Enumerable.Range(0, 2).Where(i => SensorMask.Has(s.Mask, i)).Select(i =>
                            new ResultRow($"S{i + 1}", s.KBefore[i].ToString(CultureInfo.InvariantCulture),
                                s.KAfter[i].ToString(CultureInfo.InvariantCulture),
                                Fmt.Signed(((double)s.KAfter[i] / s.KBefore[i] - 1) * 100, 1, "%")))]));
                string? footer = null;
                if (s.VerifyReading is { } verify && s.ShimReading is { } shim && s.BaselineReading is { } baseline)
                {
                    var tilts = s.KnownTilts;
                    tables.Add(new ResultTable("Check: the shim now reads",
                        [.. Enumerable.Range(0, 2).Where(i => SensorMask.Has(s.Mask, i)).Select(i =>
                            new ResultRow($"S{i + 1}", Fmt.Signed(shim[i] - baseline[i], 4), Fmt.Signed(verify[i], 4),
                                Fmt.Signed((Math.Abs(verify[i]) / tilts[i] - 1) * 100, 1, "%")))]));
                    footer = "\"Before\" is the step as read with the old k, \"After\" with the new k; \"Change\" is the new " +
                             "reading's deviation from the known tilt.\n\nLast in the usual order: the zero (flip) calibration.";
                }
                return new WizardPage(Title,
                    Headline: "Sensitivity stored",
                    Tables: tables,
                    Footer: footer ?? "Last in the usual order: the zero (flip) calibration.",
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

    private static VerdictCard Card(string sensor, GainEstimate? e, double baseline, double tilted)
    {
        VerdictSeverity sev;
        string verdict;
        if (e is null)
        {
            sev = VerdictSeverity.Bad;
            verdict = "The reading did not change. Check the shim and measure again.";
        }
        else if (!e.InRange)
        {
            sev = VerdictSeverity.Bad;
            verdict = "The new k would be outside the instrument's range.";
        }
        else if (!e.Positive)
        {
            sev = VerdictSeverity.Warning;
            verdict = "The reading moved the other way (negative). Magnitudes are used for k; if \"front up\" should " +
                      "read positive, change the invert setting afterwards.";
        }
        else if (Math.Abs(e.ChangePercent) > 50)
        {
            sev = VerdictSeverity.Warning;
            verdict = "A very large correction — double-check the shim height and edge length.";
        }
        else
        {
            sev = VerdictSeverity.Good;
            verdict = "Good.";
        }

        return new VerdictCard(sensor, sev,
            e is null ? "—" : $"k {e.KOldMicro} to {e.KNewMicro}",
            $"Baseline {Fmt.Signed(baseline, 4)}   ·   with shim {Fmt.Signed(tilted, 4)}   ·   change {Fmt.Signed(tilted - baseline, 4)}" +
            (e is null ? "" : $"  (known {e.TrueTilt.ToString("0.0000", CultureInfo.InvariantCulture)})   ·   k {Fmt.Signed(e.ChangePercent, 1, "%")}"),
            verdict);
    }

    public override void Dispose()
    {
        _w.Dispose();
        base.Dispose();
    }
}
