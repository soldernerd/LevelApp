using System.Buffers.Binary;

namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>
/// Rebuilds whole API v2 packets from a byte stream.
/// <para>
/// Needed for BLE (RN4871 Transparent UART) and wired UART, which deliver a raw
/// byte stream that must be reassembled by the <c>LEN</c> field. USB HID
/// delivers one whole report per packet and does not need this — hand each
/// report straight to <see cref="ApiV2Codec.TryParseFrame"/>.
/// </para>
/// <para>Mirrors the <c>Reassembler</c> class in <c>PythonTestCode/apiv2.py</c>.</para>
/// </summary>
public sealed class PacketReassembler
{
    private readonly List<byte> _buffer = new(ApiV2Codec.MaxPacketBytes);

    /// <summary>
    /// Append received bytes and return every complete packet now available.
    /// A packet whose declared total exceeds <see cref="ApiV2Codec.MaxPacketBytes"/>
    /// is treated as a desync: one byte is dropped and reassembly retried.
    /// A packet that frames correctly but fails CRC is dropped (not returned).
    /// </summary>
    public IReadOnlyList<byte[]> Feed(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data);

        var packets = new List<byte[]>();

        while (_buffer.Count >= ApiV2Codec.HeaderBytes)
        {
            int payLen = BinaryPrimitives.ReadUInt16LittleEndian(
                CollectionsMarshalSpan(2, 2));
            int total = ApiV2Codec.HeaderBytes + payLen + ApiV2Codec.CrcBytes;

            if (total > ApiV2Codec.MaxPacketBytes)
            {
                _buffer.RemoveAt(0); // resync
                continue;
            }

            if (_buffer.Count < total)
                break;

            var candidate = CollectionsMarshalSpan(0, total).ToArray();

            if (ApiV2Codec.TryParseFrame(candidate, out _, out _, out _))
            {
                packets.Add(candidate);
                _buffer.RemoveRange(0, total);
            }
            else
            {
                // Plausible header but bad CRC — a false alignment on a garbage
                // stream. Resync one byte at a time rather than swallowing a
                // whole speculative frame (which could eat a real packet's start).
                _buffer.RemoveAt(0);
            }
        }

        return packets;
    }

    /// <summary>Discard any partially-received packet.</summary>
    public void Reset() => _buffer.Clear();

    private ReadOnlySpan<byte> CollectionsMarshalSpan(int start, int length) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer).Slice(start, length);
}
