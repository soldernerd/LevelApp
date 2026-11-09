using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// Typed operations on the instrument, built on a <see cref="LeveltronicDeviceClient"/>.
/// The wizards, live feed and views talk to the instrument only through this
/// class (the phone app's <c>InstrumentApi</c>).
/// </summary>
public sealed class InstrumentApi(LeveltronicDeviceClient client)
{
    public LeveltronicDeviceClient Client { get; } = client;

    private static ushort Get(ApiCategory c, byte r) => ApiV3Codec.Get(c, r);

    private async Task<byte[]> GetDataAsync(ushort op, string what, CancellationToken ct)
        => (await Client.RequestAsync(op, ct: ct).ConfigureAwait(false)).OrThrow(what).Data;

    private static T Decoded<T>(T? v, string what) where T : class =>
        v ?? throw new LinkException($"malformed {what} reply");

    // ── System ───────────────────────────────────────────────────────────────

    public async Task<DeviceIdentity> IdentityAsync(CancellationToken ct = default) =>
        Decoded(DeviceIdentity.Decode(await GetDataAsync(Get(ApiCategory.System, LeveltronicApi.Sys.Identity), "identity", ct)
            .ConfigureAwait(false)), "identity");

    /// <summary>
    /// Reads IDENTITY and throws <see cref="IncompatibleFirmwareException"/> unless
    /// it reports API v3 (an older firmware answers with an error or the short
    /// v2 payload), or <see cref="UnsupportedDeviceException"/> if it is not a Leveltronic.
    /// </summary>
    public async Task<DeviceIdentity> VerifyCompatibleAsync(CancellationToken ct = default)
    {
        var r = await Client.RequestAsync(Get(ApiCategory.System, LeveltronicApi.Sys.Identity), ct: ct).ConfigureAwait(false);
        var id = r.Ok ? DeviceIdentity.Decode(r.Data) : null;
        if (id is null) throw new IncompatibleFirmwareException(null, null);
        if (id.ApiVersion != ApiV3Codec.ApiVersion) throw new IncompatibleFirmwareException(id.ApiVersion, id.Firmware);
        if (id.Product != LeveltronicApi.Product) throw new UnsupportedDeviceException(id.Product);
        return id;
    }

    public async Task<SystemState> StateAsync(CancellationToken ct = default) =>
        Decoded(SystemState.Decode(await GetDataAsync(Get(ApiCategory.System, LeveltronicApi.Sys.State), "state", ct)
            .ConfigureAwait(false)), "state");

    public async Task<HealthInfo> HealthAsync(CancellationToken ct = default) =>
        Decoded(HealthInfo.Decode(await GetDataAsync(Get(ApiCategory.System, LeveltronicApi.Sys.Health), "health", ct)
            .ConfigureAwait(false)), "health");

    public async Task<RtcInfo> RtcAsync(CancellationToken ct = default) =>
        Decoded(RtcInfo.Decode(await GetDataAsync(Get(ApiCategory.System, LeveltronicApi.Sys.Rtc), "clock", ct)
            .ConfigureAwait(false)), "clock");

    public async Task SetRtcAsync(DateTime t, CancellationToken ct = default) =>
        (await Client.RequestAsync(ApiV3Codec.Set(ApiCategory.System, LeveltronicApi.Sys.Rtc), RtcInfo.EncodeSet(t), ct: ct)
            .ConfigureAwait(false)).OrThrow("set clock");

    // ── Topics (GET) ─────────────────────────────────────────────────────────

    public async Task<LiveTilt> LiveAsync(CancellationToken ct = default) =>
        Decoded(LiveTilt.Decode(await GetDataAsync(Get(ApiCategory.Topics, LeveltronicApi.Topic.Live), "live", ct)
            .ConfigureAwait(false)), "live");

    public async Task<SystemState> StatusAsync(CancellationToken ct = default) =>
        Decoded(SystemState.Decode(await GetDataAsync(Get(ApiCategory.Topics, LeveltronicApi.Topic.Status), "status", ct)
            .ConfigureAwait(false)), "status");

    public async Task<Environmental> EnvAsync(CancellationToken ct = default) =>
        Decoded(Environmental.Decode(await GetDataAsync(Get(ApiCategory.Topics, LeveltronicApi.Topic.Env), "environment", ct)
            .ConfigureAwait(false)), "environment");

