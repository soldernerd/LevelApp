namespace LevelApp.Core.Instruments;

[Flags]
public enum TransportCapabilities
{
    None             = 0,
    SingleReading    = 1 << 0,
    ContinuousStream = 1 << 1,
    Bidirectional    = 1 << 2,  // can send commands to device
}
