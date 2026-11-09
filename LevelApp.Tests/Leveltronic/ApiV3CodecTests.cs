using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>
/// Framing / CRC tests for <see cref="ApiV3Codec"/>. Vectors are cross-checked
/// against <c>InclinationMeterFirmware/PythonTestCode/apiv3.py</c>.
/// </summary>
public sealed class ApiV3CodecTests
{
    [Fact]
    public void Crc16Ccitt_StandardCheckValue() =>
        Assert.Equal(0x29B1, ApiV3Codec.Crc16Ccitt("123456789"u8));

    [Fact]
    public void Opcode_ComposeAndSplit()
    {
        ushort op = ApiV3Codec.Opcode(ApiVerb.Set, ApiCategory.Calibrations, 0x42);
        Assert.Equal(0x1242, op);
        Assert.Equal(ApiVerb.Set, ApiV3Codec.VerbOf(op));
        Assert.Equal(ApiCategory.Calibrations, ApiV3Codec.CategoryOf(op));
        Assert.Equal(0x42, ApiV3Codec.ResourceOf(op));
    }

    [Fact]
    public void WithVerb_TurnsSubscribeIntoUnsubscribe() =>
        Assert.Equal(0x4502, ApiV3Codec.WithVerb(ApiV3Codec.Subscribe(ApiCategory.Topics, 0x02), ApiVerb.Unsubscribe) | 0x0000);

    [Fact]
    public void Opcodes_MatchTheFirmwareReference()
    {
        Assert.Equal(0x0000, ApiV3Codec.Get(ApiCategory.System, LeveltronicApi.Sys.Identity));
        Assert.Equal(0x1003, ApiV3Codec.Set(ApiCategory.System, LeveltronicApi.Sys.Rtc));
        Assert.Equal(0x2152, ApiV3Codec.Execute(LeveltronicApi.Cmd.Precision));
        Assert.Equal(0x3502, ApiV3Codec.Subscribe(ApiCategory.Topics, LeveltronicApi.Topic.Live));
        Assert.Equal(0x3901, ApiV3Codec.Subscribe(ApiCategory.Procedures, LeveltronicApi.Proc.Precision));
        Assert.Equal(0x1240, ValueCatalog.S1K.SetOpcode);
    }

    [Fact]
    public void BuildFrame_GetIdentity_MatchesReference() =>
        Assert.Equal(Convert.FromHexString("00000000C084"), ApiV3Codec.BuildFrame(0x0000));

    [Fact]
    public void BuildFrame_SetRtc_MatchesReference() =>
        Assert.Equal(
            Convert.FromHexString("03100700EA070B090A141E254E"),
            ApiV3Codec.BuildFrame(0x1003, RtcInfo.EncodeSet(new DateTime(2026, 11, 9, 10, 20, 30))));

    [Fact]
    public void BuildFrame_SubscribeLive_MatchesReference() =>
        Assert.Equal(
            Convert.FromHexString("02350400FA000000F56F"),
            ApiV3Codec.BuildFrame(0x3502, BitConverter.GetBytes(250u)));

    [Fact]
    public void BuildFrame_PrecisionStart_MatchesReference() =>
        Assert.Equal(Convert.FromHexString("52210100007736"), ApiV3Codec.BuildFrame(0x2152, [0]));

    [Fact]
    public void BuildFrame_RejectsOversizedPayload() =>
        Assert.Throws<ArgumentException>(() => ApiV3Codec.BuildFrame(0x0000, new byte[ApiV3Codec.MaxPayload + 1]));

    [Fact]
    public void BuildThenParse_RoundTrips()
    {
        byte[] frame = ApiV3Codec.BuildFrame(0x0502, [(byte)ApiStatus.Ok, 0xAD, 0xBE, 0xEF]);
        var f = ApiV3Codec.Parse(frame)!;
        Assert.True(f.CrcOk);
        Assert.Equal(0x0502, f.Opcode);
        Assert.Equal(ApiStatus.Ok, f.Status);
        Assert.Equal(new byte[] { 0xAD, 0xBE, 0xEF }, f.Data);
    }

    [Fact]
    public void Parse_ErrorStatusCarriesNoData()
    {
        var f = ApiV3Codec.Parse(ApiV3Codec.BuildFrame(0x1240, [(byte)ApiStatus.ServiceModeRequired]))!;
        Assert.Equal(ApiStatus.ServiceModeRequired, f.Status);
        Assert.Empty(f.Data);
        Assert.False(f.LooksLikePush);
    }

    [Fact]
    public void Parse_PushHasSeqPageAndData()
    {
        var f = ApiV3Codec.Parse(ApiV3Codec.BuildFrame(0x3502, [0, 7, 0, 1, 2, 3]))!;
        Assert.True(f.LooksLikePush);
        Assert.Equal(new byte[] { 1, 2, 3 }, f.PushData);
    }

    [Fact]
    public void Parse_UndefinedStatusByteIsUnknown()
    {
        var f = ApiV3Codec.Parse(ApiV3Codec.BuildFrame(0x0000, [0x77]))!;
        Assert.Equal(ApiStatus.Unknown, f.Status);
    }

    [Fact]
    public void Parse_ToleratesTrailingHidPadding()
    {
        byte[] frame = ApiV3Codec.BuildFrame(0x0001);
        byte[] padded = new byte[64];
        frame.CopyTo(padded, 0);
        var f = ApiV3Codec.Parse(padded)!;
        Assert.True(f.CrcOk);
        Assert.Equal(0x0001, f.Opcode);
    }

    [Fact]
    public void Parse_FlagsBadCrc()
    {
        byte[] frame = ApiV3Codec.BuildFrame(0x0000);
        frame[^1] ^= 0xFF;
        Assert.False(ApiV3Codec.Parse(frame)!.CrcOk);
    }

    [Fact]
    public void Parse_TruncatedIsNull() =>
        Assert.Null(ApiV3Codec.Parse([0x00, 0x00, 0x04, 0x00]));

    [Fact]
    public void DeclaredPacketLength_RejectsImpossible()
    {
        Assert.Equal(6, ApiV3Codec.DeclaredPacketLength([0, 0, 0, 0]));
        Assert.Equal(-1, ApiV3Codec.DeclaredPacketLength([0, 0, 0xFF, 0xFF]));
        Assert.Equal(128, ApiV3Codec.DeclaredPacketLength([0, 0, 122, 0]));
    }
}
