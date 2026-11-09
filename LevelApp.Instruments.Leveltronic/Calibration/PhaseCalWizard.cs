using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.Calibration;

public enum PhaseStage
{
    /// <summary>Choose the sensors, read the current phases.</summary>
    Select,
    /// <summary>Instrument flat / unshimmed — ready to capture the reference.</summary>
    Reference,
    CapturingReference,
    /// <summary>Tilt step applied — ready to capture.</summary>
    Tilted,
    CapturingTilted,
    /// <summary>Computed; the user reviews and applies.</summary>
    Review,
    Applying,
    Done,
    Error,
}

public sealed record PhaseCalState(
    PhaseStage Stage = PhaseStage.Select,
    int Mask = SensorMask.Both,
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
}

/// <summary>
/// Phase calibration. Each sensor's tilt signal lies along <c>e^{jφ}</c> in the complex
/// plane of <c>u = S / (A − B)</c>. Capture the phasor stream flat and again with a
/// known tilt step; the direction of the change in <c>u</c> is φ — independent of the
/// stored phase and of the size of the step. See
/// <see cref="CalibrationMath.EstimatePhase"/>. Port of the phone app's
/// <c>PhaseCalNotifier</c> (phase range ±180° as the firmware now allows).
/// </summary>
public sealed class PhaseCalWizard(InstrumentApi api, TimeProvider? time = null)
    : WizardBase<PhaseCalState>(new PhaseCalState())
{
    /// <summary>Length of one capture. ~40 batches/s, so ~325 batches.</summary>
    public static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(8);

    public const int MinSamples = 60;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ComplexStats[] _flat = [new(), new()];
    private readonly ComplexStats[] _tilt = [new(), new()];
    private readonly object _statsGate = new();

    private IDisposable? _sub;
    private ITimer? _timer;

    private async Task<long[]> ReadPhasesAsync() =>
        await Task.WhenAll(ValueCatalog.SensorPhase.Select(d => api.ReadValueAsync(d)));

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

    /// <summary>Leave the selection and go to "capture the reference".</summary>
    public void Begin()
    {
        if (State.Stage != PhaseStage.Select) return;
        Update(s => s with { Stage = PhaseStage.Reference });
    }

    public Task CaptureReferenceAsync() =>
        State.Stage != PhaseStage.Reference
            ? Task.CompletedTask
            : CaptureAsync(PhaseStage.CapturingReference, PhaseStage.Tilted, _flat);

    public Task CaptureTiltedAsync() =>
        State.Stage != PhaseStage.Tilted
            ? Task.CompletedTask
            : CaptureAsync(PhaseStage.CapturingTilted, PhaseStage.Review, _tilt);

    private Task CaptureAsync(PhaseStage during, PhaseStage next, ComplexStats[] into)
    {
        int gen = NextGen();
        lock (_statsGate)
            foreach (var s in into) s.Reset();
        Update(s => s with { Stage = during, CaptureProgress = 0, Samples = 0 });

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
                _ = EndCaptureAsync(gen, next, into);
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

    private Task EndCaptureAsync(int gen, PhaseStage next, ComplexStats[] into)
    {
        if (gen != Gen) return Task.CompletedTask;
        _sub?.Dispose();
        _sub = null;

        int n;
        PhaseEstimate?[]? estimates = null;
        lock (_statsGate)
        {
            n = into[0].N;
            if (n >= MinSamples && next == PhaseStage.Review)
                estimates = [CalibrationMath.EstimatePhase(_flat[0], _tilt[0]), CalibrationMath.EstimatePhase(_flat[1], _tilt[1])];
        }

        if (n < MinSamples)
        {
            Fail(gen, new LinkException("too few phasor batches arrived — is the tilt measurement running?"));
            return Task.CompletedTask;
        }
        if (estimates is not null)
            Update(s => s with { Stage = PhaseStage.Review, Samples = n, Estimates = estimates });
        else
            Update(s => s with { Stage = next, Samples = n });
        return Task.CompletedTask;
    }

    /// <summary>Back to "capture the reference" (e.g. the table was bumped).</summary>
    public void RedoReference()
    {
        Stop();
        Update(s => s with { Stage = PhaseStage.Reference });
    }

    public void RedoTilted()
    {
        Stop();
        Update(s => s with { Stage = PhaseStage.Tilted });
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
