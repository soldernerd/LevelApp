using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Calibration;

namespace LevelApp.Instruments.Leveltronic.UI.Calibration;

/// <summary>The zero (flip) calibration screens — the phone app's <c>FlipCalScreen</c>.</summary>
public sealed class FlipCalViewModel : WizardViewModelBase
{
    private const string Title = "Zero (flip) calibration";

    private readonly FlipCalWizard _w;

    public FlipCalViewModel(LeveltronicSession session) : base(session)
    {
        _w = new FlipCalWizard(session.Api);
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
            case FlipStage.Select:
                return new WizardPage(Title,
                    Intro: "A real tilt reverses its sign when the instrument is turned 180°; the instrument's own zero " +
                           "error does not. Measuring both ways and averaging gives the zero error, which is then stored.",
                    Instructions:
                    [
                        "Put the instrument on a stable surface with the mark facing you.",
                        "Keep the table undisturbed during both steps.",
                    ],
                    ShowSensorSelector: true,
                    SummaryTitle: "Stored level point now",
                    SummaryLines: SummaryLines(s),
                    ShowServiceCard: true,
                    Actions: [Act("Start step 1", () => _w.StartAsync(), primary: true, enabled: service)]);

            case FlipStage.Measuring1:
                return new WizardPage(Title, StepText: "Step 1 of 3 — Measure in the starting position",
                    Progress: Hold(s), Actions: [Act("Cancel", () => _w.CancelAsync())]);

            case FlipStage.Turn:
                return new WizardPage(Title, StepText: "Step 2 of 3 — Turn the instrument 180°",
                    Instructions:
                    [
                        "Turn the instrument 180° in place — same spot, same surface. The mark now faces away from you.",
                        "Let it settle for a moment, then continue.",
                    ],
                    ShowServiceCard: !service,
                    Actions:
                    [
                        Act("Cancel", () => _w.CancelAsync()),
                        Act("Turned — measure", () => _w.ConfirmTurnedAsync(), primary: true, enabled: service),
                    ]);

            case FlipStage.Measuring2:
                return new WizardPage(Title, StepText: "Step 3 of 3 — Measure in the turned position",
                    Progress: Hold(s), Actions: [Act("Cancel", () => _w.CancelAsync())]);

            case FlipStage.Result:
            {
                var run = s.LastRun!;
                var runs = s.FinishedRuns;
                var rows = new List<ResultRow>();
                var ppm = new List<ResultRow>();
                for (int i = 0; i < 2; i++)
                {
                    if (!SensorMask.Has(run.Mask, i)) continue;
                    double b = run.Before.ZeroMmPerM(i), a = run.After.ZeroMmPerM(i);
                    rows.Add(new ResultRow($"S{i + 1}", Fmt.Signed(b, 4), Fmt.Signed(a, 4), Fmt.Signed(a - b, 4)));
                    long pb = run.Before.ZeroPpm[i], pa = run.After.ZeroPpm[i];
                    ppm.Add(new ResultRow($"S{i + 1}", Fmt.Signed(pb, 0), Fmt.Signed(pa, 0), Fmt.Signed(pa - pb, 0)));
                }
                return new WizardPage(Title,
                    Headline: "New zero stored",
                    Tables:
                    [
                        new ResultTable("Level point in mm/m (zero ÷ k)", rows),
                        new ResultTable("Stored zero (ppm of the ratio)", ppm),
                    ],
                    Footer: runs.Count < 2
                        ? "Run it once more: the second run should change the zero hardly at all. If it does, the table moved."
                        : $"This was run {runs.Count}. A good calibration changes the zero only slightly from run to run.",
                    Actions:
                    [
                        Act("Run again", () => _w.BackToStart()),
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

    private static WizardProgress Hold(FlipCalState s) => new(
        s.Target == 0 ? null : (double)s.Progress / s.Target, "Hold still…", $"{s.Progress} / {s.Target} batches");

    private static IReadOnlyList<string> SummaryLines(FlipCalState s) =>
    [
        .. Enumerable.Range(0, 2).Select(i => s.Current is { } c
            ? $"S{i + 1}   {Fmt.Zero(c.ZeroPpm[i], c.KMicro[i])}   ({c.ZeroPpm[i]} ppm)"
            : $"S{i + 1}   —"),
    ];

    public override void Dispose()
    {
        _w.Dispose();
        base.Dispose();
    }
}
