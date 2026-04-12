namespace LevelApp.Tests.Replay;

/// <summary>
/// Thrown by <see cref="RecordedInstrumentProvider"/> when all recorded readings
/// have been consumed and a further read is requested.
/// </summary>
public sealed class EndOfRecordingException : Exception
{
    public EndOfRecordingException(string message) : base(message) { }
}
