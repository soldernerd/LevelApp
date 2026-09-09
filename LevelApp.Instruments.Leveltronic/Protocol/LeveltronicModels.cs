namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>Firmware identity (<c>GET 0x0/0x00</c>, 27-byte response).</summary>
public sealed record DeviceIdentity(
    int FirmwareMajor,
    int FirmwareMinor,
    int FirmwarePatch,
    string Product,
    string Serial)
{
    public string FirmwareVersion => $"{FirmwareMajor}.{FirmwareMinor}.{FirmwarePatch}";
}

/// <summary>Battery / connection state (<c>GET 0x0/0x01</c>, 7-byte response).</summary>
public sealed record DeviceState(
    BatteryState Battery,
    int BatterySocPercent,
    int BatteryMilliVolts,
    bool UsbConnected,
    bool BleConnected,
    bool CalibrationValid);

/// <summary>Battery state code from <see cref="DeviceState"/>.</summary>
public enum BatteryState
{
    Normal   = 0,
    Low      = 1,
    Critical = 2,
    Charging = 3,
    Full     = 4,
}

/// <summary>
/// RTC calendar value (<c>GET 0x0/0x02</c>, 9-byte response). <see cref="IsSet"/>
/// is false until the clock has been set since power-up.
/// </summary>
public sealed record RtcDateTime(
    int Year,
    int Month,
    int Day,
    int Weekday,
    int Hour,
    int Minute,
    int Second,
    bool IsSet)
{
    public DateTime ToDateTime() => new(Year, Month, Day, Hour, Minute, Second, DateTimeKind.Unspecified);
}
