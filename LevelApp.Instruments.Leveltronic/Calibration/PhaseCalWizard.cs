using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.Calibration;

/// <summary>Which tilts the phase calibration measures.</summary>
public enum PhaseMode
{
    /// <summary>A clear positive and a clear negative tilt: the zero offset cancels. The most accurate.</summary>
    Both,
    /// <summary>One clear positive tilt only.</summary>
    PositiveOnly,
    /// <summary>One clear negative tilt only.</summary>
    NegativeOnly,
}

/// <summary>
/// The direction of a tilt: positive is the one the instrument reads as a positive
/// tilt (the front, the end with the red mark, up).
/// </summary>
public enum PhaseTilt { Positive, Negative }

public static class PhaseModeExtensions
{
    private static readonly PhaseTilt[] BothTilts = [PhaseTilt.Positive, PhaseTilt.Negative];
    private static readonly PhaseTilt[] PositiveTilts = [PhaseTilt.Positive];
    private static readonly PhaseTilt[] NegativeTilts = [PhaseTilt.Negative];

    /// <summary>The tilts to capture, in order.</summary>
    public static IReadOnlyList<PhaseTilt> Tilts(this PhaseMode mode) => mode switch
    {
        PhaseMode.PositiveOnly => PositiveTilts,
        PhaseMode.NegativeOnly => NegativeTilts,
        _ => BothTilts,
    };
}

public enum PhaseStage
{
    /// <summary>Choose the sensors and the tilts, read the current phases.</summary>
    Select,
    /// <summary>The tilt in <see cref="PhaseCalState.Tilt"/> is to be applied, then captured.</summary>
    Ready,
    Capturing,
    /// <summary>Computed; the user reviews and applies.</summary>
    Review,
    Applying,
    Done,
    Error,
}

public sealed record PhaseCalState(
    PhaseStage Stage = PhaseStage.Select,
    int Mask = SensorMask.Both,
    PhaseMode Mode = PhaseMode.Both,
    // The tilt being prepared or captured.
    PhaseTilt Tilt = PhaseTilt.Positive,
    // 0..1 of the capture time.
    double CaptureProgress = 0,
    // Batches received in the running capture.
    int Samples = 0,
    // Stored phases, centidegrees, [S1, S2], as last read.
    IReadOnlyList<long>? Current = null,
    // The computed phase per sensor (null if it could not be estimated).
    IReadOnlyList<PhaseEstimate?>? Estimates = null,
    IReadOnlyList<long>? Before = null,
    IReadOnlyList<long>? After = null,
    string? Message = null,
    bool NeedsService = false)
{
    public IReadOnlyList<PhaseEstimate?> PhaseEstimates => Estimates ?? [null, null];

    /// <summary>1-based position of <see cref="Tilt"/> among the tilts of the <see cref="Mode"/>.</summary>
    public int Step
    {
        get
        {
            var tilts = Mode.Tilts();
            for (int i = 0; i < tilts.Count; i++)
                if (tilts[i] == Tilt) return i + 1;
            return 0;
        }
    }

    public int Steps => Mode.Tilts().Count;
}

