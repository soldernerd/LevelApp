using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// Thrown when a Leveltronic device returns a non-OK API v2 status code, or when
/// a request is not answered in time.
/// </summary>
public class LeveltronicProtocolException : Exception
{
    public LeveltronicProtocolException(string message) : base(message) { }

    public LeveltronicProtocolException(ushort opcode, Api2Status status)
        : base($"Device rejected opcode 0x{opcode:X4} with status {status} (0x{(byte)status:X2}).")
    {
        Opcode = opcode;
        Status = status;
    }

    /// <summary>The request opcode, when the failure was a status code.</summary>
    public ushort? Opcode { get; }

    /// <summary>The returned status, when the failure was a status code.</summary>
    public Api2Status? Status { get; }
}

/// <summary>
/// Thrown when the device reports <see cref="Api2Status.UnknownResource"/> — the
/// running firmware build does not implement the requested resource. For the
/// inclination read this is expected until the angle Measurements resource is
/// added to the firmware.
/// </summary>
public sealed class InstrumentResourceNotSupportedException : LeveltronicProtocolException
{
    public InstrumentResourceNotSupportedException(string message) : base(message) { }
}
