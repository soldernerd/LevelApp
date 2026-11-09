using System.Buffers.Binary;
using System.Text;

namespace LevelApp.Instruments.Leveltronic.Protocol;

// Payload decoders of the Leveltronic API v3 resources. Mirrors the phone app's
// lib/devices/leveltronic/protocol.dart; the layouts are in the firmware's
// docs/api-reference.md. Every Decode returns null for a payload that is too short.

internal static class Wire
{
    public static string CString(ReadOnlySpan<byte> d, int start, int len)
    {
        var s = d.Slice(start, len);
        int nul = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(nul >= 0 ? s[..nul] : s);
    }

    public static float F32(ReadOnlySpan<byte> d, int off) =>
        BinaryPrimitives.ReadSingleLittleEndian(d[off..]);
}

/// <summary>
/// System IDENTITY. Two layouts share the opcode: the 50-byte v3 one and the
/// 19-byte v2 one (<c>fw[3] product[16]</c>) of devices still on API v2, which
/// decode with <see cref="ApiVersion"/> 2 and an empty serial and build.
/// </summary>
public sealed record DeviceIdentity(
    int FwMajor, int FwMinor, int FwPatch,
    int ApiVersion, int MaxPayload,
    string Product, string Serial, string Build)
{
    public string Firmware => $"{FwMajor}.{FwMinor}.{FwPatch}";

    public static DeviceIdentity? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length == 19)
            return new DeviceIdentity(d[0], d[1], d[2], 2, ApiV3Codec.MaxPayload,
                Wire.CString(d, 3, 16), "", "");
        if (d.Length < 50) return null;
        return new DeviceIdentity(d[0], d[1], d[2], d[3],
            BinaryPrimitives.ReadUInt16LittleEndian(d[4..]),
            Wire.CString(d, 6, 16), Wire.CString(d, 22, 8), Wire.CString(d, 30, 20));
    }
}

public enum BatteryState : byte
{
    Normal = 0, Low = 1, Critical = 2, Charging = 3, Full = 4, Unknown = 0xFF,
}

/// <summary>System STATE / Topics STATUS (15 bytes).</summary>
public sealed record SystemState(
    BatteryState Battery, int BatterySocPct, int BatteryMv,
    bool UsbConnected, bool BleConnected, bool Charging, bool ForceCharging,
    bool ChargeInhibited, bool Rail3v3On, bool Rail5vOn, bool DemodRunning,
    bool PhasorStreamActive, bool BulkActive, bool ServiceMode)
{
    public static SystemState? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 15) return null;
        return new SystemState(
            Enum.IsDefined(typeof(BatteryState), d[0]) ? (BatteryState)d[0] : BatteryState.Unknown,
            d[1], BinaryPrimitives.ReadUInt16LittleEndian(d[2..]),
            d[4] != 0, d[5] != 0, d[6] != 0, d[7] != 0, d[8] != 0,
            d[9] != 0, d[10] != 0, d[11] != 0, d[12] != 0, d[13] != 0, d[14] != 0);
    }
}

/// <summary>System HEALTH (31 bytes).</summary>
public sealed record HealthInfo(
    bool AdcOk, bool DacOk, bool AdsOk, bool DisplayOk, bool BmeOk,
    int EepromSelfTest, bool SettingsSaveFailed, bool WokeFromStandby,
    int ResetCause, bool RtcSet, uint UptimeS,
    int UsbTxDropped, int BleTxDropped, int UartTxDropped, int RxMalformed,
    int LastFaultKind, uint LastFaultPc, uint LastFaultLr)
{
    private static readonly string[] ResetNames =
    [
        "pin", "power-on / brown-out", "software", "independent watchdog",
        "window watchdog", "low-power", "option-byte load",
    ];

    /// <summary>Human-readable reset causes, in bit order.</summary>
    public IReadOnlyList<string> ResetCauseNames =>
        [.. ResetNames.Where((_, i) => (ResetCause >> i & 1) == 1)];

    public static HealthInfo? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 31) return null;
        return new HealthInfo(
            d[0] != 0, d[1] != 0, d[2] != 0, d[3] != 0, d[4] != 0,
            d[5], d[6] != 0, d[7] != 0, d[8], d[9] != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(d[10..]),
            BinaryPrimitives.ReadUInt16LittleEndian(d[14..]),
            BinaryPrimitives.ReadUInt16LittleEndian(d[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(d[18..]),
            BinaryPrimitives.ReadUInt16LittleEndian(d[20..]),
            d[22],
            BinaryPrimitives.ReadUInt32LittleEndian(d[23..]),
            BinaryPrimitives.ReadUInt32LittleEndian(d[27..]));
    }
}

/// <summary>System RTC response (10 bytes).</summary>
public sealed record RtcInfo(
    int Year, int Month, int Day, int Weekday, int Hour, int Minute, int Second,
    bool IsSet, int Subsecond)
{
    public DateTime DateTime => new(Year, Month, Day, Hour, Minute, Second, DateTimeKind.Unspecified);

    public static RtcInfo? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 10) return null;
        return new RtcInfo(BinaryPrimitives.ReadUInt16LittleEndian(d), d[2], d[3], d[4],
            d[5], d[6], d[7], d[8] != 0, d[9]);
    }

    /// <summary>SET request payload (7 bytes).</summary>
    public static byte[] EncodeSet(DateTime t)
    {
        var b = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)t.Year);
        b[2] = (byte)t.Month; b[3] = (byte)t.Day;
        b[4] = (byte)t.Hour; b[5] = (byte)t.Minute; b[6] = (byte)t.Second;
        return b;
    }
}

