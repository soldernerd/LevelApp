namespace LevelApp.Core.Interfaces;

/// <summary>
/// Optional capability of an <see cref="IInstrumentProvider"/>: a line describing the physical
/// instrument that is connected (serial number, firmware ...). Recorded with a measurement so a result
/// can be traced to the exact instrument that produced it.
/// </summary>
public interface IInstrumentDetails
{
    /// <summary>E.g. "serial 6DA98059 · firmware 0.15.0 · API 3.1"; null while not connected or unknown.</summary>
    string? Details { get; }
}
