using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// Keeps the three always-on subscriptions (LIVE, STATUS, ENV) and merges their
/// pushes into one <see cref="InstrumentSnapshot"/>. One GET of each seeds the
/// state at once instead of waiting for the first push. If the tilt demodulation
/// turns out to be stopped it is started once. (The phone app's <c>LiveFeed</c>.)
/// </summary>
public sealed class LiveFeed(InstrumentApi api) : IDisposable
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _subs = [];
    private InstrumentSnapshot _state = new();
    private bool _started;
    private bool _demodChecked;
    private bool _disposed;

    public InstrumentSnapshot State { get { lock (_gate) return _state; } }

    /// <summary>Raised on every update of any part (on the link's thread).</summary>
    public event EventHandler<InstrumentSnapshot>? Updated;

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }

        _subs.Add(api.SubscribeLive(v => Update(s => s with { Live = v }), _ => { }));
        _subs.Add(api.SubscribeStatus(OnStatus, _ => { }));
        _subs.Add(api.SubscribeEnv(v => Update(s => s with { Env = v }), _ => { }));
        _ = SeedAsync();
    }

    private async Task SeedAsync()
    {
        try
        {
            OnStatus(await api.StatusAsync().ConfigureAwait(false));
            var live = await Try(api.LiveAsync()).ConfigureAwait(false);
            var env = await Try(api.EnvAsync()).ConfigureAwait(false);
            Update(s => s with { Live = s.Live ?? live, Env = s.Env ?? env });
        }
        catch
        {
            // Seeding is best effort; the subscriptions fill the state in anyway.
        }
    }

    private static async Task<T?> Try<T>(Task<T> t) where T : class
    {
        try { return await t.ConfigureAwait(false); } catch { return null; }
    }

    private void OnStatus(SystemState s)
    {
        Update(x => x with { Status = s });

        // The demodulation runs from boot; if a previous host stopped it, start
        // it again once so the live reading has something to show.
        bool check;
        lock (_gate)
        {
            check = !_demodChecked;
            _demodChecked = true;
        }
        if (check && !s.DemodRunning && !s.BulkActive)
            _ = api.SetTiltDemodAsync(true).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private void Update(Func<InstrumentSnapshot, InstrumentSnapshot> change)
    {
        InstrumentSnapshot next;
        lock (_gate)
        {
            if (_disposed) return;
            _state = next = change(_state);
        }
        Updated?.Invoke(this, next);
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
    }
}
