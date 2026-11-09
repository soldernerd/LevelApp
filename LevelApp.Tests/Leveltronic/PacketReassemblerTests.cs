using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>Byte-stream (BLE) and HID-report reassembly.</summary>
public sealed class PacketReassemblerTests
{
    private static byte[] Frame(ushort op, params byte[] payload) => ApiV3Codec.BuildFrame(op, payload);

    [Fact]
    public void SinglePacket_InOneChunk()
    {
        var r = new PacketReassembler();
        byte[] f = Frame(0x0001, 0, 1, 2);
        Assert.Equal(f, Assert.Single(r.Feed(f)));
    }

    [Fact]
    public void PacketSplitAcrossChunks_ReassembledOnCompletion()
    {
        var r = new PacketReassembler();
        byte[] f = Frame(0x0001, 0, 9, 8, 7, 6, 5);

        Assert.Empty(r.Feed(f.AsSpan(0, 3)));
        Assert.Empty(r.Feed(f.AsSpan(3, 4)));
        Assert.Equal(f, Assert.Single(r.Feed(f.AsSpan(7))));
    }

    [Fact]
    public void TwoPacketsInOneChunk_BothReturned()
    {
        var r = new PacketReassembler();
        byte[] a = Frame(0x0000, 0), b = Frame(0x0502, 0, 3);
        var packets = r.Feed([.. a, .. b]);
        Assert.Equal(2, packets.Count);
        Assert.Equal(a, packets[0]);
        Assert.Equal(b, packets[1]);
    }

    [Fact]
    public void LargestPacket_IsAccepted()
    {
        var r = new PacketReassembler();
        byte[] f = Frame(0x0901, new byte[ApiV3Codec.MaxPayload]);
        Assert.Equal(f, Assert.Single(r.Feed(f)));
    }

    [Fact]
    public void GarbagePrefix_ResyncsToNextValidPacket()
    {
        var r = new PacketReassembler();
        byte[] good = Frame(0x0001, 0, 42);
        Assert.Contains(r.Feed([0xFF, 0xFF, 0xFF, 0xFF, .. good]), p => p.SequenceEqual(good));
        Assert.True(r.Desyncs > 0);
    }

    [Fact]
    public void CrcFailure_IsDroppedAndCounted_NextPacketStillFound()
    {
        var r = new PacketReassembler();
        byte[] bad = Frame(0x0001, 0, 1, 2, 3);
        bad[^2] ^= 0xFF;
        byte[] good = Frame(0x0002, 0, 9);

        var packets = r.Feed([.. bad, .. good]);

        Assert.Equal(good, Assert.Single(packets));
        Assert.True(r.CrcErrors > 0);
    }

    [Fact]
    public void Reset_DiscardsPartialPacket()
    {
        var r = new PacketReassembler();
        byte[] f = Frame(0x0001, 0, 1, 2, 3, 4);
        r.Feed(f.AsSpan(0, 4));
        r.Reset();
        Assert.Empty(r.Feed(f.AsSpan(4)));
    }
}

public sealed class HidReportReassemblerTests
{
    [Fact]
    public void ShortPacket_IsOneReport()
    {
        byte[] frame = ApiV3Codec.BuildFrame(0x0003, [0, 1, 2]);
        var reports = HidReportReassembler.Split(frame);
        var r = new HidReportReassembler();

        Assert.Single(reports);
        Assert.Equal(64, reports[0].Length);
        Assert.Equal(frame, r.Feed(reports[0]));
    }

    [Fact]
    public void LongPacket_SpansReports_AndIsRebuiltFromTheLast()
    {
        byte[] payload = [0, .. Enumerable.Range(1, 99).Select(i => (byte)i)];
        byte[] frame = ApiV3Codec.BuildFrame(0x0901, payload); // 105 bytes -> 2 reports
        var reports = HidReportReassembler.Split(frame);
        var r = new HidReportReassembler();

        Assert.Equal(2, reports.Count);
        Assert.Null(r.Feed(reports[0]));
        Assert.Equal(frame, r.Feed(reports[1]));
    }

    [Fact]
    public void LargestPacket_NeedsTwoReports()
    {
        byte[] frame = ApiV3Codec.BuildFrame(0x0901, new byte[ApiV3Codec.MaxPayload]); // 128 bytes
        var reports = HidReportReassembler.Split(frame);
        var r = new HidReportReassembler();

        Assert.Equal(2, reports.Count);
        r.Feed(reports[0]);
        Assert.Equal(frame, r.Feed(reports[1]));
    }

    [Fact]
    public void TwoPacketsBackToBack_AreSeparate()
    {
        byte[] a = ApiV3Codec.BuildFrame(0x0001, [0, 1]);
        byte[] b = ApiV3Codec.BuildFrame(0x0002, [0, 2]);
        var r = new HidReportReassembler();

        Assert.Equal(a, r.Feed(HidReportReassembler.Split(a)[0]));
        Assert.Equal(b, r.Feed(HidReportReassembler.Split(b)[0]));
    }

    [Fact]
    public void ImpossibleLength_IsIgnored()
    {
        var r = new HidReportReassembler();
        var junk = new byte[64];
        junk[2] = 0xFF; junk[3] = 0xFF;
        Assert.Null(r.Feed(junk));

        // and the next real packet still gets through
        byte[] good = ApiV3Codec.BuildFrame(0x0001, [0]);
        Assert.Equal(good, r.Feed(HidReportReassembler.Split(good)[0]));
    }

    [Fact]
    public void CorruptPacket_IsDropped()
    {
        byte[] frame = ApiV3Codec.BuildFrame(0x0001, [0, 1, 2]);
        frame[^1] ^= 0xFF;
        Assert.Null(new HidReportReassembler().Feed(HidReportReassembler.Split(frame)[0]));
    }
}