/// <summary>Topics ENV (14 bytes).</summary>
public sealed record Environmental(
    double BmeTempC, long PressurePa, double HumidityPct, bool BmeOk,
    double OnboardTempC, double ExternalTempC, bool ExternalTempOk)
{
    public double PressureHpa => PressurePa / 100.0;

    public static Environmental? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 14) return null;
        return new Environmental(
            BinaryPrimitives.ReadInt16LittleEndian(d) / 100.0,
            BinaryPrimitives.ReadUInt32LittleEndian(d[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(d[6..]) / 100.0,
            d[8] != 0,
            BinaryPrimitives.ReadInt16LittleEndian(d[9..]) / 100.0,
            BinaryPrimitives.ReadInt16LittleEndian(d[11..]) / 100.0,
            d[13] != 0);
    }
}

/// <summary>Topics LIVE (15 bytes): the live tilt (mm/m) with its quality flags.</summary>
public sealed record LiveTilt(int Seq, int Flags, double TiltS1, double TiltS2, double TiltDiff)
{
    public bool Valid => (Flags & 1) != 0;
    public bool S1Doubtful => (Flags & 2) != 0;
    public bool S2Doubtful => (Flags & 4) != 0;
    public bool AnyDoubtful => S1Doubtful || S2Doubtful;

    public static LiveTilt? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 15) return null;
        return new LiveTilt(BinaryPrimitives.ReadUInt16LittleEndian(d), d[2],
            Wire.F32(d, 3), Wire.F32(d, 7), Wire.F32(d, 11));
    }
}

/// <summary>
/// Topics SIGNAL_DIAG (56 bytes). Channel order is that of the firmware's
/// reference: ch0 S2, ch1 B, ch2 A, ch3 S1.
/// </summary>
public sealed record SignalDiag(
    IReadOnlyList<double> RmsMv, IReadOnlyList<double> P2pMv, IReadOnlyList<double> PhaseDeg,
    double TheoreticalTiltS1, double TheoreticalTiltS2)
{
    public static readonly string[] ChannelNames = ["S2", "B", "A", "S1"];

    public static SignalDiag? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 56) return null;
        return new SignalDiag(Four(d, 0), Four(d, 16), Four(d, 32), Wire.F32(d, 48), Wire.F32(d, 52));
    }

    private static double[] Four(ReadOnlySpan<byte> d, int off)
    {
        var v = new double[4];
        for (int i = 0; i < 4; i++) v[i] = Wire.F32(d, off + 4 * i);
        return v;
    }
}

/// <summary>One Topics PHASOR_STREAM push: the raw phasors of a 64-cycle batch (34 bytes).</summary>
public sealed record PhasorEntry(
    double IB, double QB, double IA, double QA,
    double IS1, double QS1, double IS2, double QS2, int Seq)
{
    public static PhasorEntry? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 34) return null;
        return new PhasorEntry(Wire.F32(d, 0), Wire.F32(d, 4), Wire.F32(d, 8), Wire.F32(d, 12),
            Wire.F32(d, 16), Wire.F32(d, 20), Wire.F32(d, 24), Wire.F32(d, 28),
            BinaryPrimitives.ReadUInt16LittleEndian(d[32..]));
    }

    public byte[] Encode()
    {
        var b = new byte[34];
        double[] v = [IB, QB, IA, QA, IS1, QS1, IS2, QS2];
        for (int i = 0; i < 8; i++)
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4 * i), (float)v[i]);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(32), (ushort)Seq);
        return b;
    }
}

public enum ZeroCalPhase : byte
{
    Idle = 0, Step1Running = 1, Step1Done = 2, Step2Running = 3, ResultReady = 4, Unknown = 0xFF,
}

/// <summary>Procedures ZERO_CAL (6 bytes).</summary>
public sealed record ZeroCalStatus(ZeroCalPhase Phase, int Progress, int Target, int SensorMask)
{
    public static ZeroCalStatus? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 6) return null;
        return new ZeroCalStatus(
            Enum.IsDefined(typeof(ZeroCalPhase), d[0]) ? (ZeroCalPhase)d[0] : ZeroCalPhase.Unknown,
            BinaryPrimitives.ReadUInt16LittleEndian(d[1..]),
            BinaryPrimitives.ReadUInt16LittleEndian(d[3..]), d[5]);
    }
}

public enum PrecisionPhase : byte { Idle = 0, Running = 1, Done = 2, Unknown = 0xFF }

/// <summary>Procedures PRECISION (23 bytes).</summary>
public sealed record PrecisionStatus(
    PrecisionPhase Phase, int Target, int Count, long ElapsedMs, bool Failed,
    double TiltS1, double TiltS2, double TiltDiff, bool Disturbed)
{
    public static PrecisionStatus? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < 23) return null;
        return new PrecisionStatus(
            Enum.IsDefined(typeof(PrecisionPhase), d[0]) ? (PrecisionPhase)d[0] : PrecisionPhase.Unknown,
            BinaryPrimitives.ReadUInt16LittleEndian(d[1..]),
            BinaryPrimitives.ReadUInt16LittleEndian(d[3..]),
            BinaryPrimitives.ReadUInt32LittleEndian(d[5..]),
            d[9] != 0, Wire.F32(d, 10), Wire.F32(d, 14), Wire.F32(d, 18), d[22] != 0);
    }
}

/// <summary>
/// A live snapshot merged from the three topics kept subscribed while connected:
/// LIVE (tilt + quality), STATUS (battery, activities, service mode), ENV.
/// Any part is null until its first push.
/// </summary>
public sealed record InstrumentSnapshot(LiveTilt? Live = null, SystemState? Status = null, Environmental? Env = null)
{
    public bool HasReading => Live?.Valid ?? false;
    public bool ServiceMode => Status?.ServiceMode ?? false;
    public bool DemodRunning => Status?.DemodRunning ?? true;
}
