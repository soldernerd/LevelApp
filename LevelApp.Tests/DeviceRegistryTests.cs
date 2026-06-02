using LevelApp.Core.Instruments;

namespace LevelApp.Tests;

public class DeviceRegistryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DeviceRegistryTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_MissingFile_StartsEmpty_NoError()
    {
        var path = Path.Combine(_tempDir, "devices.json");
        var registry = new DeviceRegistry(path);

        Assert.Empty(registry.GetAllKnownDevices());
        Assert.Null(registry.LoadError);
    }

    [Fact]
    public void Load_CorruptJson_StartsEmptyAndSetsLoadError()
    {
        var path = Path.Combine(_tempDir, "devices.json");
        File.WriteAllText(path, "{ this is not valid json !!! }");

        var registry = new DeviceRegistry(path);

        Assert.Empty(registry.GetAllKnownDevices());
        Assert.NotNull(registry.LoadError);
    }

    [Fact]
    public void Load_CorruptJson_BacksUpCorruptFile()
    {
        var path       = Path.Combine(_tempDir, "devices.json");
        var backupPath = path + ".corrupt";
        File.WriteAllText(path, "{ this is not valid json !!! }");

        _ = new DeviceRegistry(path);

        Assert.True(File.Exists(backupPath));
    }
}
