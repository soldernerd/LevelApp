namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>
/// Well-known Device API v2 opcodes, resource indices and transport identifiers
/// for the Leveltronic firmware build documented in
/// <c>InclinationMeterFirmware/docs/api-reference.md</c>.
/// </summary>
public static class LeveltronicApi
{
    // ── Transport identity ───────────────────────────────────────────────────

    /// <summary>USB Custom-HID vendor id.</summary>
    public const ushort UsbVendorId = 0x04D8;

    /// <summary>USB Custom-HID product id (normal / application mode).</summary>
    public const ushort UsbProductId = 0xF08F;

    /// <summary>USB vendor id while in the STM32 ROM bootloader (DFU).</summary>
    public const ushort DfuVendorId = 0x0483;

    /// <summary>USB product id while in the STM32 ROM bootloader (DFU).</summary>
    public const ushort DfuProductId = 0xDF11;

    /// <summary>
    /// RN4871 Transparent-UART GATT service. Requests are written to
    /// <see cref="BleRxCharacteristicUuid"/>; responses arrive as notifications
    /// on <see cref="BleTxCharacteristicUuid"/>.
    /// </summary>
    public static readonly Guid BleServiceUuid =
        new("49535343-fe7d-4ae5-8fa9-9fafd205e455");

    /// <summary>Host → device write characteristic (Transparent UART RX).</summary>
    public static readonly Guid BleRxCharacteristicUuid =
        new("49535343-8841-43f4-a8d4-ecbe34729bb3");

    /// <summary>Device → host notify characteristic (Transparent UART TX).</summary>
    public static readonly Guid BleTxCharacteristicUuid =
        new("49535343-1e4d-4bd9-ba61-23c647249616");

    /// <summary>Advertised local-name prefix (<c>Leveltronic_&lt;MAC&gt;</c>).</summary>
    public const string BleNamePrefix = "Leveltronic";

    // ── System status (0x0) ──────────────────────────────────────────────────

    public static readonly ushort GetIdentity =
        ApiV2Codec.Opcode(Api2Verb.Get, Api2Category.SystemStatus, 0x00);

    public static readonly ushort GetDeviceState =
        ApiV2Codec.Opcode(Api2Verb.Get, Api2Category.SystemStatus, 0x01);

    public static readonly ushort GetRtc =
        ApiV2Codec.Opcode(Api2Verb.Get, Api2Category.SystemStatus, 0x02);

    public static readonly ushort SetRtc =
        ApiV2Codec.Opcode(Api2Verb.Set, Api2Category.SystemStatus, 0x02);

    // ── Commands (0x1, EXECUTE only) ─────────────────────────────────────────

    public static readonly ushort CmdTestBeep =
        ApiV2Codec.Opcode(Api2Verb.Execute, Api2Category.Commands, 0x00);

    public static readonly ushort CmdSignalAnalysis =
        ApiV2Codec.Opcode(Api2Verb.Execute, Api2Category.Commands, 0x01);

    public static readonly ushort CmdForceCharge =
        ApiV2Codec.Opcode(Api2Verb.Execute, Api2Category.Commands, 0x02);

    public static readonly ushort CmdRebootToDfu =
        ApiV2Codec.Opcode(Api2Verb.Execute, Api2Category.Commands, 0x05);

    // ── Measurements (0x4) ──────────────────────────────────────────────────

    /// <summary>
    /// PROVISIONAL. This firmware build exposes no inclination/tilt Measurements
    /// resource (0x00–0x08 are temperature / battery / BME280 / external-temp).
    /// The angle read is expected to land as a Measurements resource; this
    /// plugin assumes index <c>0x10</c> returning <c>int32 LE</c> in µm/m.
    /// Change this constant and <see cref="LeveltronicDeviceClient"/>'s
    /// <c>ParseInclinationUmPerM</c> together when the firmware resource is
    /// finalised. Until then a real device answers <see cref="Api2Status.UnknownResource"/>.
    /// </summary>
    public const byte MeasInclinationResource = 0x10;

    public static readonly ushort GetInclination =
        ApiV2Codec.Opcode(Api2Verb.Get, Api2Category.Measurements, MeasInclinationResource);

    // ── Debug messages (0x6) ────────────────────────────────────────────────

    public static readonly ushort SubscribeDebugLog =
        ApiV2Codec.Opcode(Api2Verb.Subscribe, Api2Category.DebugMessages, 0x00);

    public static readonly ushort UnsubscribeDebugLog =
        ApiV2Codec.Opcode(Api2Verb.Unsubscribe, Api2Category.DebugMessages, 0x00);

    // ── Settings (0x3, GET / SET) ───────────────────────────────────────────

