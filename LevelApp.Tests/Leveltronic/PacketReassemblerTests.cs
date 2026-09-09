using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>
/// Byte-stream reassembly tests for <see cref="PacketReassembler"/> (BLE path).
/// </summary>
public sealed class PacketReassemblerTests
{
    private static byte[] Frame(ushort op, params byte[] payload) =>
        ApiV2Codec.BuildFrame(op, payload);

    [Fact]
    public void SinglePacket_InOneChunk()
    {
        var r = new PacketReassembler();
        byte[] f = Frame(0x0001, 1, 2, 3);

        var packets = r.Feed(f);

        Assert.Single(packets);
        Assert.Equal(f, packets[0]);
    }

    [Fact]
    public void PacketSplitAcrossChunks_ReassembledOnCompletion()
    {
        var r = new PacketReassembler();
        byte[] f = Frame(0x0001, 9, 8, 7, 6, 5);

        Assert.Empty(r.Feed(f.AsSpan(0, 3)));
        Assert.Empty(r.Feed(f.AsSpan(3, 4)));
        var packets = r.Feed(f.AsSpan(7));

        Assert.Single(packets);
        Assert.Equal(f, packets[0]);
    }

    [Fact]
    public void TwoPacketsInOneChunk_BothReturned()
    {
        var r = new PacketReassembler();
        byte[] a = Frame(0x0000);
        byte[] b = Frame(0x0410, 0x03);

        var packets = r.Feed([.. a, .. b]);

        Assert.Equal(2, packets.Count);
        Assert.Equal(a, packets[0]);
        Assert.Equal(b, packets[1]);
    }

    [Fact]
    public void GarbagePrefix_ResyncsToNextValidPacket()
    {
        var r = new PacketReassembler();
        byte[] good = Frame(0x0001, 42);

        // Leading junk whose "length" field would exceed MaxPacketBytes forces
        // a byte-by-byte resync until the real packet aligns.
        var packets = r.Feed([0xFF, 0xFF, 0xFF, 0xFF, .. good]);

        Assert.Contains(packets, p => p.SequenceEqual(good));
    }

    [Fact]
    public void CrcFailure_PacketDropped_NotReturned()
    {
        var r = new PacketReassembler();
        byte[] f = Frame(0x0001, 1, 2, 3);
        f[^2] ^= 0xFF; // corrupt CRC but keep the length field intact

        Assert.Empty(r.Feed(f));
    }

    [Fact]
    public void Reset_DiscardsPartialPacket()
    {
        var r = new PacketReassembler();
        byte[] f = Frame(0x0001, 1, 2, 3, 4);

        r.Feed(f.AsSpan(0, 4));
        r.Reset();
        var packets = r.Feed(f.AsSpan(4));

        Assert.Empty(packets); // the earlier half was dropped, remainder is not a packet
    }
}
