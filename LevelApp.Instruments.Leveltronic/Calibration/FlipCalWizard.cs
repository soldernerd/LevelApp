using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic.Calibration;

/// <summary>Which sensors a calibration run covers: bit0 S1, bit1 S2.</summary>
public static class SensorMask
{
    public const int S1 = 1;
    public const int S2 = 2;
    public const int Both = 3;

    public static bool Has(int mask, int sensorIndex) => (mask & (1 << sensorIndex)) != 0;

    public static string Label(int mask) => mask switch { S1 => "S1", S2 => "S2", _ => "S1 and S2" };
}

/// <summary>
/// Base of the wizard state machines: an immutable state record, a change event,
/// and a generation counter that lets a late async result be recognised as
/// belonging to a run that has since been cancelled or restarted. Events come
/// from whatever thread the link delivers on — the UI marshals.
/// </summary>
public abstract class WizardBase<TState> : IDisposable where TState : class
{
    private readonly object _gate = new();
    private TState _state;
    private int _gen;
    private bool _disposed;

    protected WizardBase(TState initial) => _state = initial;

    public TState State { get { lock (_gate) return _state; } }

    public event EventHandler<TState>? StateChanged;

    protected int Gen => Volatile.Read(ref _gen);
    protected int NextGen() => Interlocked.Increment(ref _gen);

    protected void Update(Func<TState, TState> change)
    {
        TState next;
        lock (_gate)
        {
            if (_disposed) return;
            _state = next = change(_state);
        }
        StateChanged?.Invoke(this, next);
    }

    /// <summary>Operator-facing text and whether service mode is the problem.</summary>
    protected static (string Message, bool NeedsService) Describe(Exception e, bool linkAsLost = false) => e switch
    {
        ApiException api => (api.UserMessage, api.IsServiceModeRequired),
        LinkException link => (linkAsLost ? "Lost contact with the instrument." : link.Message, false),
        _ => ($"Something went wrong: {e.Message}", false),
    };

    public virtual void Dispose()
    {
        lock (_gate) _disposed = true;
    }
}

/// <summary>Calibration values of the two sensors at one moment.</summary>
public sealed record ZeroSnapshot(IReadOnlyList<long> ZeroPpm, IReadOnlyList<long> KMicro)
{
    /// <summary>The level point in mm/m — <c>zero / k</c>, what the display would read with the instrument perfectly level.</summary>
    public double ZeroMmPerM(int i) => (double)ZeroPpm[i] / KMicro[i];
}

/// <summary>One finished flip-calibration run.</summary>
public sealed record FlipRun(int Mask, ZeroSnapshot Before, ZeroSnapshot After);

public enum FlipStage
{
    /// <summary>Choose the sensors, read the current values.</summary>
    Select,
    /// <summary>Step 1 running (instrument in its starting position).</summary>
    Measuring1,
    /// <summary>Step 1 done — turn the instrument by 180° and continue.</summary>
    Turn,
    /// <summary>Step 2 running.</summary>
    Measuring2,
    /// <summary>Done: before / after values.</summary>
    Result,
    Error,
}

public sealed record FlipCalState(
    FlipStage Stage = FlipStage.Select,
    int Mask = SensorMask.Both,
    int Progress = 0,
    int Target = 64,
    ZeroSnapshot? Current = null,
    IReadOnlyList<FlipRun>? Runs = null,
    string? Message = null,
    bool NeedsService = false)
{
    /// <summary>
    /// Finished runs, newest last — comparing two consecutive ones is the manual's
    /// "repeat once and check that two runs agree".
    /// </summary>
    public IReadOnlyList<FlipRun> FinishedRuns => Runs ?? [];

    public FlipRun? LastRun => FinishedRuns.Count == 0 ? null : FinishedRuns[^1];
}

/// <summary>
/// The flip (zero) calibration wizard: step 1, turn by 180°, step 2, show the stored
/// zero before and after. Driven by the Procedures ZERO_CAL subscription. Needs
/// service mode. Port of the phone app's <c>FlipCalNotifier</c>.
/// </summary>
public sealed class FlipCalWizard(InstrumentApi api) : WizardBase<FlipCalState>(new FlipCalState())
{
    private IDisposable? _sub;
    private ZeroSnapshot? _before;
    private int _runMask = SensorMask.Both;
    private bool _sawStep2;

    private async Task<ZeroSnapshot> ReadAsync()
    {
        var zero = await Task.WhenAll(ValueCatalog.SensorZero.Select(d => api.ReadValueAsync(d)));
        var k = await Task.WhenAll(ValueCatalog.SensorK.Select(d => api.ReadValueAsync(d)));
        return new ZeroSnapshot(zero, k);
    }