    public static ushort GetSetting(byte resource) =>
        ApiV2Codec.Opcode(Api2Verb.Get, Api2Category.Settings, resource);

    public static ushort SetSetting(byte resource) =>
        ApiV2Codec.Opcode(Api2Verb.Set, Api2Category.Settings, resource);

    /// <summary>Wire width / signedness of a Settings field.</summary>
    public enum SettingType
    {
        UInt16,
        Int32,
        UInt32,
    }

    /// <summary>
    /// One Settings resource, per <c>docs/api-reference.md</c> §Settings. The
    /// index equals the <c>DeviceSettings</c> field order (0x1B is appended out
    /// of struct order — see the reference doc).
    /// </summary>
    public readonly record struct SettingDescriptor(
        byte Resource,
        string Field,
        SettingType Type,
        long Min,
        long Max,
        string Group);

    /// <summary>All Settings resources exposed by this firmware build.</summary>
    public static readonly IReadOnlyList<SettingDescriptor> Settings =
    [
        new(0x00, "task_sensors_ms",        SettingType.UInt16, 1,     60000,  "Scheduler"),
        new(0x01, "task_processing_ms",     SettingType.UInt16, 1,     60000,  "Scheduler"),
        new(0x02, "task_display_ms",        SettingType.UInt16, 1,     60000,  "Scheduler"),
        new(0x03, "task_ble_ms",            SettingType.UInt16, 1,     60000,  "Scheduler"),
        new(0x04, "task_usb_ms",            SettingType.UInt16, 1,     60000,  "Scheduler"),
        new(0x05, "task_battery_ms",        SettingType.UInt16, 1,     60000,  "Scheduler"),
        new(0x06, "task_temperature_ms",    SettingType.UInt16, 1,     60000,  "Scheduler"),
        new(0x07, "stream_interval_ms",     SettingType.UInt16, 1,     60000,  "Scheduler"),
        new(0x08, "settling_threshold_umpm", SettingType.Int32, 1,     100000, "Settling"),
        new(0x09, "settling_timeout_ms",    SettingType.UInt32, 1,     60000,  "Settling"),
        new(0x0A, "filter_cutoff_hz_num",   SettingType.UInt16, 1,     10000,  "Filter"),
        new(0x0B, "filter_cutoff_hz_den",   SettingType.UInt16, 1,     10000,  "Filter"),
        new(0x0C, "battery_critical_mv",    SettingType.UInt16, 2500,  4200,   "Battery"),
        new(0x0D, "battery_low_mv",         SettingType.UInt16, 2500,  4200,   "Battery"),
        new(0x0E, "battery_charge_start_mv", SettingType.UInt16, 2500, 4200,   "Battery"),
        new(0x0F, "vbat_scale_num",         SettingType.UInt16, 1,     10000,  "Battery calibration"),
        new(0x10, "vbat_scale_den",         SettingType.UInt16, 1,     10000,  "Battery calibration"),
        new(0x11, "tmp236_seg1_voffs_mv",   SettingType.UInt16, 0,     3300,   "Temp calibration"),
        new(0x12, "tmp236_seg1_num",        SettingType.UInt16, 1,     10000,  "Temp calibration"),
        new(0x13, "tmp236_seg1_den",        SettingType.UInt16, 1,     10000,  "Temp calibration"),
        new(0x14, "tmp236_seg_boundary_mv", SettingType.UInt16, 0,     3300,   "Temp calibration"),
        new(0x15, "tmp236_seg2_voffs_mv",   SettingType.UInt16, 0,     3300,   "Temp calibration"),
        new(0x16, "tmp236_seg2_num",        SettingType.UInt16, 1,     10000,  "Temp calibration"),
        new(0x17, "tmp236_seg2_den",        SettingType.UInt16, 1,     10000,  "Temp calibration"),
        new(0x18, "tmp236_seg2_tinfl_cdeg", SettingType.UInt16, 0,     20000,  "Temp calibration"),
        new(0x19, "lm35_scale_mv_per_c",    SettingType.UInt16, 1,     1000,   "Temp calibration"),
        new(0x1A, "encoder_counts_per_detent", SettingType.UInt16, 1,  100,    "Input"),
        new(0x1B, "auto_poweroff_s",        SettingType.UInt16, 0,     65535,  "Power"),
        new(0x1C, "vbat_offset_mv",         SettingType.Int32,  -500,  500,    "Battery calibration"),
    ];

    /// <summary>Wire width in bytes for a <see cref="SettingType"/>.</summary>
    public static int WidthOf(SettingType type) => type == SettingType.UInt16 ? 2 : 4;
}
