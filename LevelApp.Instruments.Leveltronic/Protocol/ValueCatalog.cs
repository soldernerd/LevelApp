using System.Buffers.Binary;

namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>Wire type of a Settings / Calibrations value.</summary>
public enum WireType { U8, U16, I16, I32 }

public static class WireTypeExtensions
{
    public static int Bytes(this WireType t) => t switch
    {
        WireType.U8 => 1, WireType.U16 or WireType.I16 => 2, _ => 4,
    };

    public static bool Covers(this WireType t, ReadOnlySpan<byte> d) => d.Length >= t.Bytes();

    public static long Decode(this WireType t, ReadOnlySpan<byte> d) => t switch
    {
        WireType.U8 => d[0],
        WireType.U16 => BinaryPrimitives.ReadUInt16LittleEndian(d),
        WireType.I16 => BinaryPrimitives.ReadInt16LittleEndian(d),
        _ => BinaryPrimitives.ReadInt32LittleEndian(d),
    };

    public static byte[] Encode(this WireType t, long v)
    {
        var b = new byte[t.Bytes()];
        switch (t)
        {
            case WireType.U8: b[0] = (byte)v; break;
            case WireType.U16: BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); break;
            case WireType.I16: BinaryPrimitives.WriteInt16LittleEndian(b, (short)v); break;
            default: BinaryPrimitives.WriteInt32LittleEndian(b, (int)v); break;
        }
        return b;
    }
}

/// <summary>One Settings or Calibrations resource: id, wire type, range and meaning.</summary>
public sealed record ValueDef(
    ApiCategory Category, byte Id, string Label, string Description,
    WireType Type, long Min, long Max,
    string Unit = "", bool Service = false, bool Optional = false, string Group = "")
{
    public ushort GetOpcode => ApiV3Codec.Get(Category, Id);
    public ushort SetOpcode => ApiV3Codec.Set(Category, Id);
}

/// <summary>
/// The Settings and Calibrations resources as data — mirrors the firmware's
/// <c>tools/api_spec.py</c> / <c>docs/api-reference.md</c> and the phone app's
/// <c>api_catalog.dart</c>. Drives the generic value editor and the wizards.
/// </summary>
public static class ValueCatalog
{
    private const ApiCategory S = ApiCategory.Settings;
    private const ApiCategory C = ApiCategory.Calibrations;

    /// <summary>Settings (category 0x3). Ranges are the firmware's.</summary>
    public static readonly IReadOnlyList<ValueDef> Settings =
    [
        new(S, 0x60, "Auto power-off",
            "Idle time with no encoder input before the instrument powers off (0 = never).",
            WireType.U16, 0, 65535, "s", Group: "Power"),
        new(S, 0x52, "Charge start voltage",
            "Below this battery voltage, with USB power present, charging starts.",
            WireType.U16, 3000, 4200, "mV", Group: "Power"),
        new(S, 0x53, "Max time at 100 %",
            "Charging stops after this many minutes at a full battery (0 = never). Only present in firmware builds that support it.",
            WireType.U16, 0, 1440, "min", Optional: true, Group: "Power"),
        new(S, 0x51, "Battery low warning",
            "Below this voltage the low-battery warning shows (must stay above \"Battery critical\").",
            WireType.U16, 3000, 4200, "mV", Group: "Power"),
        new(S, 0x50, "Battery critical",
            "Below this voltage the instrument powers off (must stay below \"Battery low warning\").",
            WireType.U16, 2500, 3600, "mV", Group: "Power"),
        new(S, 0x61, "Encoder counts per detent",
            "Raw quadrature transitions per mechanical detent of the rotary encoders.",
            WireType.U16, 1, 100, Group: "Instrument"),
        new(S, 0x40, "Sensor task period", "How often the sensor task runs.",
            WireType.U16, 1, 1000, "ms", Group: "Task periods"),
        new(S, 0x41, "BLE task period", "How often the BLE task runs.",
            WireType.U16, 1, 250, "ms", Group: "Task periods"),
        new(S, 0x42, "USB task period", "How often the USB task runs.",
            WireType.U16, 1, 250, "ms", Group: "Task periods"),
        new(S, 0x43, "Battery task period", "How often the battery task runs.",
            WireType.U16, 100, 10000, "ms", Group: "Task periods"),
        new(S, 0x44, "Temperature task period", "How often the temperature task runs.",
            WireType.U16, 100, 60000, "ms", Group: "Task periods"),
    ];

    // ── Sensor calibrations — referenced by the wizards ──────────────────────

    private const string KText = "tilt [mm/m] = (ratio − zero) / k, at PGA 1. Unit: 1e-6 per mm/m.";

