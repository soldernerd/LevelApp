namespace LevelApp.Core.Instruments;

[Flags]
public enum InstrumentCapabilities
{
    None              = 0,
    SingleMeasurement = 1 << 0,  // supports one-shot GetReadingAsync()
    ContinuousStream  = 1 << 1,  // supports SubscribeToReadingsAsync()
}
