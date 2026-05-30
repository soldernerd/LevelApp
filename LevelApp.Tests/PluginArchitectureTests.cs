using LevelApp.Core.Instruments;
using LevelApp.Instruments.Manual;

namespace LevelApp.Tests;

public sealed class PluginArchitectureTests
{
    // ── ManualEntryPlugin ─────────────────────────────────────────────────────

    [Fact]
    public void ManualEntryPlugin_HasCorrectPluginId()
    {
        var plugin = new ManualEntryPlugin();
        Assert.Equal("manual-entry", plugin.PluginId);
    }

    [Fact]
    public void ManualEntryPlugin_ReturnsNullForOptionalCapabilities()
    {
        var plugin = new ManualEntryPlugin();
        var device = ManualEntryProvider.BuiltInDevice;

        Assert.Null(plugin.CreateCalibrationWorkflow(device));
        Assert.Null(plugin.CreateFirmwareUpdater(device));
        Assert.Null(plugin.CreateDeviceManagementView(null!));
    }

    [Fact]
    public void ManualEntryPlugin_CreateProvider_ReturnsConnectedProvider()
    {
        var plugin   = new ManualEntryPlugin();
        var provider = plugin.CreateProvider(ManualEntryProvider.BuiltInDevice);

        Assert.Equal(InstrumentConnectionState.Connected, provider.ConnectionState);
    }

    // ── DeviceRegistry ────────────────────────────────────────────────────────

    [Fact]
    public void DeviceRegistry_RegisterAndRetrieve()
    {
        var registry = new DeviceRegistry();
        var device   = new KnownDevice("dev-1", "test-plugin", "manual", "Test Device", string.Empty);

        registry.RegisterDevice(device);

        var found = registry.GetKnownDevices("test-plugin");
        Assert.Single(found);
        Assert.Equal("dev-1", found[0].DeviceId);
    }

    [Fact]
    public void DeviceRegistry_ForgetDevice_RemovesFromList()
    {
        var registry = new DeviceRegistry();
        var device   = new KnownDevice("dev-2", "test-plugin", "manual", "Test Device", string.Empty);

        registry.RegisterDevice(device);
        registry.ForgetDevice("dev-2");

        Assert.Empty(registry.GetKnownDevices("test-plugin"));
    }

    [Fact]
    public void DeviceRegistry_PreferredDevice_RoundTrips()
    {
        var registry = new DeviceRegistry();
        var device   = new KnownDevice("dev-3", "test-plugin", "manual", "Test Device", string.Empty);

        registry.RegisterDevice(device);
        registry.SetPreferredDevice("test-plugin", "dev-3");

        var preferred = registry.GetPreferredDevice("test-plugin");
        Assert.NotNull(preferred);
        Assert.Equal("dev-3", preferred.DeviceId);
    }
}
