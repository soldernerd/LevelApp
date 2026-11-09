using System.Buffers.Binary;

namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>
/// Rebuilds whole API v3 packets from a byte stream (BLE, UART). Only packets
/// that pass their CRC are returned.
/// <para>
/// Resynchronisation: a declared length beyond <see cref="ApiV3Codec.MaxPacketBytes"/>,
/// or a candidate packet whose CRC fails, means the stream is out of step (a
/// lost notification, a bit error). One byte is dropped and parsing retried, so
/// a valid packet lying behind the damage is still found. Discards are counted
/// in <see cref="CrcErrors"/> / <see cref="Desyncs"/>.
/// </para>
/// </summary>
public sealed class PacketReassembler
{
    private readonly List<byte> _buffer = new(ApiV3Codec.MaxPacketBytes);

    /// <summary>Candidate packets rejected by their CRC.</summary>
    public int CrcErrors { get; private set; }

    /// <summary>Headers that declared an impossible length.</summary>
    public int Desyncs { get; private set; }

    public IReadOnlyList<byte[]> Feed(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data);
        var packets = new List<byte[]>();

        while (_buffer.Count >= ApiV3Codec.HeaderBytes)
        {
            int payLen = BinaryPrimitives.ReadUInt16LittleEndian(Window(2, 2));
            int total = ApiV3Codec.HeaderBytes + payLen + ApiV3Codec.CrcBytes;

            if (total > ApiV3Codec.MaxPacketBytes)
            {
                Desyncs++;
                _buffer.RemoveAt(0);
                continue;
            }
            if (_buffer.Count < total)
                break;

            byte[] candidate = Window(0, total).ToArray();
            if (ApiV3Codec.Parse(candidate) is { CrcOk: true })
            {
                packets.Add(candidate);
                _buffer.RemoveRange(0, total);
            }
            else
            {
                CrcErrors++;
                _buffer.RemoveAt(0);
            }
        }
        return packets;
    }

    /// <summary>Discard any partially-received packet.</summary>
    public void Reset() => _buffer.Clear();

    private ReadOnlySpan<byte> Window(int start, int length) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer).Slice(start, length);
}

/// <summary>
/// Rebuilds packets from 64-byte USB HID IN reports (API v3 spec §2.2): a
/// packet always starts at the beginning of a report; the first report carries
/// <c>LEN</c> and the packet occupies <c>ceil((6 + LEN) / 64)</c> reports, the
/// last zero-padded. Mirrors <c>HidReassembler</c> in the firmware's
/// <c>PythonTestCode/apiv3.py</c>.
/// </summary>
public sealed class HidReportReassembler
{
    public const int ReportBytes = 64;

    private readonly List<byte> _buffer = new(ApiV3Codec.MaxPacketBytes);
    private int _total;

    /// <summary>Feed one report (without the report-id byte). Returns the packet it completes, if any.</summary>
    public byte[]? Feed(ReadOnlySpan<byte> report)
    {
        if (_total == 0)
        {
            int declared = ApiV3Codec.DeclaredPacketLength(report);
            if (declared < 0) return null;
            _total = declared;
            _buffer.Clear();
        }

        int take = Math.Min(report.Length, _total - _buffer.Count);
        for (int i = 0; i < take; i++) _buffer.Add(report[i]);

        if (_buffer.Count < _total) return null;

        byte[] packet = [.. _buffer];
        _buffer.Clear();
        _total = 0;
        return ApiV3Codec.Parse(packet) is { CrcOk: true } ? packet : null;
    }

    public void Reset()
    {
        _buffer.Clear();
        _total = 0;
    }

    /// <summary>Split a packet into zero-padded 64-byte reports (without report id).</summary>
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> packet)
    {
        var reports = new List<byte[]>();
        for (int i = 0; i < packet.Length; i += ReportBytes)
        {
            var r = new byte[ReportBytes];
            packet.Slice(i, Math.Min(ReportBytes, packet.Length - i)).CopyTo(r);
            reports.Add(r);
        }
        return reports;
    }
}
