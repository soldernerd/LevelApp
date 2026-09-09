using System.Buffers.Binary;

namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>
/// Framing and integrity helpers for Device API v2.
/// <para>
/// Wire packet: <c>[OPCODE 2B LE][LEN 2B LE][PAYLOAD 0..LEN][CRC16 2B LE]</c>,
/// no padding, total size <c>6 + LEN</c>. <c>CRC16</c> is CRC-16/CCITT-FALSE
/// (poly <c>0x1021</c>, init <c>0xFFFF</c>, no reflection, no final XOR)
/// computed over <c>OPCODE + LEN + PAYLOAD</c>.
/// </para>
/// <para>
/// Mirrors <c>PythonTestCode/apiv2.py</c> in the firmware repository, which is
/// the reference implementation.
/// </para>
/// </summary>
public static class ApiV2Codec
{
    /// <summary>Bytes before the payload: OPCODE (2) + LEN (2).</summary>
    public const int HeaderBytes = 4;

    /// <summary>Trailing CRC-16 bytes.</summary>
    public const int CrcBytes = 2;

    /// <summary>
    /// Reassembly ceiling. The firmware caps a packet at 128 bytes on this
    /// build (<c>API2_PACKET_MAX_SIZE</c>); anything larger on the wire is a
    /// desync and triggers a resync.
    /// </summary>
    public const int MaxPacketBytes = 128;

    // ── Opcode ────────────────────────────────────────────────────────────────

    /// <summary>Compose a 16-bit opcode from verb, category and resource index.</summary>
    public static ushort Opcode(Api2Verb verb, Api2Category category, byte resource) =>
        (ushort)((((int)verb & 0xF) << 12) | (((int)category & 0xF) << 8) | resource);

    public static Api2Verb VerbOf(ushort opcode) => (Api2Verb)((opcode >> 12) & 0xF);
    public static Api2Category CategoryOf(ushort opcode) => (Api2Category)((opcode >> 8) & 0xF);
    public static byte ResourceOf(ushort opcode) => (byte)(opcode & 0xFF);

    // ── CRC-16/CCITT-FALSE ────────────────────────────────────────────────────

    /// <summary>
    /// CRC-16/CCITT-FALSE over <paramref name="data"/>: poly <c>0x1021</c>,
    /// init <c>0xFFFF</c>, no input/output reflection, no final XOR.
    /// </summary>
    public static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
        }
        return crc;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a complete on-wire packet for <paramref name="opcode"/> with the
    /// given (possibly empty) <paramref name="payload"/>.
    /// </summary>
    public static byte[] BuildFrame(ushort opcode, ReadOnlySpan<byte> payload = default)
    {
        var frame = new byte[HeaderBytes + payload.Length + CrcBytes];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0), opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderBytes));

        ushort crc = Crc16Ccitt(frame.AsSpan(0, HeaderBytes + payload.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(HeaderBytes + payload.Length), crc);
        return frame;
    }

    // ── Parse ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse one complete on-wire packet.
    /// <para>
    /// USB HID zero-pads reports to 64 bytes; pass the whole report — trailing
    /// pad bytes past <c>6 + LEN</c> are ignored. Returns <see langword="false"/>
    /// for a truncated packet or a CRC mismatch.
    /// </para>
    /// </summary>
    /// <param name="packet">The received bytes (may include trailing padding).</param>
    /// <param name="opcode">The echoed request opcode.</param>
    /// <param name="status">
    /// The response status (payload byte 0), or <see langword="null"/> when the
    /// payload is empty.
    /// </param>
    /// <param name="data">The resource bytes that follow the status byte.</param>
    public static bool TryParseFrame(
        ReadOnlySpan<byte> packet,
        out ushort opcode,
        out Api2Status? status,
        out byte[] data)
    {
        opcode = 0;
        status = null;
        data = [];

        if (packet.Length < HeaderBytes + CrcBytes)
            return false;

        opcode = BinaryPrimitives.ReadUInt16LittleEndian(packet[..2]);
        int payLen = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2, 2));

        int total = HeaderBytes + payLen + CrcBytes;
        if (payLen < 0 || total > packet.Length)
            return false;

        ushort wantCrc = BinaryPrimitives.ReadUInt16LittleEndian(
            packet.Slice(HeaderBytes + payLen, CrcBytes));
        ushort gotCrc = Crc16Ccitt(packet[..(HeaderBytes + payLen)]);
        if (wantCrc != gotCrc)
            return false;

        if (payLen == 0)
        {
            status = null;
            data = [];
            return true;
        }

        status = (Api2Status)packet[HeaderBytes];
        data = payLen > 1 ? packet.Slice(HeaderBytes + 1, payLen - 1).ToArray() : [];
        return true;
    }
}
