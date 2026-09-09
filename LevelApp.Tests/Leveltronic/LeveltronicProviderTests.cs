using System.Buffers.Binary;
using LevelApp.Core.Instruments;
using LevelApp.Core.Models;
using LevelApp.Instruments.Leveltronic;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

public sealed class LeveltronicProviderTests
{
    private static KnownDevice Device(string transportId = "usb-hid") => new(
        DeviceId:         "dev-1",
        PluginId:         "leveltronic",
        TransportId:      transportId,
        DisplayName:      "Leveltronic #1",
        TransportAddress: "abc");

    private static FakeLeveltronicLink LinkWithIdentity()
    {
        byte[] body = new byte[27];
        body[0] = 1; body[1] = 0; body[2] = 0;
        return new FakeLeveltronicLink().RespondOk(LeveltronicApi.GetIdentity, body);
    }

    [Fact]
    public async Task Connect_OpensLink_HandshakesIdentity_StateConnected()
    {
        var link = LinkWithIdentity();
        var provider = new LeveltronicProvider(Device(), () => link, autoReconnect: false);

        await provider.ConnectAsync();

        Assert.Equal(InstrumentConnectionState.Connected, provider.ConnectionState);
        Assert.True(link.IsConnected);
    }

    [Fact]
    public async Task GetReading_ReturnsParsedMicronsPerMetre()
    {
        var link = LinkWithIdentity();
        byte[] incl = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(incl, 4200);
        link.RespondOk(LeveltronicApi.GetInclination, incl);

        var provider = new LeveltronicProvider(Device(), () => link, autoReconnect: false);
        await provider.ConnectAsync();

        double reading = await provider.GetReadingAsync(new MeasurementStep(), CancellationToken.None);

        Assert.Equal(4200, reading);
    }

    [Fact]
    public async Task GetReading_NoFirmwareResource_Surfaces_NotSupported()
    {
        var link = LinkWithIdentity()
            .RespondStatus(LeveltronicApi.GetInclination, Api2Status.UnknownResource);
        var provider = new LeveltronicProvider(Device(), () => link, autoReconnect: false);
        await provider.ConnectAsync();

        await Assert.ThrowsAsync<InstrumentResourceNotSupportedException>(
            () => provider.GetReadingAsync(new MeasurementStep(), CancellationToken.None));
    }

    [Fact]
    public async Task Disconnect_TearsDownLink()
    {
        var link = LinkWithIdentity();
        var provider = new LeveltronicProvider(Device(), () => link, autoReconnect: false);
        await provider.ConnectAsync();

        await provider.DisconnectAsync();

        Assert.Equal(InstrumentConnectionState.Disconnected, provider.ConnectionState);
        Assert.True(link.Disposed);
    }

    [Fact]
    public async Task Connect_Failure_SetsErrorState()
    {
        var provider = new LeveltronicProvider(
            Device(),
            () => throw new InvalidOperationException("no device"),
            autoReconnect: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ConnectAsync());
        Assert.Equal(InstrumentConnectionState.Error, provider.ConnectionState);
    }

    [Fact]
    public async Task UnknownTransport_ThrowsNotSupported()
    {
        // Default ctor -> real CreateLink, which rejects an unknown transport id
        // before touching any WinRT API.
        var provider = new LeveltronicProvider(Device("carrier-pigeon"));

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.ConnectAsync());
    }
}
