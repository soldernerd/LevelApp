using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// Request/response and subscription transport over an <see cref="ILeveltronicLink"/>
/// — the C# counterpart of the phone app's <c>DeviceLink</c>. Typed operations
/// live in <see cref="InstrumentApi"/>.
/// <para>
/// Requests are correlated by the echoed opcode; a second request for an opcode
/// that is still waiting fails the older one ("superseded"), so a late reply can
/// never be mistaken for the newer request's. SUBSCRIBE is acknowledged with a
/// status byte, then pushes <c>[OK][issue_seq][page][data]</c> follow under the
/// SUBSCRIBE opcode. One device subscription is shared between all listeners of
/// an opcode and ended (UNSUBSCRIBE) when the last one leaves.
/// </para>
/// </summary>
public sealed class LeveltronicDeviceClient : IAsyncDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly ILeveltronicLink _link;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<ushort, TaskCompletionSource<ApiFrame>> _pending = [];
    private readonly Dictionary<ushort, SubscriptionEntry> _subs = [];
    private bool _closed;

    // The instrument handles one request per scheduler tick: requests that arrive back to back
    // are dropped without an answer (seen over USB HID with firmware 0.15, where a SUBSCRIBE
    // immediately followed by an EXECUTE lost the EXECUTE). So only one request is in flight at
    // a time; the next is sent once the previous was answered (or timed out).
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public LeveltronicDeviceClient(ILeveltronicLink link, TimeProvider? time = null)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _time = time ?? TimeProvider.System;
        _link.PacketReceived += OnPacket;
        _link.ConnectionChanged += OnConnectionChanged;
    }

    public TimeProvider Time => _time;

    /// <summary>Raised once when the link goes down; pending work has been failed by then.</summary>
    public event EventHandler? Closed;

    // ── Requests ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends one request and waits for its reply. Throws <see cref="LinkException"/>
    /// if the link is down, the request is superseded or the device does not
    /// answer in time; any device status, including errors, comes back as an
    /// <see cref="ApiReply"/>.
    /// </summary>
    public async Task<ApiReply> RequestAsync(
        ushort opcode, ReadOnlyMemory<byte> payload = default,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var frame = await ExchangeAsync(opcode, payload, timeout, ct).ConfigureAwait(false);
        return new ApiReply(frame.Status, frame.Data);
    }

    private async Task<ApiFrame> ExchangeAsync(
        ushort opcode, ReadOnlyMemory<byte> payload, TimeSpan? timeout, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExchangeCoreAsync(opcode, payload, timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<ApiFrame> ExchangeCoreAsync(
        ushort opcode, ReadOnlyMemory<byte> payload, TimeSpan? timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ApiFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ApiFrame>? superseded;
        lock (_gate)
        {
            if (_closed) throw new LinkException("not connected");
            _pending.TryGetValue(opcode, out superseded);
            _pending[opcode] = tcs;
        }
        superseded?.TrySetException(new LinkException("superseded by a newer request"));

        try
        {
            await _link.SendAsync(ApiV3Codec.BuildFrame(opcode, payload.Span), ct).ConfigureAwait(false);

            using var timer = _time.CreateTimer(
                _ => tcs.TrySetException(new LinkException($"no response to opcode 0x{opcode:X4}")),
                null, timeout ?? DefaultTimeout, Timeout.InfiniteTimeSpan);
            using var reg = ct.Register(static s => ((TaskCompletionSource<ApiFrame>)s!).TrySetCanceled(), tcs);
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not LinkException and not OperationCanceledException)
        {
            throw new LinkException(ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                if (_pending.TryGetValue(opcode, out var cur) && ReferenceEquals(cur, tcs))
                    _pending.Remove(opcode);
            }
        }
    }

    // ── Subscriptions ────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to a resource. <paramref name="subscribeOpcode"/> is a SUBSCRIBE
    /// opcode; <paramref name="payload"/> is the request body (default
    /// <c>u32 intervalMs</c>). <paramref name="onPush"/> gets each push's data
    /// (after <c>[issue_seq][page]</c>); <paramref name="onError"/> fires if the
    /// device refuses; <paramref name="onClosed"/> when the link goes down.
    /// Disposing the returned handle removes the listener.
    /// </summary>
    public IDisposable Subscribe(
        ushort subscribeOpcode, Action<byte[]> onPush,
        Action<Exception>? onError = null, Action? onClosed = null,
        int intervalMs = 250, byte[]? payload = null)
    {
        var listener = new Listener(onPush, onError, onClosed);
        SubscriptionEntry entry;
        bool first;
        lock (_gate)
        {
            if (_closed)
            {
                _ = Task.Run(() => listener.Closed?.Invoke());
                return new Handle(() => { });
            }
            first = !_subs.TryGetValue(subscribeOpcode, out entry!);
            if (first)
            {
                entry = new SubscriptionEntry();
                _subs[subscribeOpcode] = entry;
            }
            entry.Listeners.Add(listener);
        }

        if (first)
            _ = StartSubscriptionAsync(subscribeOpcode, entry,
                payload ?? BitConverter.GetBytes((uint)intervalMs));

        return new Handle(() => Remove(subscribeOpcode, entry, listener));
    }

    private async Task StartSubscriptionAsync(ushort op, SubscriptionEntry entry, byte[] payload)
    {
        try
        {
            var ack = await ExchangeAsync(op, payload, null, CancellationToken.None).ConfigureAwait(false);
            if (ack.Status != ApiStatus.Ok)
                Fail(op, entry, new ApiException(ack.Status, "subscribe"));
        }
        catch (Exception ex)
        {
            Fail(op, entry, ex);
        }
    }

    private void Fail(ushort op, SubscriptionEntry entry, Exception ex)
    {
        Listener[] ls;
        lock (_gate)
        {
            if (_subs.TryGetValue(op, out var cur) && ReferenceEquals(cur, entry)) _subs.Remove(op);
            ls = [.. entry.Listeners];
            entry.Listeners.Clear();
        }
        foreach (var l in ls) Safe(() => l.Error?.Invoke(ex));
    }

    private void Remove(ushort op, SubscriptionEntry entry, Listener listener)
    {
        bool last;
        lock (_gate)
        {
            entry.Listeners.Remove(listener);
            last = entry.Listeners.Count == 0 && _subs.TryGetValue(op, out var cur) && ReferenceEquals(cur, entry);
            if (last) _subs.Remove(op);
        }
        if (last && !_closed) _ = SendUnsubscribeAsync(op);
    }

    private async Task SendUnsubscribeAsync(ushort subscribeOpcode)
    {
        try
        {
            // A request like any other: the device answers it, and waiting for that answer keeps
            // the next request from arriving in the same scheduler tick (it would be dropped).
            await ExchangeAsync(ApiV3Codec.WithVerb(subscribeOpcode, ApiVerb.Unsubscribe),
                ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* best effort: the device also clears subscriptions on disconnect */ }
    }

    // ── Incoming ─────────────────────────────────────────────────────────────

    private void OnPacket(object? sender, byte[] packet)
    {
        var frame = ApiV3Codec.Parse(packet);
        if (frame is not { CrcOk: true }) return;

        if (frame.Verb == ApiVerb.Subscribe && frame.LooksLikePush)
        {
            Listener[] ls;
            TaskCompletionSource<ApiFrame>? ack = null;
            lock (_gate)
            {
                // A push also proves the subscription was accepted.
                _pending.TryGetValue(frame.Opcode, out ack);
                ls = _subs.TryGetValue(frame.Opcode, out var e) ? [.. e.Listeners] : [];
            }
            ack?.TrySetResult(new ApiFrame(frame.Opcode, ApiStatus.Ok, [], true));
            var data = frame.PushData;
            foreach (var l in ls) Safe(() => l.Push(data));
            return;
        }

        TaskCompletionSource<ApiFrame>? waiting;
        lock (_gate) _pending.TryGetValue(frame.Opcode, out waiting);
        waiting?.TrySetResult(frame);
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        if (connected) return;
        Close();
    }

    private void Close()
    {
        List<TaskCompletionSource<ApiFrame>> pending;
        List<Listener> listeners = [];
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            pending = [.. _pending.Values];
            _pending.Clear();
            foreach (var e in _subs.Values) listeners.AddRange(e.Listeners);
            _subs.Clear();
        }
        foreach (var p in pending) p.TrySetException(new LinkException("link lost"));
        foreach (var l in listeners) Safe(() => l.Closed?.Invoke());
        Safe(() => Closed?.Invoke(this, EventArgs.Empty));
    }

    private static void Safe(Action a)
    {
        try { a(); } catch { /* a listener must not break the link */ }
    }

    public async ValueTask DisposeAsync()
    {
        _link.PacketReceived -= OnPacket;
        _link.ConnectionChanged -= OnConnectionChanged;
        Close();
        await _link.DisposeAsync().ConfigureAwait(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class SubscriptionEntry
    {
        public List<Listener> Listeners { get; } = [];
    }

    private sealed class Listener(Action<byte[]> push, Action<Exception>? error, Action? closed)
    {
        public Action<byte[]> Push { get; } = push;
        public Action<Exception>? Error { get; } = error;
        public Action? Closed { get; } = closed;
    }

    private sealed class Handle(Action dispose) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) dispose();
        }
    }
}
