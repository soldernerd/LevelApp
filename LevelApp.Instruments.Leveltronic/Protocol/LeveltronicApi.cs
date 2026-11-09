namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>
/// Device API v3 resource ids and transport identity of the Leveltronic
/// ("InclinationMeter") firmware, 0.11 and later. The authoritative list is
/// <c>InclinationMeterFirmware/docs/api-reference.md</c> (generated from
/// <c>tools/api_spec.py</c>); the phone app's <c>protocol.dart</c> is the
/// reference client.
/// </summary>
public static class LeveltronicApi
{
    // ── Transport identity ───────────────────────────────────────────────────

    /// <summary>System IDENTITY <c>product</c> of this device family.</summary>
    public const string Product = "InclinationMeter";

    public const ushort UsbVendorId = 0x04D8;
    public const ushort UsbProductId = 0xF08F;

    /// <summary>USB ids while in the STM32 ROM bootloader (DFU).</summary>
    public const ushort DfuVendorId = 0x0483;
    public const ushort DfuProductId = 0xDF11;

    /// <summary>
    /// RN4871 Transparent-UART GATT service. The module does not advertise it,
    /// so scans filter on <see cref="BleNamePrefix"/> instead.
    /// </summary>
    public static readonly Guid BleServiceUuid = new("49535343-fe7d-4ae5-8fa9-9fafd205e455");
    public static readonly Guid BleRxCharacteristicUuid = new("49535343-8841-43f4-a8d4-ecbe34729bb3");
    public static readonly Guid BleTxCharacteristicUuid = new("49535343-1e4d-4bd9-ba61-23c647249616");

    /// <summary>Advertised name prefix (<c>Leveltronic-xxxx</c> / <c>Leveltronic_&lt;MAC&gt;</c>).</summary>
    public const string BleNamePrefix = "Leveltronic";

    // ── System (0x0) ─────────────────────────────────────────────────────────

    public static class Sys
    {
        public const byte Identity = 0x00;
        public const byte State = 0x01;
        public const byte Health = 0x02;
        public const byte Rtc = 0x03;
    }

    // ── Commands (0x1, EXECUTE) ──────────────────────────────────────────────

    public static class Cmd
    {
        public const byte PowerOff = 0x40;
        public const byte Reboot = 0x41;
        public const byte RebootDfu = 0x42;       // service mode
        public const byte ForceCharge = 0x43;
        public const byte EndCharging = 0x44;
        public const byte ChargeInhibit = 0x45;
        public const byte TiltDemod = 0x50;
        public const byte ZeroCal = 0x51;         // service mode
        public const byte Precision = 0x52;
        public const byte FactoryDefaults = 0x60; // service mode
        public const byte ClearCounters = 0x61;
        public const byte ServiceEnd = 0x62;
        public const byte TestBeep = 0x70;
    }

    // ── Topics (0x5) ─────────────────────────────────────────────────────────

    public static class Topic
    {
        public const byte Env = 0x00;
        public const byte Status = 0x01;
        public const byte Live = 0x02;
        public const byte Raw = 0x03;
        public const byte PhasorStream = 0x04;
        public const byte SignalDiag = 0x05;
    }

    // ── Procedures (0x9) ─────────────────────────────────────────────────────

    public static class Proc
    {
        public const byte ZeroCal = 0x00;
        public const byte Precision = 0x01;
    }

    /// <summary>Confirmation byte of Commands FACTORY_DEFAULTS.</summary>
    public const byte FactoryDefaultsKey = 0xA5;
}