    // ── Settings / calibrations ──────────────────────────────────────────────

    /// <summary>Reads one value. Throws <see cref="ApiException"/> on any non-OK status.</summary>
    public async Task<long> ReadValueAsync(ValueDef def, CancellationToken ct = default)
    {
        var data = await GetDataAsync(def.GetOpcode, def.Label, ct).ConfigureAwait(false);
        if (!def.Type.Covers(data)) throw new LinkException($"short {def.Label}");
        return def.Type.Decode(data);
    }

    /// <summary>Like <see cref="ReadValueAsync"/>, but a firmware without this (optional) resource gives null.</summary>
    public async Task<long?> TryReadValueAsync(ValueDef def, CancellationToken ct = default)
    {
        var r = await Client.RequestAsync(def.GetOpcode, ct: ct).ConfigureAwait(false);
        if (r.Status == ApiStatus.UnknownResource) return null;
        r.OrThrow(def.Label);
        if (!def.Type.Covers(r.Data)) throw new LinkException($"short {def.Label}");
        return def.Type.Decode(r.Data);
    }

    /// <summary>Writes one value; the device persists it immediately.</summary>
    public async Task WriteValueAsync(ValueDef def, long value, CancellationToken ct = default) =>
        (await Client.RequestAsync(def.SetOpcode, def.Type.Encode(value), ct: ct).ConfigureAwait(false)).OrThrow(def.Label);

    // ── Commands ─────────────────────────────────────────────────────────────

    private async Task ExecAsync(byte res, string what, byte[]? payload = null, CancellationToken ct = default) =>
        (await Client.RequestAsync(ApiV3Codec.Execute(res), payload ?? [], ct: ct).ConfigureAwait(false)).OrThrow(what);

    public Task PowerOffAsync(CancellationToken ct = default) => ExecAsync(LeveltronicApi.Cmd.PowerOff, "power off", ct: ct);
    public Task RebootAsync(CancellationToken ct = default) => ExecAsync(LeveltronicApi.Cmd.Reboot, "reboot", ct: ct);
    public Task RebootToDfuAsync(CancellationToken ct = default) => ExecAsync(LeveltronicApi.Cmd.RebootDfu, "reboot to DFU", ct: ct);
    public Task ForceChargeAsync(CancellationToken ct = default) => ExecAsync(LeveltronicApi.Cmd.ForceCharge, "force charge", ct: ct);
    public Task EndChargingAsync(CancellationToken ct = default) => ExecAsync(LeveltronicApi.Cmd.EndCharging, "end charging", ct: ct);
    public Task SetChargeInhibitAsync(bool inhibit, CancellationToken ct = default) =>
        ExecAsync(LeveltronicApi.Cmd.ChargeInhibit, "charge inhibit", [(byte)(inhibit ? 1 : 0)], ct);
    public Task SetTiltDemodAsync(bool run, CancellationToken ct = default) =>
        ExecAsync(LeveltronicApi.Cmd.TiltDemod, "tilt demodulation", [(byte)(run ? 1 : 0)], ct);
    public Task TestBeepAsync(CancellationToken ct = default) => ExecAsync(LeveltronicApi.Cmd.TestBeep, "beep", ct: ct);
    public Task ClearCountersAsync(CancellationToken ct = default) => ExecAsync(LeveltronicApi.Cmd.ClearCounters, "clear counters", ct: ct);
    public Task ServiceEndAsync(CancellationToken ct = default) => ExecAsync(LeveltronicApi.Cmd.ServiceEnd, "leave service mode", ct: ct);
    public Task FactoryDefaultsAsync(CancellationToken ct = default) =>
        ExecAsync(LeveltronicApi.Cmd.FactoryDefaults, "factory defaults", [LeveltronicApi.FactoryDefaultsKey], ct);

    // ── Procedures ───────────────────────────────────────────────────────────

    /// <summary>Flip calibration step 1 on the sensors in <paramref name="sensorMask"/> (bit0 S1, bit1 S2).</summary>
    public Task ZeroCalStep1Async(int sensorMask, CancellationToken ct = default) =>
        ExecAsync(LeveltronicApi.Cmd.ZeroCal, "flip calibration", [1, (byte)sensorMask], ct);
    public Task ZeroCalStep2Async(CancellationToken ct = default) =>
        ExecAsync(LeveltronicApi.Cmd.ZeroCal, "flip calibration", [2], ct);
    public Task ZeroCalCancelAsync(CancellationToken ct = default) =>
        ExecAsync(LeveltronicApi.Cmd.ZeroCal, "cancel flip calibration", [0], ct);