    /// <summary>Reads the stored values for the selection screen.</summary>
    public async Task RefreshValuesAsync()
    {
        try
        {
            var snap = await ReadAsync();
            Update(s => s with { Current = snap });
        }
        catch
        {
            // The select stage shows "—" until the next try.
        }
    }

    public void SelectSensors(int mask)
    {
        var st = State.Stage;
        if (st is not (FlipStage.Select or FlipStage.Result)) return;
        Update(s => s with { Mask = mask, Stage = FlipStage.Select });
    }

    /// <summary>Step 1.</summary>
    public async Task StartAsync()
    {
        int gen = NextGen();
        _runMask = State.Mask;
        _sawStep2 = false;
        try
        {
            _before = await ReadAsync();
            if (gen != Gen) return;
            Update(s => s with { Stage = FlipStage.Measuring1, Progress = 0, Current = _before, Message = null, NeedsService = false });
            _sub?.Dispose();
            _sub = api.SubscribeZeroCal(st => OnStatus(gen, st), ex => Fail(gen, ex));
            await api.ZeroCalStep1Async(_runMask);
        }
        catch (Exception e)
        {
            Fail(gen, e);
        }
    }

    /// <summary>Step 2, after the user turned the instrument by 180°.</summary>
    public async Task ConfirmTurnedAsync()
    {
        if (State.Stage != FlipStage.Turn) return;
        int gen = Gen;
        Update(s => s with { Stage = FlipStage.Measuring2, Progress = 0 });
        try
        {
            await api.ZeroCalStep2Async();
        }
        catch (Exception e)
        {
            Fail(gen, e);
        }
    }

    public async Task CancelAsync()
    {
        NextGen();
        _sub?.Dispose();
        _sub = null;
        Update(s => s with { Stage = FlipStage.Select, Progress = 0 });
        try { await api.ZeroCalCancelAsync(); } catch { /* best effort */ }
        _ = RefreshValuesAsync();
    }

    /// <summary>Back to the sensor selection after a result or an error.</summary>
    public void BackToStart()
    {
        Update(s => s with { Stage = FlipStage.Select, Progress = 0, Message = null, NeedsService = false });
        _ = RefreshValuesAsync();
    }

    private void OnStatus(int gen, ZeroCalStatus s)
    {
        if (gen != Gen) return;
        switch (s.Phase)
        {
            case ZeroCalPhase.Step1Running:
                if (State.Stage == FlipStage.Measuring1)
                    Update(x => x with { Progress = s.Progress, Target = s.Target });
                break;
            case ZeroCalPhase.Step1Done:
                if (State.Stage == FlipStage.Measuring1)
                    Update(x => x with { Stage = FlipStage.Turn, Progress = s.Target, Target = s.Target });
                break;
            case ZeroCalPhase.Step2Running:
                _sawStep2 = true;
                if (State.Stage == FlipStage.Measuring2)
                    Update(x => x with { Progress = s.Progress, Target = s.Target });
                break;
            case ZeroCalPhase.ResultReady:
            case ZeroCalPhase.Idle:
                // The result is stored automatically and the phase falls back to idle
                // (ResultReady is transient). Idle after step 2 means done.
                if (State.Stage == FlipStage.Measuring2 && _sawStep2)
                    _ = FinishAsync(gen);
                break;
        }
    }

    private async Task FinishAsync(int gen)
    {
        _sub?.Dispose();
        _sub = null;
        try
        {
            var after = await ReadAsync();
            if (gen != Gen) return;
            var run = new FlipRun(_runMask, _before ?? after, after);
            Update(s => s with { Stage = FlipStage.Result, Current = after, Runs = [.. s.FinishedRuns, run] });
        }
        catch (Exception e)
        {
            Fail(gen, e);
        }
    }

    private void Fail(int gen, Exception e)
    {
        if (gen != Gen) return;
        NextGen();
        _sub?.Dispose();
        _sub = null;
        var (msg, needsService) = Describe(e, linkAsLost: true);
        Update(s => s with { Stage = FlipStage.Error, Message = msg, NeedsService = needsService });
    }

    public override void Dispose()
    {
        // Leaving the wizard mid-run stops the device's procedure too.
        if (State.Stage is FlipStage.Measuring1 or FlipStage.Turn or FlipStage.Measuring2)
            _ = api.ZeroCalCancelAsync().ContinueWith(_ => { }, TaskScheduler.Default);
        _sub?.Dispose();
        base.Dispose();
    }
}
