namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>
/// Device API v2 verbs. Encoded in the top nibble of the 16-bit opcode.
/// </summary>
public enum Api2Verb : byte
{
    Get         = 0x0,
    Set         = 0x1,
    Execute     = 0x2,
    Subscribe   = 0x3,
    Unsubscribe = 0x4,
    StartBulk   = 0x5,
    CancelBulk  = 0x6,
}

/// <summary>
/// Device API v2 categories. Encoded in the second nibble of the opcode.
/// </summary>
public enum Api2Category : byte
{
    SystemStatus = 0x0,
    Commands     = 0x1,
    Calibrations = 0x2,
    Settings     = 0x3,
    Measurements = 0x4,
    TopicGroups  = 0x5,
    DebugMessages = 0x6,
    RawData      = 0x7,
    Bulk         = 0x8,
}

/// <summary>
/// First payload byte of every API v2 response.
/// </summary>
public enum Api2Status : byte
{
    Ok               = 0x00,
    UnknownCategory  = 0x01,
    VerbNotValid     = 0x02,
    UnknownResource  = 0x03,
    BadCrc           = 0x04,
    BadLength        = 0x05,
    BusyResource     = 0x06,
    BusyExclusive    = 0x07,
    InvalidParameter = 0x08,
    NotSubscribed    = 0x09,
    NothingToCancel  = 0x0A,
}
