using System.Buffers.Binary;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>
/// An in-memory instrument that speaks API v3 at the frame level — a C# port of
/// the phone app's <c>simulated_device.dart</c>. It models the sensor physics
/// (excitation phasors, phase / k / zero calibration, invert), the flip-calibration
/// and precision-measurement state machines, service-mode gating, settings
/// ranges and the topic subscriptions, so the client, feed and wizards run
/// end to end against a known truth.
/// <para>
/// Physics: each sensor sees a physical tilt <c>T</c> (mm/m, native sign). The
/// carrier phasors are <c>A = 1000·e^{jθ}</c>, <c>B = −A</c>,
/// <c>S = PGA·(A−B)·u</c> with <c>u = (zeroTrue + T·kTrue)·e^{jφTrue}</c>, and the
/// "firmware" computes, from the <i>stored</i> calibration, <c>x = u·e^{−jφcal}</c>,
/// <c>tilt = (Re x − zero) / k</c> — exactly <c>Math/math_displacement.c</c>.
/// </para>
/// All timing runs on the supplied <see cref="TimeProvider"/> (use FakeTimeProvider).
/// </summary>
internal sealed class SimulatedLeveltronicLink : ILeveltronicLink
{
    private readonly TimeProvider _time;
    private readonly Random _rng = new(1);
    private readonly object _gate = new();

    public SimulatedLeveltronicLink(
        TimeProvider time, int apiVersion = ApiV3Codec.ApiVersion,
        double noise = 0, string product = LeveltronicApi.Product)
    {
        _time = time;
        ApiVersion = apiVersion;
        Noise = noise;
        Product = product;
        _boot = time.GetUtcNow();
        ResetDefaults();
    }

    public int ApiVersion { get; }

    /// <summary>The API minor reported in IDENTITY (3.1 = firmware 0.15.0).</summary>
    public int ApiMinor { get; set; } = ApiV3Codec.KnownApiMinor;
    public double Noise { get; }
    public string Product { get; }
    public bool SupportsChargeFullTimeout { get; init; } = true;

    // ── Test hooks ───────────────────────────────────────────────────────────

    /// <summary>What each sensor physically sees, mm/m in its native sign.</summary>
    public double[] PhysicalTilt { get; set; } = [0.8000, 0.5000];

    /// <summary>The instrument's true electrical parameters (what calibration should converge to).</summary>
    public double[] TrueK { get; set; } = [0.050, 0.048];
    public double[] TrueZero { get; set; } = [0.0012, -0.0008];
    public double[] TruePhaseDeg { get; set; } = [12.0, 10.5];

    /// <summary>Marks the readings doubtful (vibration).</summary>
    public bool Disturbed { get; set; }

    /// <summary>Makes a precision measurement never find a clean window.</summary>
    public bool PrecisionNeverClean { get; set; }

    /// <summary>How many precision measurements were started.</summary>
    public int PrecisionRuns { get; private set; }

    /// <summary>Phasor pushes stop (to provoke "too few batches").</summary>
    public bool PhasorSilent { get; set; }

    public bool ServiceMode => _serviceMode;

    /// <summary>Service mode can only be entered on the instrument; this is its stand-in.</summary>
    public void SetServiceMode(bool on)
    {
        _serviceMode = on;
        TouchServiceTimer();
    }

    /// <summary>The battery / charger.</summary>
    public void SetBattery(int? soc = null, int? mv = null, bool? usb = null, bool? charging = null)
    {
        if (soc is { } s) _soc = s;
        if (mv is { } m) _mv = m;
        if (usb is { } u) _usb = u;
        if (charging is { } c) _charging = c;
    }

    public long Value(ValueDef def) => _values[Key(def)];
    public void SetValue(ValueDef def, long v) => _values[Key(def)] = v;

    public int SentCount { get; private set; }
    public List<ushort> Requests { get; } = [];

    // ── ILeveltronicLink ─────────────────────────────────────────────────────

