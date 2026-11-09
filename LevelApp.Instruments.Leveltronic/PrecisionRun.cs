using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// One triggered precision measurement, driven by the Procedures PRECISION
/// subscription: start it, watch <see cref="Progress"/>, await the final status.
/// <para>
/// The final status is either a result (<c>Failed == false</c>) or "no clean
/// window within 5 s" (<c>Failed == true</c>). Anything else — the device
/// refusing the start, the link dropping, no answer for <see cref="Timeout"/> —
/// throws. A stale <c>done</c> left over from a previous run is ignored until
/// this run has been seen <c>running</c> (or <see cref="StaleGrace"/> has
/// passed), because the resource stays readable after the last run.
/// (The phone app's <c>PrecisionRun</c>.)
/// </para>
/// </summary>
public sealed class PrecisionRun(InstrumentApi api, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TaskCompletionSource<PrecisionStatus> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    private IDisposable? _sub;
    private ITimer? _timer;
    private bool _sawRunning;
    private bool _finished;
    private int _seconds;
    private int _quietSeconds;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(9);
    public TimeSpan StaleGrace { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Every status push (and fallback poll) of this run.</summary>
    public event EventHandler<PrecisionStatus>? Progress;

    public Task<PrecisionStatus> StartAsync()
    {
        _sub = api.SubscribePrecision(OnStatus, Fail);
        _ = TriggerAsync();
        _timer = _time.CreateTimer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        return _done.Task;
    }

    private async Task TriggerAsync()
    {
        try { await api.PrecisionStartAsync().ConfigureAwait(false); }
        catch (Exception e) { Fail(e); }
    }

    private void Tick()
    {
        bool timedOut, poll;
        lock (_gate)
        {
            if (_finished) return;
            _seconds++;
            _quietSeconds++;
            timedOut = _seconds * 1000 > Timeout.TotalMilliseconds;
            // Pushes come about every 250 ms while running; a second of silence
            // means one was lost — ask once.
            poll = !timedOut && _quietSeconds >= 1;
        }

        if (timedOut)
        {
            Fail(new LinkException("the measurement did not finish"));
            return;
        }
        if (poll)
            _ = api.PrecisionStatusAsync().ContinueWith(
                t => { if (t.IsCompletedSuccessfully) OnStatus(t.Result); }, TaskScheduler.Default);
    }

    private void OnStatus(PrecisionStatus s)
    {
        lock (_gate)
        {
            if (_finished) return;
            _quietSeconds = 0;
        }
        Progress?.Invoke(this, s);

        switch (s.Phase)
        {
            case PrecisionPhase.Running:
                lock (_gate) _sawRunning = true;
                break;
            case PrecisionPhase.Done:
                bool fresh;
                lock (_gate)
                    fresh = _sawRunning || _seconds * 1000 >= StaleGrace.TotalMilliseconds;
                if (fresh) Finish(s);
                break;
        }
    }

    private void Finish(PrecisionStatus s)
    {
        lock (_gate)
        {
            if (_finished) return;
            _finished = true;
        }
        _done.TrySetResult(s);
        Cleanup();
    }

    private void Fail(Exception e)
    {
        lock (_gate)
        {
            if (_finished) return;
            _finished = true;
        }
        _done.TrySetException(e);
        Cleanup();
    }

    /// <summary>Aborts the run (also on the device). <see cref="StartAsync"/> then throws <see cref="PrecisionCancelledException"/>.</summary>
    public async Task CancelAsync()
    {
        lock (_gate)
        {
            if (_finished) return;
        }
        Fail(new PrecisionCancelledException());
        try { await api.PrecisionCancelAsync().ConfigureAwait(false); }
        catch { /* best effort */ }
    }

    private void Cleanup()
    {
        _timer?.Dispose();
        _sub?.Dispose();
        _sub = null;
    }
}
