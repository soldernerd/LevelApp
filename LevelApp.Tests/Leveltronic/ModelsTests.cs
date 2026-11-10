using System.Buffers.Binary;
using System.Text;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>Payload decoders, against byte fixtures laid out per the firmware's api-reference.md.</summary>
public sealed class ModelsTests
{
    private static byte[] Str(string s, int len)
    {
        var b = new byte[len];
        Encoding.ASCII.GetBytes(s).CopyTo(b, 0);
        return b;
    }

    [Fact]
    public void Identity_V3_50Bytes()
    {
        byte[] d = new byte[50];
        d[0] = 0; d[1] = 11; d[2] = 5; d[3] = 3;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(4), 122);
        Str("InclinationMeter", 16).CopyTo(d, 6);
        Str("6DA08073", 8).CopyTo(d, 22);
        Str("1371e8b0-dirty", 20).CopyTo(d, 30);

        var id = DeviceIdentity.Decode(d)!;

        Assert.Equal("0.11.5", id.Firmware);
        Assert.Equal(3, id.ApiVersion);
        Assert.Equal(122, id.MaxPayload);
        Assert.Equal("InclinationMeter", id.Product);
        Assert.Equal("6DA08073", id.Serial);
        Assert.Equal("1371e8b0-dirty", id.Build);
    }

    [Fact]
    public void Identity_V3_50Bytes_ReadsAsApi30()
    {
        var id = DeviceIdentity.Decode(new byte[50])!;

        Assert.Equal(0, id.ApiMinor);
        Assert.Equal("0.0", id.ApiVersionText);
        Assert.True(id.AtLeast(0, 0));
    }

    [Fact]
    public void Identity_V3_51Bytes_CarriesApiMinor()
    {
        byte[] d = new byte[51];
        d[0] = 0; d[1] = 15; d[2] = 0; d[3] = 3; d[50] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(4), 122);

        var id = DeviceIdentity.Decode(d)!;

        Assert.Equal(3, id.ApiVersion);
        Assert.Equal(1, id.ApiMinor);
        Assert.Equal("3.1", id.ApiVersionText);
        Assert.True(id.AtLeast(3, 1));
        Assert.True(id.AtLeast(3, 0));
        Assert.False(id.AtLeast(3, 2));
        Assert.False(id.AtLeast(4, 0));
    }

    [Fact]
    public void Identity_LongerThanKnown_IsAccepted() =>
        Assert.NotNull(DeviceIdentity.Decode(new byte[60]));

    [Fact]
    public void Identity_V2_19Bytes_DecodesAsApiVersion2()
    {
        byte[] d = new byte[19];
        d[0] = 0; d[1] = 9; d[2] = 13;
        Str("InclinationMeter", 16).CopyTo(d, 3);

        var id = DeviceIdentity.Decode(d)!;

        Assert.Equal(2, id.ApiVersion);
        Assert.Equal("0.9.13", id.Firmware);
        Assert.Equal("", id.Serial);
    }

    [Fact]
    public void Identity_OtherLengthIsNull() => Assert.Null(DeviceIdentity.Decode(new byte[30]));

    [Fact]
    public void SystemState_15Bytes()
    {
        byte[] d = new byte[15];
        d[0] = (byte)BatteryState.Charging; d[1] = 88;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(2), 4123);
        d[4] = 1; d[6] = 1; d[11] = 1; d[14] = 1;

        var s = SystemState.Decode(d)!;

        Assert.Equal(BatteryState.Charging, s.Battery);
        Assert.Equal(88, s.BatterySocPct);
        Assert.Equal(4123, s.BatteryMv);
        Assert.True(s.UsbConnected);
        Assert.False(s.BleConnected);
        Assert.True(s.Charging);
        Assert.True(s.DemodRunning);
        Assert.True(s.ServiceMode);
        Assert.Null(SystemState.Decode(new byte[14]));
    }

    [Fact]
    public void Health_31Bytes_AndResetNames()
    {
        byte[] d = new byte[31];
        d[0] = 1; d[5] = 1; d[8] = 0b0000_1010; // power-on + IWDG
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(10), 3661);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(16), 4);
        d[22] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(23), 0x0800_1234);

        var h = HealthInfo.Decode(d)!;

        Assert.True(h.AdcOk);
        Assert.Equal(3661u, h.UptimeS);
        Assert.Equal(4, h.BleTxDropped);
        Assert.Equal(1, h.LastFaultKind);
        Assert.Equal(0x0800_1234u, h.LastFaultPc);
        Assert.Equal(["power-on / brown-out", "independent watchdog"], h.ResetCauseNames);
    }

    [Fact]
    public void Rtc_10Bytes_AndSetEncoding()
    {
        byte[] d = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(d, 2026);
        d[2] = 11; d[3] = 9; d[4] = 1; d[5] = 10; d[6] = 20; d[7] = 30; d[8] = 1; d[9] = 128;

        var r = RtcInfo.Decode(d)!;

        Assert.Equal(new DateTime(2026, 11, 9, 10, 20, 30), r.DateTime);
        Assert.True(r.IsSet);
        Assert.Equal(128, r.Subsecond);
        Assert.Equal(new byte[] { 0xEA, 0x07, 11, 9, 10, 20, 30 }, RtcInfo.EncodeSet(r.DateTime));
    }

    [Fact]
    public void Environmental_14Bytes()
    {
        byte[] d = new byte[14];
        BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(0), 2380);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(2), 96400);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(6), 4100);
        d[8] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(9), -250);
        BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(11), 2200);

        var e = Environmental.Decode(d)!;

        Assert.Equal(23.80, e.BmeTempC, 3);
        Assert.Equal(964.0, e.PressureHpa, 3);
        Assert.Equal(41.0, e.HumidityPct, 3);
        Assert.Equal(-2.5, e.OnboardTempC, 3);
        Assert.False(e.ExternalTempOk);
    }

    [Fact]
    public void LiveTilt_15Bytes_FlagsAndValues()
    {
        byte[] d = new byte[15];
        BinaryPrimitives.WriteUInt16LittleEndian(d, 513);
        d[2] = 0b101; // valid + sensor 2 doubtful
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(3), 0.8f);
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(7), 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(11), 0.3f);

        var l = LiveTilt.Decode(d)!;

        Assert.Equal(513, l.Seq);
        Assert.True(l.Valid);
        Assert.False(l.S1Doubtful);
        Assert.True(l.S2Doubtful);
        Assert.True(l.AnyDoubtful);
        Assert.Equal(0.8, l.TiltS1, 5);
        Assert.Equal(0.3, l.TiltDiff, 5);
    }

    [Fact]
    public void PhasorEntry_RoundTripsThroughEncode()
    {
        var e = new PhasorEntry(-1, -2, 3, 4, 5, 6, 7, 8, 640);
        var back = PhasorEntry.Decode(e.Encode())!;
        Assert.Equal(e, back);
        Assert.Null(PhasorEntry.Decode(new byte[33]));
    }

    [Fact]
    public void PhasorEntry_WireOrderIsS2_B_A_S1()
    {
        // Firmware 0.13.0: the stream follows the ADC channels (iS2 qS2 iB qB iA qA iS1 qS1 seq).
        var e = new PhasorEntry(IB: 3, QB: 4, IA: 5, QA: 6, IS1: 7, QS1: 8, IS2: 1, QS2: 2, Seq: 9);
        var b = e.Encode();
        for (int i = 0; i < 8; i++)
            Assert.Equal(i + 1, BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(4 * i)));
        Assert.Equal(9, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(32)));
    }

    [Fact]
    public void SignalDiag_56Bytes()
    {
        byte[] d = new byte[56];
        for (int i = 0; i < 14; i++) BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(4 * i), i + 0.5f);

        var s = SignalDiag.Decode(d)!;

        Assert.Equal(0.5, s.RmsMv[0]);
        Assert.Equal(4.5, s.P2pMv[0]);
        Assert.Equal(11.5, s.PhaseDeg[3]);
        Assert.Equal(12.5, s.TheoreticalTiltS1);
        Assert.Equal(["S2", "B", "A", "S1"], SignalDiag.ChannelNames);
    }

    [Fact]
    public void ZeroCalStatus_6Bytes()
    {
        byte[] d = [(byte)ZeroCalPhase.Step1Done, 64, 0, 64, 0, 3];
        var z = ZeroCalStatus.Decode(d)!;
        Assert.Equal(ZeroCalPhase.Step1Done, z.Phase);
        Assert.Equal(64, z.Progress);
        Assert.Equal(64, z.Target);
        Assert.Equal(3, z.SensorMask);
    }

    [Fact]
    public void PrecisionStatus_23Bytes()
    {
        byte[] d = new byte[23];
        d[0] = (byte)PrecisionPhase.Done;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(1), 81);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(3), 81);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(5), 2050);
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(10), 0.8f);
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(14), 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(18), 0.3f);

        var p = PrecisionStatus.Decode(d)!;

        Assert.Equal(PrecisionPhase.Done, p.Phase);
        Assert.Equal(81, p.Target);
        Assert.Equal(2050, p.ElapsedMs);
        Assert.False(p.Failed);
        Assert.Equal(0.3, p.TiltDiff, 5);
    }

    [Fact]
    public void ValueTypes_RoundTrip()
    {
        Assert.Equal(-1234, WireType.I32.Decode(WireType.I32.Encode(-1234)));
        Assert.Equal(-4500, WireType.I16.Decode(WireType.I16.Encode(-4500)));
        Assert.Equal(65535, WireType.U16.Decode(WireType.U16.Encode(65535)));
        Assert.Equal(1, WireType.U8.Decode(WireType.U8.Encode(1)));
        Assert.Equal(4, WireType.I32.Bytes());
        Assert.False(WireType.I32.Covers(new byte[3]));
    }

    [Fact]
    public void Catalog_HasTheFirmwareRanges()
    {
        Assert.Equal(-18000, ValueCatalog.S1Phase.Min);
        Assert.Equal(18000, ValueCatalog.S2Phase.Max);
        Assert.All(ValueCatalog.Calibrations, d => Assert.True(d.Service));
        Assert.DoesNotContain(ValueCatalog.Settings, d => d.Service);
        Assert.Equal(ValueCatalog.Settings.Count, ValueCatalog.Settings.Select(d => d.Id).Distinct().Count());
        Assert.False(ValueCatalog.Settings.Single(d => d.Id == 0x53).Optional);
        Assert.Equal(100, ValueCatalog.Settings.Single(d => d.Id == 0x54).Max);
        Assert.Equal(["S2", "B", "A", "S1"], SignalDiag.ChannelNames);
        Assert.Equal(4, ValueCatalog.ChannelPga.Count);
        Assert.Equal(0x4A, ValueCatalog.ChannelPga[2].Id);
    }
}
