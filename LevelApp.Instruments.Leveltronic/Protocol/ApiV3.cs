namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>Device API v3 verbs (top nibble of the 16-bit opcode).</summary>
public enum ApiVerb : byte
{
    Get         = 0x0,
    Set         = 0x1,
    Execute     = 0x2,
    Subscribe   = 0x3,
    Unsubscribe = 0x4,
    StartBulk   = 0x5,
    CancelBulk  = 0x6,
}

/// <summary>Device API v3 categories (second nibble of the opcode).</summary>
public enum ApiCategory : byte
{
    System       = 0x0,
    Commands     = 0x1,
    Calibrations = 0x2,
    Settings     = 0x3,
    Measurements = 0x4,
    Topics       = 0x5,
    DebugLog     = 0x6,
    Diagnostics  = 0x7,
    Bulk         = 0x8,
    Procedures   = 0x9,
}

/// <summary>First payload byte of every API v3 response.</summary>
public enum ApiStatus : byte
{
    Ok                  = 0x00,
    UnknownCategory     = 0x01,
    VerbNotValid        = 0x02,
    UnknownResource     = 0x03,
    BadCrc              = 0x04,
    BadLength           = 0x05,
    BusyResource        = 0x06,
    BusyExclusive       = 0x07,
    InvalidParameter    = 0x08,
    NotSubscribed       = 0x09,
    NothingToCancel     = 0x0A,
    ServiceModeRequired = 0x0B,

    /// <summary>Not a device code: an empty payload or an undefined status byte.</summary>
    Unknown             = 0xFF,
}

/// <summary>
/// A decoded API frame. <see cref="Data"/> is the payload after the status byte.
/// </summary>
public sealed record ApiFrame(ushort Opcode, ApiStatus Status, byte[] Data, bool CrcOk)
{
    public bool IsOk => CrcOk && Status == ApiStatus.Ok;

    public ApiVerb Verb => ApiV3Codec.VerbOf(Opcode);

    /// <summary>
    /// A frame that starts with <c>[issue_seq][page]</c> is a subscription push;
    /// a SUBSCRIBE ack (or an error) carries no data after the status byte.
    /// </summary>
    public bool LooksLikePush => Status == ApiStatus.Ok && Data.Length >= 2;

    /// <summary>The push data after <c>[issue_seq][page]</c>.</summary>
    public byte[] PushData => Data.Length >= 2 ? Data[2..] : [];
}
