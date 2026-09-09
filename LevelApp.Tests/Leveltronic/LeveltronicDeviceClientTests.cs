using System.Buffers.Binary;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

public sealed class LeveltronicDeviceClientTests
{
    [Fact]
    public async Task GetIdentity_DecodesVersionProductSerial()
    {
        byte[] body =
        [
            1, 2, 3,
            .. Pad("InclinoMeter", 16),
            .. Pad("SN00001", 8),
        ];
        var link = new FakeLeveltronicLink().RespondOk(LeveltronicApi.GetIdentity, body);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        var id = await client.GetIdentityAsync();

        Assert.Equal("1.2.3", id.FirmwareVersion);
        Assert.Equal("InclinoMeter", id.Product);
        Assert.Equal("SN00001", id.Serial);
    }

    [Fact]
    public async Task GetDeviceState_DecodesFields()
    {
        byte[] body = new byte[7];
        body[0] = (byte)BatteryState.Charging;
        body[1] = 88;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), 4123);
        body[4] = 1; // usb
        body[5] = 0; // ble
        body[6] = 1; // calibration valid
        var link = new FakeLeveltronicLink().RespondOk(LeveltronicApi.GetDeviceState, body);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        var st = await client.GetDeviceStateAsync();

        Assert.Equal(BatteryState.Charging, st.Battery);
        Assert.Equal(88, st.BatterySocPercent);
        Assert.Equal(4123, st.BatteryMilliVolts);
        Assert.True(st.UsbConnected);
        Assert.False(st.BleConnected);
        Assert.True(st.CalibrationValid);
    }

    [Fact]
    public async Task SetRtc_PacksSevenBytesLittleEndianYear()
    {
        byte[]? sent = null;
        var link = new FakeLeveltronicLink().CaptureThenAck(LeveltronicApi.SetRtc, p => sent = p);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        await client.SetRtcAsync(new DateTime(2026, 9, 9, 14, 5, 30));

        Assert.NotNull(sent);
        Assert.Equal(7, sent!.Length);
        Assert.Equal(2026, BinaryPrimitives.ReadUInt16LittleEndian(sent));
        Assert.Equal(new byte[] { 9, 9, 14, 5, 30 }, sent[2..]);
    }

    [Fact]
    public async Task GetSettingValue_DecodesPerDescriptorWidth()
    {
        var i32 = LeveltronicApi.Settings.Single(s => s.Field == "vbat_offset_mv"); // Int32
        byte[] body = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(body, -321);
        var link = new FakeLeveltronicLink().RespondOk(LeveltronicApi.GetSetting(i32.Resource), body);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        long value = await client.GetSettingValueAsync(i32);

        Assert.Equal(-321, value);
    }

    [Fact]
    public async Task SetSettingValue_EncodesWidthAndRejectsOutOfRange()
    {
        var u16 = LeveltronicApi.Settings.Single(s => s.Field == "auto_poweroff_s");
        byte[]? sent = null;
        var link = new FakeLeveltronicLink().CaptureThenAck(LeveltronicApi.SetSetting(u16.Resource), p => sent = p);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        await client.SetSettingValueAsync(u16, 900);
        Assert.Equal(new byte[] { 0x84, 0x03 }, sent); // 900 LE

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.SetSettingValueAsync(u16, 100_000));
    }

    [Fact]
    public async Task GetInclination_UnknownResource_ThrowsNotSupported()
    {
        var link = new FakeLeveltronicLink()
            .RespondStatus(LeveltronicApi.GetInclination, Api2Status.UnknownResource);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        await Assert.ThrowsAsync<InstrumentResourceNotSupportedException>(
            () => client.GetInclinationMicronsPerMetreAsync());
    }

    [Fact]
    public async Task GetInclination_Ok_ReturnsInt32MicronsPerMetre()
    {
        byte[] body = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(body, -1750);
        var link = new FakeLeveltronicLink().RespondOk(LeveltronicApi.GetInclination, body);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        double v = await client.GetInclinationMicronsPerMetreAsync();

        Assert.Equal(-1750, v);
    }

    [Fact]
    public async Task NonOkStatus_ThrowsWithStatus()
    {
        var link = new FakeLeveltronicLink()
            .RespondStatus(LeveltronicApi.CmdTestBeep, Api2Status.BusyResource);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        var ex = await Assert.ThrowsAsync<LeveltronicProtocolException>(() => client.TestBeepAsync());
        Assert.Equal(Api2Status.BusyResource, ex.Status);
    }

    [Fact]
    public async Task NoResponse_TimesOut()
    {
        var link = new FakeLeveltronicLink().Silent(LeveltronicApi.GetIdentity);
        await link.ConnectAsync();
        await using var client = new LeveltronicDeviceClient(link);

        var ex = await Assert.ThrowsAsync<LeveltronicProtocolException>(() => client.GetIdentityAsync());
        Assert.Contains("No response", ex.Message);
    }

    private static byte[] Pad(string s, int len)
    {
        byte[] b = new byte[len];
        System.Text.Encoding.ASCII.GetBytes(s).CopyTo(b, 0);
        return b;
    }
}
