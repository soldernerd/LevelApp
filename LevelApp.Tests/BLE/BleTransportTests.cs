using LevelApp.Core.Instruments;
using LevelApp.Instruments.BLE;

namespace LevelApp.Tests.BLE;

public sealed class BleTransportTests
{
    private readonly BleTransport _sut = new();

    [Fact]
    public void TransportId_Is_ble()
        => Assert.Equal("ble", _sut.TransportId);

    [Fact]
    public void DisplayName_Is_Bluetooth()
        => Assert.Equal("Bluetooth", _sut.DisplayName);

    [Fact]
    public void Capabilities_Include_SingleReading()
        => Assert.True(_sut.Capabilities.HasFlag(TransportCapabilities.SingleReading));

    [Fact]
    public void Capabilities_Include_ContinuousStream()
        => Assert.True(_sut.Capabilities.HasFlag(TransportCapabilities.ContinuousStream));

    [Fact]
    public void Capabilities_Include_Bidirectional()
        => Assert.True(_sut.Capabilities.HasFlag(TransportCapabilities.Bidirectional));
}
