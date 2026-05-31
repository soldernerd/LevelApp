using LevelApp.Core.Instruments;
using LevelApp.Instruments.UsbHid;

namespace LevelApp.Tests.UsbHid;

public sealed class UsbHidTransportTests
{
    private readonly UsbHidTransport _sut = new();

    [Fact]
    public void TransportId_Is_usb_hid()
        => Assert.Equal("usb-hid", _sut.TransportId);

    [Fact]
    public void DisplayName_Is_USB()
        => Assert.Equal("USB", _sut.DisplayName);

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
