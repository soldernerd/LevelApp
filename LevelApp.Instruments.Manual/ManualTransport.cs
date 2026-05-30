using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;

namespace LevelApp.Instruments.Manual;

public sealed class ManualTransport : ITransport
{
    public string TransportId  => "manual";
    public string DisplayName  => "Manual";
    public TransportCapabilities Capabilities => TransportCapabilities.SingleReading;
}
