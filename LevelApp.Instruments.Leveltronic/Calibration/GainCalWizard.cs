using LevelApp.Instruments.Leveltronic.Preferences;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.Calibration;

public enum GainStage
{
    /// <summary>Choose the sensors and the known tilt step.</summary>
    Setup,
    /// <summary>Instrument level / unshimmed — ready to measure the baseline.</summary>
    Baseline,
    MeasuringBaseline,
    /// <summary>Known tilt step applied — ready to measure.</summary>
    Shim,
    MeasuringShim,
    /// <summary>Computed; the user reviews and applies.</summary>
    Review,
    Applying,
    Done,
    Error,
}

public sealed record GainCalState(
    GainStage Stage = GainStage.Setup,
    int Mask = SensorMask.Both,
    // Per-sensor shim setup [S1, S2]: a shim of ShimHeightMm[i] under one end of a
    // baseline of BaselineMm[i] gives the known tilt for sensor i.
    IReadOnlyList<double>? ShimHeightMm = null,
    IReadOnlyList<double>? BaselineMm = null,
    // 0..1 of the running precision measurement.
    double Progress = 0,
    bool Disturbed = false,
    // Stored k per sensor, 1e-6 per mm/m, as last read.
    IReadOnlyList<long>? CurrentK = null,
    // Precision readings [S1, S2] without / with the step.
    IReadOnlyList<double>? BaselineReading = null,
    IReadOnlyList<double>? ShimReading = null,
    IReadOnlyList<GainEstimate?>? Estimates = null,
    IReadOnlyList<long>? KBefore = null,
    IReadOnlyList<long>? KAfter = null,
    string? Message = null,
    bool NeedsService = false)
{
    public IReadOnlyList<double> ShimHeights => ShimHeightMm ?? [0.100, 0.100];
    public IReadOnlyList<double> Baselines => BaselineMm ?? [150.0, 150.0];
    public IReadOnlyList<GainEstimate?> GainEstimates => Estimates ?? [null, null];

    public IReadOnlyList<double> KnownTilts =>
        [CalibrationMath.ShimTilt(ShimHeights[0], Baselines[0]), CalibrationMath.ShimTilt(ShimHeights[1], Baselines[1])];
}

/// <summary>
/// Gain (sensitivity k) calibration against a known tilt step. Measure a baseline and
/// the same with a shim of known height under one end of a baseline of known length
/// (precision measurements, so each is a clean 2 s window);
/// <c>kNew = kOld · |Δreading| / tilt</c> (<see cref="CalibrationMath.EstimateGain"/>).
/// The shim setup is remembered per instrument. Port of the phone app's <c>GainCalNotifier</c>.
/// </summary>
public sealed class GainCalWizard : WizardBase<GainCalState>
{
    public const string NoCleanWindowMessage =
        "No quiet 2 s window within 5 s. Keep the table still and measure again.";

    private readonly InstrumentApi _api;
    private readonly TimeProvider _time;
    private readonly ILeveltronicPreferences? _prefs;
    private readonly string? _serial;

    private PrecisionRun? _run;

    public GainCalWizard(InstrumentApi api, TimeProvider? time = null,
        ILeveltronicPreferences? prefs = null, string? serial = null) : base(new GainCalState())
    {
        _api = api;
        _time = time ?? TimeProvider.System;
        _prefs = prefs;
        _serial = serial;
        LoadShim();
    }

    private void LoadShim()
    {
        if (_prefs is null || _serial is null) return;
        var h = State.ShimHeights.ToArray();
        var b = State.Baselines.ToArray();
        bool changed = false;
        for (int i = 0; i < 2; i++)
        {
            if (_prefs.GetShim(_serial, i) is { } s)
            {
                h[i] = s.HeightMm;
                b[i] = s.BaselineMm;
                changed = true;
            }
        }
        if (changed) Update(s => s with { ShimHeightMm = h, BaselineMm = b });
    }

    private async Task<long[]> ReadKAsync() =>
        await Task.WhenAll(ValueCatalog.SensorK.Select(d => _api.ReadValueAsync(d)));

    public async Task RefreshValuesAsync()
    {
        try
        {
            var v = await ReadKAsync();
            Update(s => s with { CurrentK = v });
        }
        catch { /* shown as "—" */ }
    }

    public void SelectSensors(int mask)
    {
        if (State.Stage != GainStage.Setup) return;
        Update(s => s with { Mask = mask });
    }

    public void SetShimForSensor(int sensor, double? heightMm = null, double? baselineMm = null)
    {
        if (State.Stage != GainStage.Setup) return;
        var h = State.ShimHeights.ToArray();
        var b = State.Baselines.ToArray();
        if (heightMm is { } hv) h[sensor] = hv;
        if (baselineMm is { } bv) b[sensor] = bv;
        Update(s => s with { ShimHeightMm = h, BaselineMm = b });

        if (_prefs is not null && _serial is not null)
            for (int i = 0; i < 2; i++) _prefs.SetShim(_serial, i, new ShimSetup(h[i], b[i]));
    }