    public static readonly ValueDef S1K = new(C, 0x40, "S1 sensitivity k", KText,
        WireType.I32, 100, 1_000_000, Service: true, Group: "Sensor 1");
    public static readonly ValueDef S1Zero = new(C, 0x41, "S1 zero",
        "Level point of sensor 1 as a ratio, independent of k.",
        WireType.I32, -500_000, 500_000, "ppm", Service: true, Group: "Sensor 1");
    public static readonly ValueDef S1Phase = new(C, 0x42, "S1 phase",
        "Phase of the tilt signal relative to the excitation reference.",
        WireType.I16, -18000, 18000, "0.01°", Service: true, Group: "Sensor 1");
    public static readonly ValueDef S1Invert = new(C, 0x43, "S1 invert",
        "Sign of the reported reading (1 = inverted).",
        WireType.U8, 0, 1, Service: true, Group: "Sensor 1");
    public static readonly ValueDef S2K = new(C, 0x44, "S2 sensitivity k", KText,
        WireType.I32, 100, 1_000_000, Service: true, Group: "Sensor 2");
    public static readonly ValueDef S2Zero = new(C, 0x45, "S2 zero",
        "Level point of sensor 2 as a ratio, independent of k.",
        WireType.I32, -500_000, 500_000, "ppm", Service: true, Group: "Sensor 2");
    public static readonly ValueDef S2Phase = new(C, 0x46, "S2 phase",
        "Phase of the tilt signal relative to the excitation reference.",
        WireType.I16, -18000, 18000, "0.01°", Service: true, Group: "Sensor 2");
    public static readonly ValueDef S2Invert = new(C, 0x47, "S2 invert",
        "Sign of the reported reading (1 = inverted).",
        WireType.U8, 0, 1, Service: true, Group: "Sensor 2");

    /// <summary>Per-sensor calibration definitions, index 0 = S1, 1 = S2.</summary>
    public static readonly IReadOnlyList<ValueDef> SensorK = [S1K, S2K];
    public static readonly IReadOnlyList<ValueDef> SensorZero = [S1Zero, S2Zero];
    public static readonly IReadOnlyList<ValueDef> SensorPhase = [S1Phase, S2Phase];
    public static readonly IReadOnlyList<ValueDef> SensorInvert = [S1Invert, S2Invert];

    /// <summary>All calibrations, for the generic editor (service mode to write).</summary>
    public static readonly IReadOnlyList<ValueDef> Calibrations =
    [
        S1K, S1Zero, S1Phase, S1Invert, S2K, S2Zero, S2Phase, S2Invert,
        new(C, 0x50, "Battery scale numerator", "Vbat = Vadc × num / den + offset.",
            WireType.U16, 1, 10000, Service: true, Group: "Battery voltage"),
        new(C, 0x51, "Battery scale denominator", "Vbat = Vadc × num / den + offset.",
            WireType.U16, 1, 10000, Service: true, Group: "Battery voltage"),
        new(C, 0x52, "Battery voltage offset", "Additive correction of the battery voltage.",
            WireType.I32, -500, 500, "mV", Service: true, Group: "Battery voltage"),
        new(C, 0x70, "Clock trim", "Positive makes the clock run faster. About 0.95 ppm per step.",
            WireType.I16, -4800, 4800, "0.1 ppm", Service: true, Group: "Clock"),
        new(C, 0x68, "External LM35 scale", "Scale of the external temperature sensor.",
            WireType.U16, 1, 1000, "mV/°C", Service: true, Group: "Temperature sensors"),
        new(C, 0x60, "TMP236 seg. 1 offset", "TMP236 segment 1 voltage offset.",
            WireType.U16, 0, 3300, "mV", Service: true, Group: "Temperature sensors"),
        new(C, 0x61, "TMP236 seg. 1 slope num.", "TMP236 segment 1 slope numerator.",
            WireType.U16, 1, 10000, Service: true, Group: "Temperature sensors"),
        new(C, 0x62, "TMP236 seg. 1 slope den.", "TMP236 segment 1 slope denominator.",
            WireType.U16, 1, 10000, Service: true, Group: "Temperature sensors"),
        new(C, 0x63, "TMP236 segment boundary", "TMP236 segment boundary.",
            WireType.U16, 0, 3300, "mV", Service: true, Group: "Temperature sensors"),
        new(C, 0x64, "TMP236 seg. 2 offset", "TMP236 segment 2 voltage offset.",
            WireType.U16, 0, 3300, "mV", Service: true, Group: "Temperature sensors"),
        new(C, 0x65, "TMP236 seg. 2 slope num.", "TMP236 segment 2 slope numerator.",
            WireType.U16, 1, 10000, Service: true, Group: "Temperature sensors"),
        new(C, 0x66, "TMP236 seg. 2 slope den.", "TMP236 segment 2 slope denominator.",
            WireType.U16, 1, 10000, Service: true, Group: "Temperature sensors"),
        new(C, 0x67, "TMP236 seg. 2 inflection", "TMP236 segment 2 inflection temperature.",
            WireType.U16, 0, 20000, "0.01 °C", Service: true, Group: "Temperature sensors"),
    ];
}
