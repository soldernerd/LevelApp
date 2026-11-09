using LevelApp.Core.Instruments;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// An open, verified Leveltronic: link + client + typed API + the always-on
/// <see cref="LiveFeed"/>, shared by everything that talks to the instrument
/// (guided measurement, management view, calibration wizards). It lives until
/// the last <see cref="SessionLease"/> is released or the link drops.
/// </summary>
public sealed class LeveltronicSession : IAsyncDisposable
{
    private static readonly TimeSpan ServiceKeepAliveInterval = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time;
    private int _closed;

    private LeveltronicSession(
        KnownDevice device, DeviceIdentity identity, LeveltronicDeviceClient client, TimeProvider time)
    {
        Device = device;
        Identity = identity;
        Client = client;
        Api = new InstrumentApi(client);
        Feed = new LiveFeed(Api);
        _time = time;
        client.Closed += (_, _) => RaiseClosed();
    }

    public KnownDevice Device { get; }
    public DeviceIdentity Identity { get; }
    public LeveltronicDeviceClient Client { get; }
    public InstrumentApi Api { get; }
    public LiveFeed Feed { get; }

    /// <summary>The instrument serial; the transport address if the firmware reports none.</summary>
    public string Serial => Identity.Serial.Length > 0 ? Identity.Serial : Device.TransportAddress;

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Raised once when the link is lost or the session is closed.</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Connects <paramref name="link"/>, checks the firmware speaks API v3 and is a
    /// Leveltronic, and starts the live feed.
    /// </summary>
    /// <exception cref="ConnectFailureException">Incompatible or unsupported device.</exception>
    public static async Task<LeveltronicSession> OpenAsync(
        KnownDevice device, ILeveltronicLink link, TimeProvider? time = null, CancellationToken ct = default)
    {
        time ??= TimeProvider.System;
        var client = new LeveltronicDeviceClient(link, time);
        try
        {
            await link.ConnectAsync(ct).ConfigureAwait(false);
            var identity = await new InstrumentApi(client).VerifyCompatibleAsync(ct).ConfigureAwait(false);
            var session = new LeveltronicSession(device, identity, client, time);
            session.Feed.Start();
            return session;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Service mode ends after 10 minutes without a request. While a calibration
    /// surface is open this sends a cheap request every 30 s; dispose to stop.
    /// </summary>
    public IDisposable KeepServiceModeAlive()
    {
        var timer = _time.CreateTimer(
            _ => _ = Api.StateAsync().ContinueWith(_ => { }, TaskScheduler.Default),
            null, ServiceKeepAliveInterval, ServiceKeepAliveInterval);
        return timer;
    }

    private void RaiseClosed()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        Feed.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        RaiseClosed();
        await Client.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>A reference to a shared <see cref="LeveltronicSession"/>; dispose to release it.</summary>
public sealed class SessionLease : IAsyncDisposable
{
    private readonly Func<ValueTask> _release;
    private int _released;

    internal SessionLease(LeveltronicSession session, Func<ValueTask> release)
    {
        Session = session;
        _release = release;
    }

    public LeveltronicSession Session { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            await _release().ConfigureAwait(false);
    }
}

/// <summary>
/// Hands out reference-counted <see cref="LeveltronicSession"/>s, one per
/// physical instrument. USB HID and a GATT link cannot be opened twice, and the
/// guided measurement, the management view and the wizards all need the same
/// live data, so they share.
/// </summary>
public sealed class LeveltronicSessionManager(
    Func<KnownDevice, ILeveltronicLink> linkFactory, TimeProvider? time = null)
{
    private sealed class Slot
    {
        public required Task<LeveltronicSession> Open { get; init; }
        public int Refs;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Slot> _slots = [];
    private readonly Dictionary<string, Task> _closing = [];
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Open (or join) the session of <paramref name="device"/>.</summary>
    public async Task<SessionLease> AcquireAsync(KnownDevice device, CancellationToken ct = default)
    {
        // A session that is still shutting down holds the HID handle / GATT
        // link; wait for it before opening a new one.
        Task? closing;
        lock (_gate) _closing.TryGetValue(device.DeviceId, out closing);
        if (closing is not null) await closing.WaitAsync(ct).ConfigureAwait(false);

        Slot slot;
        lock (_gate)
        {
            if (!_slots.TryGetValue(device.DeviceId, out slot!))
            {
                slot = new Slot { Open = OpenAsync(device) };
                _slots[device.DeviceId] = slot;
            }
            slot.Refs++;
        }

        try
        {
            var session = await slot.Open.WaitAsync(ct).ConfigureAwait(false);
            return new SessionLease(session, () => ReleaseAsync(device.DeviceId, slot));
        }
        catch
        {
            await ReleaseAsync(device.DeviceId, slot).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<LeveltronicSession> OpenAsync(KnownDevice device)
    {
        var session = await LeveltronicSession.OpenAsync(device, linkFactory(device), _time).ConfigureAwait(false);
        session.Closed += (_, _) => Forget(device.DeviceId, session);
        return session;
    }

    private void Forget(string deviceId, LeveltronicSession session)
    {
        lock (_gate)
        {
            if (_slots.TryGetValue(deviceId, out var s) && s.Open.IsCompletedSuccessfully && ReferenceEquals(s.Open.Result, session))
                _slots.Remove(deviceId);
        }
    }

    private async ValueTask ReleaseAsync(string deviceId, Slot slot)
    {
        bool last;
        lock (_gate)
        {
            last = --slot.Refs == 0;
            if (last && _slots.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, slot))
                _slots.Remove(deviceId);
        }
        if (!last) return;

        var close = CloseAsync(slot);
        lock (_gate) _closing[deviceId] = close;
        try { await close.ConfigureAwait(false); }
        finally
        {
            lock (_gate)
                if (_closing.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, close))
                    _closing.Remove(deviceId);
        }
    }

    private static async Task CloseAsync(Slot slot)
    {
        try
        {
            var session = await slot.Open.ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch { /* the open failed; nothing to close */ }
    }
}