/// <summary>
/// Phase calibration. Each sensor's tilt signal lies along <c>e^{jφ}</c> in the complex
/// plane of <c>u = S / (A − B)</c>: a positive tilt points at φ, a negative one at
/// φ + 180°. Capture the phasor stream with a clear positive and a clear negative tilt
/// (the direction of their difference is φ, the zero offset cancels), or with one of
/// them only (the direction of <c>u</c> itself). Neither the size of the tilt nor the
/// stored phase matters, and no neutral measurement is needed. See
/// <see cref="CalibrationMath.EstimatePhaseFromTilts"/>. An inverted sensor (INVERT = 1)
/// gets φ + 180°, as its stored phase must absorb the sign. Port of the phone app's
/// <c>PhaseCalNotifier</c> (phase range ±180°).
/// </summary>
public sealed class PhaseCalWizard(InstrumentApi api, TimeProvider? time = null)
    : WizardBase<PhaseCalState>(new PhaseCalState())
{
    /// <summary>Length of one capture. ~40 batches/s, so ~325 batches.</summary>
    public static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(8);

    public const int MinSamples = 60;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ComplexStats[] _pos = [new(), new()];
    private readonly ComplexStats[] _neg = [new(), new()];
    private readonly object _statsGate = new();

    // The sensors' invert flags: an inverted sensor reads positive for a negative raw
    // signal, which the stored phase must absorb.
    private long[] _invert = [0, 0];

    private IDisposable? _sub;
    private ITimer? _timer;

    private async Task<long[]> ReadPhasesAsync() =>
        await Task.WhenAll(ValueCatalog.SensorPhase.Select(d => api.ReadValueAsync(d)));

    private async Task<long[]> ReadInvertAsync() =>
        await Task.WhenAll(ValueCatalog.SensorInvert.Select(d => api.ReadValueAsync(d)));

    public async Task RefreshValuesAsync()
    {
        try
        {
            var v = await ReadPhasesAsync();
            Update(s => s with { Current = v });
        }
        catch { /* shown as "—" */ }
    }

    public void SelectSensors(int mask)
    {
        if (State.Stage != PhaseStage.Select) return;
        Update(s => s with { Mask = mask });
    }

    public void SelectMode(PhaseMode mode)
    {
        if (State.Stage != PhaseStage.Select) return;
        Update(s => s with { Mode = mode });
    }

    /// <summary>Leave the selection and go to the first tilt.</summary>
    public async Task BeginAsync()
    {
        if (State.Stage != PhaseStage.Select) return;
        int gen = NextGen();
        long[] invert;
        try
        {
            invert = await ReadInvertAsync();
        }
        catch (Exception e)
        {
            Fail(gen, e);
            return;
        }
        if (gen != Gen) return;
        _invert = invert;
        Update(s => s with { Stage = PhaseStage.Ready, Tilt = s.Mode.Tilts()[0] });
    }

    /// <summary>Captures the tilt that is ready.</summary>
    public Task CaptureAsync()
    {
        if (State.Stage != PhaseStage.Ready) return Task.CompletedTask;
        var into = State.Tilt == PhaseTilt.Positive ? _pos : _neg;
        int gen = NextGen();
        lock (_statsGate)
            foreach (var s in into) s.Reset();
        Update(s => s with { Stage = PhaseStage.Capturing, CaptureProgress = 0, Samples = 0 });

        int ticks = 0;
        int total = (int)(CaptureDuration.TotalMilliseconds / 200);
        _timer?.Dispose();
        _timer = _time.CreateTimer(t =>
        {
            if (gen != Gen) return;
            ticks++;
            Update(s => s with { CaptureProgress = Math.Clamp((double)ticks / total, 0.0, 1.0) });
            if (ticks >= total)
            {
                _timer?.Dispose();
                _ = EndCaptureAsync(gen, into);
            }
        }, null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));

        _sub?.Dispose();
        _sub = api.SubscribePhasorStream(
            e =>
            {
                if (gen != Gen) return;
                // u = S / D with D = A − B
                double dr = e.IA - e.IB, di = e.QA - e.QB;
                var u1 = CalibrationMath.ComplexDivide(e.IS1, e.QS1, dr, di);
                var u2 = CalibrationMath.ComplexDivide(e.IS2, e.QS2, dr, di);
                if (u1 is null || u2 is null) return;
                int n;
                lock (_statsGate)
                {
                    into[0].Add(u1.Value.Re, u1.Value.Im);
                    into[1].Add(u2.Value.Re, u2.Value.Im);
                    n = into[0].N;
                }
                if (n % 8 == 0) Update(s => s with { Samples = n });
            },
            ex => Fail(gen, ex));
        return Task.CompletedTask;
    }

    private Task EndCaptureAsync(int gen, ComplexStats[] into)
    {
        if (gen != Gen) return Task.CompletedTask;
        _sub?.Dispose();
        _sub = null;

        int n;
        lock (_statsGate) n = into[0].N;
        if (n < MinSamples)
        {
            Fail(gen, new LinkException("too few phasor batches arrived — is the tilt measurement running?"));
            return Task.CompletedTask;
        }

        var st = State;
        var tilts = st.Mode.Tilts();
        int next = st.Step; // 1-based position of the current tilt = 0-based index of the next one
        if (next < tilts.Count)
        {
            Update(s => s with { Stage = PhaseStage.Ready, Tilt = tilts[next], Samples = n });
            return Task.CompletedTask;
        }

        PhaseEstimate?[] estimates;
        lock (_statsGate)
            estimates = [Estimate(0, tilts), Estimate(1, tilts)];
        Update(s => s with { Stage = PhaseStage.Review, Samples = n, Estimates = estimates });
        return Task.CompletedTask;
    }

    private PhaseEstimate? Estimate(int i, IReadOnlyList<PhaseTilt> tilts)
    {
        var e = CalibrationMath.EstimatePhaseFromTilts(
            positive: tilts.Contains(PhaseTilt.Positive) ? _pos[i] : null,
            negative: tilts.Contains(PhaseTilt.Negative) ? _neg[i] : null);
        // The estimate is the direction of a tilt that reads positive; with the sign of
        // the reading inverted that is the raw signal turned by 180°.
        return e is not null && _invert[i] == 1 ? e.Rotated(180) : e;
    }

    /// <summary>Back to the first tilt (e.g. the table was bumped).</summary>
    public void Redo()
    {
        Stop();
        Update(s => s with { Stage = PhaseStage.Ready, Tilt = s.Mode.Tilts()[0] });
    }

    /// <summary>Writes the estimated phase of every selected sensor that has a usable one. Needs service mode.</summary>
    public async Task ApplyAsync()
    {
        if (State.Stage != PhaseStage.Review) return;
        int gen = NextGen();
        Update(s => s with { Stage = PhaseStage.Applying });
        try
        {
            var before = await ReadPhasesAsync();
            var st = State;
            for (int i = 0; i < 2; i++)
            {
                var est = st.PhaseEstimates[i];
                if (!SensorMask.Has(st.Mask, i) || est is not { InRange: true }) continue;
                await api.WriteValueAsync(ValueCatalog.SensorPhase[i], est.CentiDegrees);
            }
            var after = await ReadPhasesAsync();
            if (gen != Gen) return;
            Update(s => s with { Stage = PhaseStage.Done, Before = before, After = after, Current = after });
        }
        catch (Exception e)
        {
            Fail(gen, e);
        }
    }

    public void BackToStart()
    {
        Stop();
        Update(s => s with
        {
            Stage = PhaseStage.Select, Estimates = null, Message = null, NeedsService = false,
        });
        _ = RefreshValuesAsync();
    }

    private void Stop()
    {
        NextGen();
        _sub?.Dispose();
        _sub = null;
        _timer?.Dispose();
        _timer = null;
    }

    private void Fail(int gen, Exception e)
    {
        if (gen != Gen) return;
        NextGen();
        _sub?.Dispose();
        _sub = null;
        _timer?.Dispose();
        _timer = null;
        var (msg, needsService) = Describe(e);
        Update(s => s with { Stage = PhaseStage.Error, Message = msg, NeedsService = needsService });
    }

    public override void Dispose()
    {
        Stop();
        base.Dispose();
    }
}