    public Task PrecisionStartAsync(CancellationToken ct = default) =>
        ExecAsync(LeveltronicApi.Cmd.Precision, "precision measurement", [0], ct);
    public Task PrecisionCancelAsync(CancellationToken ct = default) =>
        ExecAsync(LeveltronicApi.Cmd.Precision, "cancel precision measurement", [1], ct);

    public async Task<ZeroCalStatus> ZeroCalStatusAsync(CancellationToken ct = default) =>
        Decoded(ZeroCalStatus.Decode(await GetDataAsync(Get(ApiCategory.Procedures, LeveltronicApi.Proc.ZeroCal), "flip calibration", ct)
            .ConfigureAwait(false)), "flip calibration");

    public async Task<PrecisionStatus> PrecisionStatusAsync(CancellationToken ct = default) =>
        Decoded(PrecisionStatus.Decode(await GetDataAsync(Get(ApiCategory.Procedures, LeveltronicApi.Proc.Precision), "precision", ct)
            .ConfigureAwait(false)), "precision");

    // ── Subscriptions ────────────────────────────────────────────────────────

    private IDisposable Sub<T>(
        ApiCategory cat, byte res, Func<byte[], T?> decode, Action<T> onUpdate,
        Action<Exception>? onError, Action? onClosed, int intervalMs = 250) where T : class =>
        Client.Subscribe(ApiV3Codec.Subscribe(cat, res),
            d => { if (decode(d) is { } v) onUpdate(v); },
            onError, onClosed, intervalMs);

    public IDisposable SubscribeLive(Action<LiveTilt> onUpdate, Action<Exception>? onError = null, Action? onClosed = null) =>
        Sub<LiveTilt>(ApiCategory.Topics, LeveltronicApi.Topic.Live, d => LiveTilt.Decode(d), onUpdate, onError, onClosed);

    public IDisposable SubscribeStatus(Action<SystemState> onUpdate, Action<Exception>? onError = null,
        Action? onClosed = null, int intervalMs = 1000) =>
        Sub<SystemState>(ApiCategory.Topics, LeveltronicApi.Topic.Status, d => SystemState.Decode(d), onUpdate, onError, onClosed, intervalMs);

    public IDisposable SubscribeEnv(Action<Environmental> onUpdate, Action<Exception>? onError = null,
        Action? onClosed = null, int intervalMs = 2000) =>
        Sub<Environmental>(ApiCategory.Topics, LeveltronicApi.Topic.Env, d => Environmental.Decode(d), onUpdate, onError, onClosed, intervalMs);

    public IDisposable SubscribeSignalDiag(Action<SignalDiag> onUpdate, Action<Exception>? onError = null,
        Action? onClosed = null, int intervalMs = 500) =>
        Sub<SignalDiag>(ApiCategory.Topics, LeveltronicApi.Topic.SignalDiag, d => SignalDiag.Decode(d), onUpdate, onError, onClosed, intervalMs);

    public IDisposable SubscribePhasorStream(Action<PhasorEntry> onUpdate, Action<Exception>? onError = null, Action? onClosed = null) =>
        Sub<PhasorEntry>(ApiCategory.Topics, LeveltronicApi.Topic.PhasorStream, d => PhasorEntry.Decode(d), onUpdate, onError, onClosed);

    public IDisposable SubscribeZeroCal(Action<ZeroCalStatus> onUpdate, Action<Exception>? onError = null, Action? onClosed = null) =>
        Sub<ZeroCalStatus>(ApiCategory.Procedures, LeveltronicApi.Proc.ZeroCal, d => ZeroCalStatus.Decode(d), onUpdate, onError, onClosed);

    public IDisposable SubscribePrecision(Action<PrecisionStatus> onUpdate, Action<Exception>? onError = null, Action? onClosed = null) =>
        Sub<PrecisionStatus>(ApiCategory.Procedures, LeveltronicApi.Proc.Precision, d => PrecisionStatus.Decode(d), onUpdate, onError, onClosed);
}
