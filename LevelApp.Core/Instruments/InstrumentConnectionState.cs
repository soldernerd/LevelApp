namespace LevelApp.Core.Instruments;

public enum InstrumentConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Degraded,  // connected but signal/quality poor
    Error      // unrecoverable until reconnect attempted
}
