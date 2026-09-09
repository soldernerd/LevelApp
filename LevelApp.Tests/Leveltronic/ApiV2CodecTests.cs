using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>
/// Framing / CRC tests for <see cref="ApiV2Codec"/>. Vectors are cross-checked
/// against <c>InclinationMeterFirmware/PythonTestCode/apiv2.py</c>.
/// </summary>
public sealed class ApiV2CodecTests
{
    [Fact]
    public void Crc16Ccitt_StandardCheckValue()
    {
        // "123456789" -> 0x29B1 is the published CRC-16/CCITT-FALSE check value.
        ushort crc = ApiV2Codec.Crc16Ccitt("123456789"u8);
        Assert.Equal(0x29B1, crc);
    }

    [Fact]
    public void Crc16Ccitt_ZeroHeader()
    {
        ushort crc = ApiV2Codec.Crc16Ccitt([0x00, 0x00, 0x00, 0x00]);
        Assert.Equal(0x84C0, crc);
    }

    [Fact]
    public void Opcode_ComposeAndSplit()
    {
        ushort op = ApiV2Codec.Opcode(Api2Verb.Set, Api2Category.Settings, 0x1B);
        Assert.Equal(0x131B, op);
        Assert.Equal(Api2Verb.Set, ApiV2Codec.VerbOf(op));
        Assert.Equal(Api2Category.Settings, ApiV2Codec.CategoryOf(op));
        Assert.Equal(0x1B, ApiV2Codec.ResourceOf(op));
    }

    [Fact]
    public void BuildFrame_GetIdentity_MatchesReference()
    {
        byte[] frame = ApiV2Codec.BuildFrame(LeveltronicApi.GetIdentity);
        Assert.Equal(Convert.FromHexString("00000000C084"), frame);
    }

    [Fact]
    public void BuildFrame_SetAutoPowerOff_MatchesReference()
    {
        ushort op = LeveltronicApi.SetSetting(0x1B);
        byte[] frame = ApiV2Codec.BuildFrame(op, [0xFA, 0x00]); // 250 LE
        Assert.Equal(Convert.FromHexString("1B130200FA00DC2C"), frame);
    }

    [Fact]
    public void BuildThenParse_RoundTrips()
    {
        byte[] payload = [(byte)Api2Status.Ok, 0xAD, 0xBE, 0xEF];
        byte[] frame = ApiV2Codec.BuildFrame(0x0410, payload);

        Assert.True(ApiV2Codec.TryParseFrame(frame, out ushort op, out var status, out var data));
        Assert.Equal(0x0410, op);
        Assert.Equal(Api2Status.Ok, status);
        Assert.Equal(new byte[] { 0xAD, 0xBE, 0xEF }, data);
    }

    [Fact]
    public void TryParseFrame_IdentityOkResponse()
    {
        byte[] frame = Convert.FromHexString(
            "00001C0000010203496E636C696E6F4D6574657200000000534E303030303100668F");

        Assert.True(ApiV2Codec.TryParseFrame(frame, out ushort op, out var status, out var data));
        Assert.Equal(0x0000, op);
        Assert.Equal(Api2Status.Ok, status);
        Assert.Equal(27, data.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, data[..3]);
    }

    [Fact]
    public void TryParseFrame_ToleratesTrailingHidPadding()
    {
        byte[] frame = ApiV2Codec.BuildFrame(LeveltronicApi.GetDeviceState);
        byte[] padded = new byte[64];
        frame.CopyTo(padded, 0); // rest stays zero, as a HID IN report would arrive

        Assert.True(ApiV2Codec.TryParseFrame(padded, out ushort op, out _, out _));
        Assert.Equal(LeveltronicApi.GetDeviceState, op);
    }

    [Fact]
    public void TryParseFrame_RejectsBadCrc()
    {
        byte[] frame = ApiV2Codec.BuildFrame(LeveltronicApi.GetIdentity);
        frame[^1] ^= 0xFF;
        Assert.False(ApiV2Codec.TryParseFrame(frame, out _, out _, out _));
    }

    [Fact]
    public void TryParseFrame_RejectsTruncated()
    {
        Assert.False(ApiV2Codec.TryParseFrame([0x00, 0x00, 0x04, 0x00], out _, out _, out _));
    }

    [Fact]
    public void TryParseFrame_EmptyPayload_HasNullStatus()
    {
        byte[] frame = ApiV2Codec.BuildFrame(0x1234);
        Assert.True(ApiV2Codec.TryParseFrame(frame, out ushort op, out var status, out var data));
        Assert.Equal(0x1234, op);
        Assert.Null(status);
        Assert.Empty(data);
    }
}
