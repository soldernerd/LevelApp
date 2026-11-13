using LevelApp.Core.Instruments;
using Xunit;

namespace LevelApp.Tests;

public class KnownDeviceTests
{
    [Fact]
    public void BluetoothDevice_TechnicalId_IsFoundUnderNameAndFormattedAddress()
    {
        var d = new KnownDevice("id", "leveltronic", "ble", "Prototyp Gr\u00fcn", "8C7112934651", "Leveltronic_4651");

        Assert.Equal("Leveltronic_4651 \u00b7 8C:71:12:93:46:51", d.GetTechnicalId());
    }

    [Fact]
    public void BluetoothDevice_WithoutRecordedName_ShowsTheAddressOnly()
    {
        var d = new KnownDevice("id", "leveltronic", "ble", "Prototyp Gr\u00fcn", "8C7112934651");

        Assert.Equal("8C:71:12:93:46:51", d.GetTechnicalId());
    }

    [Fact]
    public void UnrenamedDevice_DoesNotRepeatItsName()
    {
        var d = new KnownDevice("id", "leveltronic", "usb-hid", "InclinationMeter \u00b7 205C30574D4D", "usb-interface-path",
                                "InclinationMeter \u00b7 205C30574D4D");

        Assert.Equal("", d.GetTechnicalId());
    }
}
