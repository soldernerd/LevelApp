using System.Buffers.Binary;

namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>
/// Framing and integrity helpers for Device API v3.
/// <para>
/// Wire packet: <c>[OPCODE 2B LE][LEN 2B LE][PAYLOAD 0..LEN][CRC16 2B LE]</c>,
/// total <c>6 + LEN</c>, <c>LEN</c> at most <see cref="MaxPayload"/>. CRC-16/CCITT-FALSE
/// (poly <c>0x1021</c>, init <c>0xFFFF</c>, no reflection, no final XOR) over
/// <c>OPCODE + LEN + PAYLOAD</c>. See <c>InclinationMeterFirmware/docs/api-v3-spec.md</c>.
/// </para>
/// </summary>
public static class ApiV3Codec
{
    /// <summary>The protocol version reported by System IDENTITY.</summary>
    public const int ApiVersion = 3;

    /// <summary>
    /// The newest API minor this app knows (3.1, firmware 0.15.0). Only the major decides
    /// compatibility; a device with a higher minor still works, it just offers more.
    /// </summary>
    public const int KnownApiMinor = 1;

    public const int HeaderBytes = 4;
    public const int CrcBytes = 2;

    /// <summary>Largest payload any packet may carry (IDENTITY <c>max_payload</c>).</summary>
    public const int MaxPayload = 122;

    /// <summary>Largest packet on the wire.</summary>
    public const int MaxPacketBytes = HeaderBytes + MaxPayload + CrcBytes;

    // ── Opcode ────────────────────────────────────────────────────────────────

    public static ushort Opcode(ApiVerb verb, ApiCategory category, byte resource) =>
        (ushort)((((int)verb & 0xF) << 12) | (((int)category & 0xF) << 8) | resource);

    public static ApiVerb VerbOf(ushort opcode) => (ApiVerb)((opcode >> 12) & 0xF);
    public static ApiCategory CategoryOf(ushort opcode) => (ApiCategory)((opcode >> 8) & 0xF);
    public static byte ResourceOf(ushort opcode) => (byte)(opcode & 0xFF);

    /// <summary>The same resource with another verb — e.g. UNSUBSCRIBE for a SUBSCRIBE opcode.</summary>
    public static ushort WithVerb(ushort opcode, ApiVerb verb) =>
        (ushort)((opcode & 0x0FFF) | (((int)verb & 0xF) << 12));

    public static ushort Get(ApiCategory c, byte r) => Opcode(ApiVerb.Get, c, r);
    public static ushort Set(ApiCategory c, byte r) => Opcode(ApiVerb.Set, c, r);
    public static ushort Execute(byte r) => Opcode(ApiVerb.Execute, ApiCategory.Commands, r);
    public static ushort Subscribe(ApiCategory c, byte r) => Opcode(ApiVerb.Subscribe, c, r);

    // ── CRC-16/CCITT-FALSE ────────────────────────────────────────────────────

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

    public static byte[] BuildFrame(ushort opcode, ReadOnlySpan<byte> payload = default)
    {
        if (payload.Length > MaxPayload)
            throw new ArgumentException($"Payload of {payload.Length} B exceeds {MaxPayload} B.", nameof(payload));

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
    /// Parse one complete packet (trailing zero padding past <c>6 + LEN</c>, as in
    /// a HID report, is ignored). Returns <see langword="null"/> for a truncated
    /// packet; a CRC mismatch yields a frame with <see cref="ApiFrame.CrcOk"/> false.
    /// </summary>
    public static ApiFrame? Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderBytes + CrcBytes)
            return null;

        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(packet);
        int payLen = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2, 2));
        if (HeaderBytes + payLen + CrcBytes > packet.Length)
            return null;

        ushort wantCrc = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(HeaderBytes + payLen, CrcBytes));
        bool crcOk = Crc16Ccitt(packet[..(HeaderBytes + payLen)]) == wantCrc;

        if (payLen == 0)
            return new ApiFrame(opcode, ApiStatus.Unknown, [], crcOk);

        byte status = packet[HeaderBytes];
        return new ApiFrame(
            opcode,
            Enum.IsDefined(typeof(ApiStatus), status) ? (ApiStatus)status : ApiStatus.Unknown,
            packet.Slice(HeaderBytes + 1, payLen - 1).ToArray(),
            crcOk);
    }

    /// <summary>Total packet length declared by a header, or -1 if it is impossible.</summary>
    public static int DeclaredPacketLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderBytes) return -1;
        int total = HeaderBytes + BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2, 2)) + CrcBytes;
        return total > MaxPacketBytes ? -1 : total;
    }
}