    /// <summary>Leave the setup and go to "measure the baseline".</summary>
    public void Begin()
    {
        var st = State;
        if (st.Stage != GainStage.Setup) return;
        var tilts = st.KnownTilts;
        for (int i = 0; i < 2; i++)
            if (SensorMask.Has(st.Mask, i) && tilts[i] <= 0) return;
        Update(s => s with { Stage = GainStage.Baseline });
    }

    public async Task MeasureBaselineAsync()
    {
        if (State.Stage != GainStage.Baseline) return;
        var reading = await MeasureAsync(GainStage.MeasuringBaseline, GainStage.Baseline);
        if (reading is null) return;
        Update(s => s with
        {
            Stage = GainStage.Shim,
            BaselineReading = [reading.TiltS1, reading.TiltS2],
        });
    }

    public async Task MeasureShimAsync()
    {
        if (State.Stage != GainStage.Shim) return;
        var reading = await MeasureAsync(GainStage.MeasuringShim, GainStage.Shim);
        if (reading is null) return;

        var st = State;
        var baseline = st.BaselineReading!;
        double[] tilted = [reading.TiltS1, reading.TiltS2];
        long[] k;
        try { k = st.CurrentK is { } known ? [.. known] : await ReadKAsync(); }
        catch (Exception e) { Fail(Gen, e); return; }

        var tilts = st.KnownTilts;
        GainEstimate?[] est =
        [
            CalibrationMath.EstimateGain(k[0], baseline[0], tilted[0], tilts[0]),
            CalibrationMath.EstimateGain(k[1], baseline[1], tilted[1], tilts[1]),
        ];
        Update(s => s with { Stage = GainStage.Review, ShimReading = tilted, CurrentK = k, Estimates = est });
    }

    /// <summary>One precision measurement; null if it did not give a result (the stage then returns to <paramref name="fallback"/> with a message).</summary>
    private async Task<PrecisionStatus?> MeasureAsync(GainStage during, GainStage fallback)
    {
        int gen = NextGen();
        var run = new PrecisionRun(_api, _time);
        _run = run;
        Update(s => s with { Stage = during, Progress = 0, Disturbed = false, Message = null, NeedsService = false });
        run.Progress += (_, s) =>
        {
            if (gen != Gen || s.Phase != PrecisionPhase.Running) return;
            Update(x => x with { Progress = s.Target == 0 ? 0 : (double)s.Count / s.Target, Disturbed = s.Disturbed });
        };
        try
        {
            var result = await run.StartAsync();
            if (gen != Gen) return null;
            if (result.Failed)
            {
                Update(s => s with { Stage = fallback, Message = NoCleanWindowMessage });
                return null;
            }
            return result;
        }
        catch (PrecisionCancelledException)
        {
            return null;
        }
        catch (Exception e)
        {
            Fail(gen, e);
            return null;
        }
        finally
        {
            if (gen == Gen) _run = null;
        }
    }

    public void MeasureAgainBaseline()
    {
        CancelRun();
        Update(s => s with { Stage = GainStage.Baseline });
    }

    public void MeasureAgainShim()
    {
        CancelRun();
        Update(s => s with { Stage = GainStage.Shim });
    }

    /// <summary>
    /// Writes the estimated k of every selected sensor that has a usable one. Nothing is
    /// measured again: the new k is only stored.
    /// </summary>
    public async Task ApplyAsync()
    {
        if (State.Stage != GainStage.Review) return;
        int gen = NextGen();
        Update(s => s with { Stage = GainStage.Applying });
        try
        {
            var before = await ReadKAsync();
            var st = State;
            for (int i = 0; i < 2; i++)
            {
                var est = st.GainEstimates[i];
                if (!SensorMask.Has(st.Mask, i) || est is not { InRange: true }) continue;
                await _api.WriteValueAsync(ValueCatalog.SensorK[i], est.KNewMicro);
            }
            var after = await ReadKAsync();
            if (gen != Gen) return;
            Update(s => s with { Stage = GainStage.Done, KBefore = before, KAfter = after, CurrentK = after, Progress = 0 });
        }
        catch (Exception e)
        {
            Fail(gen, e);
        }
    }

    public void BackToStart()
    {
        CancelRun();
        Update(s => new GainCalState(
            Mask: s.Mask, ShimHeightMm: s.ShimHeights, BaselineMm: s.Baselines, CurrentK: s.CurrentK));
        _ = RefreshValuesAsync();
    }

    private void CancelRun()
    {
        NextGen();
        var r = Interlocked.Exchange(ref _run, null);
        if (r is not null) _ = r.CancelAsync();
    }

    private void Fail(int gen, Exception e)
    {
        if (gen != Gen) return;
        NextGen();
        var (msg, needsService) = Describe(e);
        Update(s => s with { Stage = GainStage.Error, Message = msg, NeedsService = needsService });
    }

    public override void Dispose()
    {
        CancelRun();
        base.Dispose();
    }
}
