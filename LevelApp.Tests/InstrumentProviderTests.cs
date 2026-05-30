using LevelApp.Core.Instruments;
using LevelApp.Instruments.Manual;

namespace LevelApp.Tests;

public sealed class InstrumentProviderTests
{
    [Fact]
    public void ManualEntryProvider_IsAlwaysConnected()
    {
        var provider = new ManualEntryProvider();
        Assert.Equal(InstrumentConnectionState.Connected, provider.ConnectionState);
    }

    [Fact]
    public void ManualEntryProvider_HasSingleMeasurementCapability()
    {
        var provider = new ManualEntryProvider();
        Assert.True(provider.Capabilities.HasFlag(InstrumentCapabilities.SingleMeasurement));
    }

    [Fact]
    public async Task ManualEntryProvider_ConnectAsync_IsNoOp()
    {
        var provider = new ManualEntryProvider();
        await provider.ConnectAsync();
        // State is unchanged — always Connected
        Assert.Equal(InstrumentConnectionState.Connected, provider.ConnectionState);
    }

    [Fact]
    public async Task ManualEntryProvider_GetReadingAsync_ReturnsExpectedValue()
    {
        var provider = new ManualEntryProvider { NextReading = 1.23 };
        double result = await provider.GetReadingAsync(null!, CancellationToken.None);
        Assert.Equal(1.23, result);
    }
}