    public bool IsConnected { get; private set; }
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<byte[]>? PacketReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        ConnectionChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        LinkDown();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        StopAll();
        return ValueTask.CompletedTask;
    }

    /// <summary>The instrument vanishes: all subscriptions end.</summary>
    public void LinkDown()
    {
        StopAll();
        _serviceMode = false;
        _zPhase = ZeroCalPhase.Idle;
        _pPhase = PrecisionPhase.Idle;
        if (IsConnected)
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, false);
        }
    }

    public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("not connected");
        var f = ApiV3Codec.Parse(frame.Span);
        if (f is null || !f.CrcOk) return Task.CompletedTask;

        // The parser treats the first payload byte as a status; for requests it is data.
        var payload = RequestPayload(frame.Span);
        lock (_gate)
        {
            SentCount++;
            Requests.Add(f.Opcode);
            TouchServiceTimer();
            Dispatch(f.Opcode, payload);
        }
        return Task.CompletedTask;
    }

    private static byte[] RequestPayload(ReadOnlySpan<byte> frame)
    {
        int len = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(2, 2));
        return frame.Slice(ApiV3Codec.HeaderBytes, len).ToArray();
    }

    // ── State ────────────────────────────────────────────────────────────────

    private readonly Dictionary<int, long> _values = [];
    private static int Key(ValueDef d) => ((int)d.Category << 8) | d.Id;

    private bool _serviceMode;
    private ITimer? _serviceTimer;

    private bool _demodRunning = true;
    private bool _phasorActive;
    private int _soc = 85;
    private int _mv = 4020;
    private bool _usb;
    private bool _charging, _forceCharging, _chargeInhibited;

    private TimeSpan _rtcOffset = TimeSpan.Zero;
    private bool _rtcSet;
    private readonly DateTimeOffset _boot;

    private int _liveSeq;
    private ITimer? _liveTimer;

    // Procedures
    private ZeroCalPhase _zPhase = ZeroCalPhase.Idle;
    private int _zProgress;
    private int _zMask = 3;
    private const int ZTarget = 64;
    private readonly double[] _zAvg1 = [0, 0];
    private ITimer? _zTimer;

    private PrecisionPhase _pPhase = PrecisionPhase.Idle;
    private int _pCount, _pElapsed;
    private bool _pFailed;
    private double[] _pResult = [0, 0, 0];
    private ITimer? _pTimer;
    private const int PTarget = 81;

    private bool _rotated; // physical tilt negated during flip step 2

    private sealed class Sub
    {
        public ITimer? Timer;
        public byte Seq;
    }
    private readonly Dictionary<ushort, Sub> _subs = [];

    private void ResetDefaults()
    {
        _values.Clear();
        foreach (var d in ValueCatalog.Settings)
            _values[Key(d)] = d.Id switch
            {
                0x60 => 300, 0x52 => 3700, 0x51 => 3600, 0x50 => 3400, 0x53 => 120, 0x54 => 97, 0x61 => 4,
                0x40 => 10, 0x41 => 20, 0x42 => 20, 0x43 => 1000, 0x44 => 1000,
                _ => d.Min,
            };

        // Deliberately not equal to the "true" parameters so the wizards have something to correct.
        _values[Key(ValueCatalog.S1K)] = 45000;
        _values[Key(ValueCatalog.S1Zero)] = 500;
        _values[Key(ValueCatalog.S1Phase)] = 1000;
        _values[Key(ValueCatalog.S1Invert)] = 0;
        _values[Key(ValueCatalog.S2K)] = 50000;
        _values[Key(ValueCatalog.S2Zero)] = -300;
        _values[Key(ValueCatalog.S2Phase)] = 900;
        _values[Key(ValueCatalog.S2Invert)] = 0;
        foreach (var d in ValueCatalog.Calibrations)
            _values.TryAdd(Key(d), d.Id switch
            {
                0x50 or 0x51 => 1000,
                0x48 or 0x49 or 0x4A or 0x4B => 1,
                0x61 or 0x62 or 0x65 or 0x66 => 1000,
                0x68 => 10,
                0x63 => 1500,
                _ => 0,
            });
    }

    // ── Physics ──────────────────────────────────────────────────────────────

    private double EffectiveTilt(int i) => _rotated ? -PhysicalTilt[i] : PhysicalTilt[i];

    private (double re, double im) U(int i, double t)
    {
        double mag = TrueZero[i] + t * TrueK[i];
        double ph = TruePhaseDeg[i] * Math.PI / 180;
        return (mag * Math.Cos(ph), mag * Math.Sin(ph));
    }

    /// <summary>What the firmware would report for sensor i at tilt t with the stored calibration.</summary>
    private double Reading(int i, double t, bool applyInvert = true)
    {
        var (ur, ui) = U(i, t);
        double phCal = _values[Key(ValueCatalog.SensorPhase[i])] * Math.PI / 18000;
        double xRe = ur * Math.Cos(phCal) + ui * Math.Sin(phCal);
        double k = _values[Key(ValueCatalog.SensorK[i])] * 1e-6;
        double zero = _values[Key(ValueCatalog.SensorZero[i])] * 1e-6;
        double native = (xRe - zero) / k;
        bool inv = _values[Key(ValueCatalog.SensorInvert[i])] == 1;
        return applyInvert && inv ? -native : native;
    }

    private double NoiseValue() => Noise == 0 ? 0 : (_rng.NextDouble() * 2 - 1) * Noise;

    // ── Output ───────────────────────────────────────────────────────────────

    private void Reply(ushort op, ApiStatus status, byte[]? data = null)
    {
        byte[] payload = [(byte)status, .. data ?? []];
        PacketReceived?.Invoke(this, ApiV3Codec.BuildFrame(op, payload));
    }

    private void Push(ushort op, byte[] data)
    {
        if (!_subs.TryGetValue(op, out var sub)) return;
        byte[] payload = [(byte)ApiStatus.Ok, sub.Seq++, 0, .. data];
        PacketReceived?.Invoke(this, ApiV3Codec.BuildFrame(op, payload));
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────

    private void Dispatch(ushort op, byte[] p)
    {
        var verb = ApiV3Codec.VerbOf(op);
        var cat = ApiV3Codec.CategoryOf(op);
        byte res = ApiV3Codec.ResourceOf(op);

        if (verb == ApiVerb.Subscribe) { Subscribe(op, cat, res, p); return; }
        if (verb == ApiVerb.Unsubscribe) { Unsubscribe(op); return; }

        (ApiStatus, byte[]?) r = cat switch
        {
            ApiCategory.System => SystemResource(verb, res, p),
            ApiCategory.Commands => verb == ApiVerb.Execute ? Command(res, p) : (ApiStatus.VerbNotValid, null),
            ApiCategory.Calibrations or ApiCategory.Settings => ValueResource(verb, cat, res, p),
            ApiCategory.Topics => verb != ApiVerb.Get ? (ApiStatus.VerbNotValid, null) : TopicGet(res),
            ApiCategory.Procedures => verb != ApiVerb.Get ? (ApiStatus.VerbNotValid, null) : ProcedureGet(res),
            ApiCategory.Measurements or ApiCategory.DebugLog or ApiCategory.Diagnostics or ApiCategory.Bulk
                => (ApiStatus.UnknownResource, null),
            _ => (ApiStatus.UnknownCategory, null),
        };
        Reply(op, r.Item1, r.Item2);
    }

    private (ApiStatus, byte[]?) SystemResource(ApiVerb verb, byte res, byte[] p)
    {
        switch (res)
        {
            case LeveltronicApi.Sys.Identity:
            {
                if (verb != ApiVerb.Get) return (ApiStatus.VerbNotValid, null);
                if (ApiVersion < 3)
                {
                    // The short v2 layout: fw[3] product[16].
                    var v2 = new byte[19];
                    v2[0] = 0; v2[1] = 9; v2[2] = 0;
                    PutStr(v2, 3, 16, Product);
                    return (ApiStatus.Ok, v2);
                }
                var b = new byte[51];
                b[0] = 1; b[1] = 0; b[2] = 0; b[3] = (byte)ApiVersion;
                b[50] = (byte)ApiMinor;
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), ApiV3Codec.MaxPayload);
                PutStr(b, 6, 16, Product);
                PutStr(b, 22, 8, "6DA08073");
                PutStr(b, 30, 20, "1371e8b0");
                return (ApiStatus.Ok, b);
            }
            case LeveltronicApi.Sys.State:
                return verb == ApiVerb.Get ? (ApiStatus.Ok, StateBytes()) : (ApiStatus.VerbNotValid, null);
            case LeveltronicApi.Sys.Health:
            {
                if (verb != ApiVerb.Get) return (ApiStatus.VerbNotValid, null);
                var b = new byte[31];
                b[0] = b[1] = b[2] = b[3] = b[4] = 1;
                b[5] = 1;
                b[8] = 2; // power-on
                b[9] = (byte)(_rtcSet ? 1 : 0);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), (uint)(_time.GetUtcNow() - _boot).TotalSeconds);
                return (ApiStatus.Ok, b);
            }
            case LeveltronicApi.Sys.Rtc:
            {
                if (verb == ApiVerb.Get) return (ApiStatus.Ok, RtcBytes());
                if (verb != ApiVerb.Set) return (ApiStatus.VerbNotValid, null);
                if (p.Length != 7) return (ApiStatus.BadLength, null);
                int year = BinaryPrimitives.ReadUInt16LittleEndian(p);
                if (year < 2000 || year > 2099) return (ApiStatus.InvalidParameter, null);
                var t = new DateTime(year, p[2], p[3], p[4], p[5], p[6]);
                _rtcOffset = t - _time.GetUtcNow().UtcDateTime;
                _rtcSet = true;
                return (ApiStatus.Ok, null);
            }
            default:
                return (ApiStatus.UnknownResource, null);
        }
    }

    private static void PutStr(byte[] b, int off, int len, string s)
    {
        for (int i = 0; i < len && i < s.Length; i++) b[off + i] = (byte)s[i];
    }

    private byte[] StateBytes()
    {
        var b = new byte[15];
        long low = _values[Key(ValueCatalog.Settings.First(d => d.Id == 0x51))];
        var st = _charging ? BatteryState.Charging : _mv < low ? BatteryState.Low : BatteryState.Normal;
        b[0] = (byte)st;
        b[1] = (byte)_soc;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), (ushort)_mv);
        b[4] = (byte)(_usb ? 1 : 0);
        b[5] = 1;
        b[6] = (byte)(_charging ? 1 : 0);
        b[7] = (byte)(_forceCharging ? 1 : 0);
        b[8] = (byte)(_chargeInhibited ? 1 : 0);
        b[9] = 1; b[10] = 1;
        b[11] = (byte)(_demodRunning ? 1 : 0);
        b[12] = (byte)(_phasorActive ? 1 : 0);
        b[13] = 0;
        b[14] = (byte)(_serviceMode ? 1 : 0);
        return b;
    }

    private byte[] RtcBytes()
    {
        var t = _time.GetUtcNow().UtcDateTime + _rtcOffset;
        var b = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)t.Year);
        b[2] = (byte)t.Month; b[3] = (byte)t.Day;
        b[4] = (byte)(t.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)t.DayOfWeek);
        b[5] = (byte)t.Hour; b[6] = (byte)t.Minute; b[7] = (byte)t.Second;
        b[8] = (byte)(_rtcSet ? 1 : 0);
        b[9] = (byte)(t.Millisecond * 256 / 1000);
        return b;
    }

    // ── Settings / calibrations ──────────────────────────────────────────────

    private ValueDef? Def(ApiCategory cat, byte res)
    {
        var list = cat == ApiCategory.Settings ? ValueCatalog.Settings : ValueCatalog.Calibrations;
        var d = list.FirstOrDefault(x => x.Id == res);
        if (d is { Optional: true } && !SupportsChargeFullTimeout) return null;
        return d;
    }

    private (ApiStatus, byte[]?) ValueResource(ApiVerb verb, ApiCategory cat, byte res, byte[] p)
    {
        var def = Def(cat, res);
        if (def is null) return (ApiStatus.UnknownResource, null);
        if (verb == ApiVerb.Get) return (ApiStatus.Ok, def.Type.Encode(_values[Key(def)]));
        if (verb != ApiVerb.Set) return (ApiStatus.VerbNotValid, null);
        if (p.Length != def.Type.Bytes()) return (ApiStatus.BadLength, null);
        if (def.Service && !_serviceMode) return (ApiStatus.ServiceModeRequired, null);

        long v = def.Type.Decode(p);
        if (v < def.Min || v > def.Max) return (ApiStatus.InvalidParameter, null);
        // Cross-field rule: critical < low.
        if (cat == ApiCategory.Settings && res == 0x50 &&
            v >= _values[Key(ValueCatalog.Settings.First(d => d.Id == 0x51))])
            return (ApiStatus.InvalidParameter, null);
        if (cat == ApiCategory.Settings && res == 0x51 &&
            v <= _values[Key(ValueCatalog.Settings.First(d => d.Id == 0x50))])
            return (ApiStatus.InvalidParameter, null);
        _values[Key(def)] = v;
        return (ApiStatus.Ok, null);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private (ApiStatus, byte[]?) NeedService(Func<(ApiStatus, byte[]?)> body) =>
        _serviceMode ? body() : (ApiStatus.ServiceModeRequired, null);

    private (ApiStatus, byte[]?) Command(byte res, byte[] p)
    {
        switch (res)
        {
            case LeveltronicApi.Cmd.PowerOff:
            case LeveltronicApi.Cmd.Reboot:
                _ = Task.Run(LinkDown);
                return (ApiStatus.Ok, null);
            case LeveltronicApi.Cmd.RebootDfu:
                return NeedService(() => { _ = Task.Run(LinkDown); return (ApiStatus.Ok, null); });
            case LeveltronicApi.Cmd.ForceCharge:
                if (_usb) _forceCharging = true;
                return (ApiStatus.Ok, null);
            case LeveltronicApi.Cmd.EndCharging:
                _forceCharging = false;
                return (ApiStatus.Ok, null);
            case LeveltronicApi.Cmd.ChargeInhibit:
                if (p.Length != 1) return (ApiStatus.BadLength, null);
                _chargeInhibited = p[0] != 0;
                if (_chargeInhibited) _forceCharging = false;
                return (ApiStatus.Ok, null);
            case LeveltronicApi.Cmd.TiltDemod:
                if (p.Length != 1) return (ApiStatus.BadLength, null);
                if (_phasorActive) return (ApiStatus.BusyExclusive, null);
                _demodRunning = p[0] != 0;
                return (ApiStatus.Ok, null);
            case LeveltronicApi.Cmd.ZeroCal:
                return NeedService(() => ZeroCalCommand(p));
            case LeveltronicApi.Cmd.Precision:
                return PrecisionCommand(p);
            case LeveltronicApi.Cmd.FactoryDefaults:
                return NeedService(() =>
                {
                    if (p.Length != 1) return (ApiStatus.BadLength, null);
                    if (p[0] != LeveltronicApi.FactoryDefaultsKey) return (ApiStatus.InvalidParameter, null);
                    ResetDefaults();
                    return (ApiStatus.Ok, null);
                });
            case LeveltronicApi.Cmd.ClearCounters:
            case LeveltronicApi.Cmd.TestBeep:
                return (ApiStatus.Ok, null);
            case LeveltronicApi.Cmd.ServiceEnd:
                _serviceMode = false;
                _serviceTimer?.Dispose();
                return (ApiStatus.Ok, null);
            default:
                return (ApiStatus.UnknownResource, null);
        }
    }

    // ── Zero (flip) calibration ──────────────────────────────────────────────

    private (ApiStatus, byte[]?) ZeroCalCommand(byte[] p)
    {
        if (p.Length is 0 or > 2) return (ApiStatus.BadLength, null);
        switch (p[0])
        {
            case 0:
                _zTimer?.Dispose();
                _zPhase = ZeroCalPhase.Idle;
                _zProgress = 0;
                _rotated = false;
                PushZero();
                return (ApiStatus.Ok, null);
            case 1:
            {
                if (!_demodRunning || _pPhase == PrecisionPhase.Running) return (ApiStatus.BusyResource, null);
                int mask = p.Length > 1 ? p[1] : 3;
                if (mask is < 1 or > 3) return (ApiStatus.InvalidParameter, null);
                _zMask = mask;
                _zPhase = ZeroCalPhase.Step1Running;
                _zProgress = 0;
                _rotated = false;
                StartZeroTimer(step: 1);
                PushZero();
                return (ApiStatus.Ok, null);
            }
            case 2:
                if (_zPhase != ZeroCalPhase.Step1Done) return (ApiStatus.BusyResource, null);
                _zPhase = ZeroCalPhase.Step2Running;
                _zProgress = 0;
                _rotated = true; // the user turned the instrument by 180 degrees
                StartZeroTimer(step: 2);
                PushZero();
                return (ApiStatus.Ok, null);
            default:
                return (ApiStatus.InvalidParameter, null);
        }
    }

    private void StartZeroTimer(int step)
    {
        _zTimer?.Dispose();
        var period = TimeSpan.FromMilliseconds(100);
        _zTimer = _time.CreateTimer(_ =>
        {
            lock (_gate)
            {
                _zProgress = Math.Min(_zProgress + 8, ZTarget);
                if (_zProgress < ZTarget) { PushZero(); return; }
                _zTimer?.Dispose();
                if (step == 1)
                {
                    for (int i = 0; i < 2; i++) _zAvg1[i] = Reading(i, EffectiveTilt(i), applyInvert: false);
                    _zPhase = ZeroCalPhase.Step1Done;
                    PushZero();
                }
                else
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if ((_zMask & (1 << i)) == 0) continue;
                        double avg2 = Reading(i, EffectiveTilt(i), applyInvert: false);
                        long oldZero = _values[Key(ValueCatalog.SensorZero[i])];
                        long kMicro = _values[Key(ValueCatalog.SensorK[i])];
                        // math_zero_cal_new_zero(): new zero in mm/m, back to ppm.
                        double newZeroMmPerM = (double)oldZero / kMicro + (_zAvg1[i] + avg2) / 2;
                        long ppm = (long)Math.Round(newZeroMmPerM * kMicro);
                        ppm = Math.Clamp(ppm, ValueCatalog.SensorZero[i].Min, ValueCatalog.SensorZero[i].Max);
                        _values[Key(ValueCatalog.SensorZero[i])] = ppm;
                    }
                    _rotated = false;
                    _zPhase = ZeroCalPhase.ResultReady;
                    PushZero();
                    _zPhase = ZeroCalPhase.Idle;
                    _zProgress = 0;
                    PushZero();
                }
            }
        }, null, period, period);
    }

    private byte[] ZeroBytes()
    {
        var b = new byte[6];
        b[0] = (byte)_zPhase;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), (ushort)_zProgress);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(3), ZTarget);
        b[5] = (byte)_zMask;
        return b;
    }

    private void PushZero() =>
        Push(ApiV3Codec.Subscribe(ApiCategory.Procedures, LeveltronicApi.Proc.ZeroCal), ZeroBytes());

    // ── Precision ────────────────────────────────────────────────────────────

    private (ApiStatus, byte[]?) PrecisionCommand(byte[] p)
    {
        if (p.Length != 1) return (ApiStatus.BadLength, null);
        if (p[0] == 1)
        {
            _pTimer?.Dispose();
            _pPhase = PrecisionPhase.Idle;
            PushPrecision();
            return (ApiStatus.Ok, null);
        }
        if (p[0] != 0) return (ApiStatus.InvalidParameter, null);
        if (!_demodRunning || _zPhase != ZeroCalPhase.Idle) return (ApiStatus.BusyResource, null);

        PrecisionRuns++;
        _pPhase = PrecisionPhase.Running;
        _pCount = 0;
        _pElapsed = 0;
        _pFailed = false;
        _pTimer?.Dispose();
        var period = TimeSpan.FromMilliseconds(100);
        _pTimer = _time.CreateTimer(_ =>
        {
            lock (_gate)
            {
                _pElapsed += 100;
                if (!PrecisionNeverClean) _pCount = Math.Min(_pCount + 4, PTarget);
                if (_pCount >= PTarget)
                {
                    _pPhase = PrecisionPhase.Done;
                    double s1 = Reading(0, EffectiveTilt(0)) + NoiseValue();
                    double s2 = Reading(1, EffectiveTilt(1)) + NoiseValue();
                    _pResult = [s1, s2, s1 - s2];
                    _pTimer?.Dispose();
                }
                else if (_pElapsed >= 5000)
                {
                    _pPhase = PrecisionPhase.Done;
                    _pFailed = true;
                    _pTimer?.Dispose();
                }
                PushPrecision();
            }
        }, null, period, period);
        PushPrecision();
        return (ApiStatus.Ok, null);
    }

    private byte[] PrecisionBytes()
    {
        var b = new byte[23];
        b[0] = (byte)_pPhase;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), PTarget);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(3), (ushort)_pCount);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5), (uint)_pElapsed);
        b[9] = (byte)(_pFailed ? 1 : 0);
        bool done = _pPhase == PrecisionPhase.Done && !_pFailed;
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(10), done ? (float)_pResult[0] : 0);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(14), done ? (float)_pResult[1] : 0);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(18), done ? (float)_pResult[2] : 0);
        b[22] = (byte)(_pPhase == PrecisionPhase.Running && PrecisionNeverClean ? 1 : 0);
        return b;
    }

    private void PushPrecision() =>
        Push(ApiV3Codec.Subscribe(ApiCategory.Procedures, LeveltronicApi.Proc.Precision), PrecisionBytes());

    // ── Topics / procedures GET ──────────────────────────────────────────────

    private (ApiStatus, byte[]?) ProcedureGet(byte res) => res switch
    {
        LeveltronicApi.Proc.ZeroCal => (ApiStatus.Ok, ZeroBytes()),
        LeveltronicApi.Proc.Precision => (ApiStatus.Ok, PrecisionBytes()),
        _ => (ApiStatus.UnknownResource, null),
    };

    private (ApiStatus, byte[]?) TopicGet(byte res)
    {
        var data = TopicData(res);
        return data is null ? (ApiStatus.UnknownResource, null) : (ApiStatus.Ok, data);
    }

    private byte[]? TopicData(byte res)
    {
        switch (res)
        {
            case LeveltronicApi.Topic.Env:
            {
                var b = new byte[14];
                BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(0), 2380);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(2), 96400);
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), 4100);
                b[8] = 1;
                BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(9), 2450);
                BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(11), 2200);
                b[13] = 0;
                return b;
            }
            case LeveltronicApi.Topic.Status: return StateBytes();
            case LeveltronicApi.Topic.Live: return LiveBytes();
            case LeveltronicApi.Topic.SignalDiag: return DiagBytes();
            default: return null;
        }
    }

    private byte[] LiveBytes()
    {
        var b = new byte[15];
        double s1 = Reading(0, EffectiveTilt(0)) + NoiseValue();
        double s2 = Reading(1, EffectiveTilt(1)) + NoiseValue();
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0), (ushort)(_liveSeq & 0xFFFF));
        b[2] = (byte)((_demodRunning ? 1 : 0) | (Disturbed ? 6 : 0));
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(3), (float)s1);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(7), (float)s2);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(11), (float)(s1 - s2));
        return b;
    }

    private byte[] DiagBytes()
    {
        var b = new byte[56];
        // ADC channel order S2, B, A, S1; A − B at 90°.
        double[] rms = [4.1, 310.0, 309.5, 3.8];
        double[] p2p = [11.6, 877.0, 875.4, 10.8];
        // A sensor reads its own phase against the drive, which sits at 90°.
        double[] ph = [TruePhaseDeg[1] + 90, 270.0, 90.0, TruePhaseDeg[0] + 90];
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4 * i), (float)rms[i]);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(16 + 4 * i), (float)p2p[i]);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(32 + 4 * i), (float)ph[i]);
        }
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(48), (float)PhysicalTilt[0]);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(52), (float)PhysicalTilt[1]);
        return b;
    }

    private byte[] PhasorBytes(int seq)
    {
        // A = 1000 e^{j th}, B = -A, S = PGA * (A - B) * u
        const double th = 0.3;
        double ar = 1000 * Math.Cos(th), ai = 1000 * Math.Sin(th);
        double dr = 2 * ar, di = 2 * ai;
        double N() => Noise == 0 ? 0 : (_rng.NextDouble() * 2 - 1) * Noise * 20;
        (double, double) S(int i)
        {
            var (ur, ui) = U(i, EffectiveTilt(i));
            return (16 * (dr * ur - di * ui) + N(), 16 * (dr * ui + di * ur) + N());
        }
        var (s1r, s1i) = S(0);
        var (s2r, s2i) = S(1);
        return new PhasorEntry(-ar, -ai, ar, ai, s1r, s1i, s2r, s2i, seq & 0xFFFF).Encode();
    }

    // ── Subscriptions ────────────────────────────────────────────────────────

    private void Subscribe(ushort op, ApiCategory cat, byte res, byte[] p)
    {
        var status = ValidateSubscribe(cat, res);
        if (status != ApiStatus.Ok) { Reply(op, status); return; }
        if (p.Length < 4 && !(cat == ApiCategory.Topics && res == LeveltronicApi.Topic.Live)
            && !(cat == ApiCategory.Procedures) && !(cat == ApiCategory.Topics && res == LeveltronicApi.Topic.PhasorStream))
        {
            Reply(op, ApiStatus.BadLength);
            return;
        }

        StopSub(op);
        var sub = new Sub();
        _subs[op] = sub;
        Reply(op, ApiStatus.Ok); // the ack comes first

        int period = p.Length >= 4 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(p) : 250;

        if (cat == ApiCategory.Topics && res == LeveltronicApi.Topic.PhasorStream)
        {
            _phasorActive = true;
            int seq = 0;
            var tick = TimeSpan.FromMilliseconds(25);
            sub.Timer = _time.CreateTimer(_ =>
            {
                lock (_gate)
                {
                    if (PhasorSilent) return;
                    seq += 64;
                    Push(op, PhasorBytes(seq));
                }
            }, null, tick, tick);
        }
        else if (cat == ApiCategory.Topics && res == LeveltronicApi.Topic.Live)
        {
            EnsureLiveTimer();
        }
        else if (cat == ApiCategory.Procedures)
        {
            // Pushed on change; the current state once, like the device.
            Push(op, res == LeveltronicApi.Proc.ZeroCal ? ZeroBytes() : PrecisionBytes());
        }
        else
        {
            var every = TimeSpan.FromMilliseconds(Math.Max(period, 50));
            Push(op, TopicData(res)!);
            sub.Timer = _time.CreateTimer(_ =>
            {
                lock (_gate)
                {
                    if (TopicData(res) is { } d) Push(op, d);
                }
            }, null, every, every);
        }
    }

    private static ApiStatus ValidateSubscribe(ApiCategory cat, byte res)
    {
        if (cat == ApiCategory.Topics)
            return res is LeveltronicApi.Topic.Env or LeveltronicApi.Topic.Status or LeveltronicApi.Topic.Live
                or LeveltronicApi.Topic.SignalDiag or LeveltronicApi.Topic.PhasorStream
                ? ApiStatus.Ok : ApiStatus.UnknownResource;
        if (cat == ApiCategory.Procedures && res is LeveltronicApi.Proc.ZeroCal or LeveltronicApi.Proc.Precision)
            return ApiStatus.Ok;
        return ApiStatus.UnknownResource;
    }

    private void Unsubscribe(ushort subscribeLessOp)
    {
        ushort sop = ApiV3Codec.WithVerb(subscribeLessOp, ApiVerb.Subscribe);
        bool had = _subs.ContainsKey(sop);
        StopSub(sop);
        Reply(subscribeLessOp, had ? ApiStatus.Ok : ApiStatus.NotSubscribed);
    }

    private void StopSub(ushort op)
    {
        if (!_subs.Remove(op, out var sub)) return;
        sub.Timer?.Dispose();
        if (ApiV3Codec.CategoryOf(op) == ApiCategory.Topics)
        {
            byte res = ApiV3Codec.ResourceOf(op);
            if (res == LeveltronicApi.Topic.PhasorStream) _phasorActive = false;
            if (res == LeveltronicApi.Topic.Live)
            {
                _liveTimer?.Dispose();
                _liveTimer = null;
            }
        }
    }

    private void EnsureLiveTimer()
    {
        var period = TimeSpan.FromMilliseconds(246);
        _liveTimer ??= _time.CreateTimer(_ =>
        {
            lock (_gate)
            {
                _liveSeq++;
                Push(ApiV3Codec.Subscribe(ApiCategory.Topics, LeveltronicApi.Topic.Live), LiveBytes());
            }
        }, null, period, period);
    }

    private void StopAll()
    {
        lock (_gate)
        {
            foreach (var s in _subs.Values) s.Timer?.Dispose();
            _subs.Clear();
            _phasorActive = false;
            _liveTimer?.Dispose(); _liveTimer = null;
            _zTimer?.Dispose();
            _pTimer?.Dispose();
            _serviceTimer?.Dispose();
        }
    }

    // ── Service mode idle timeout (10 minutes without a request) ─────────────

    private void TouchServiceTimer()
    {
        _serviceTimer?.Dispose();
        if (_serviceMode)
            _serviceTimer = _time.CreateTimer(_ => _serviceMode = false, null,
                TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
    }
}
