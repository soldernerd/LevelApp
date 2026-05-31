using System.Diagnostics;
using LevelApp.Core.Instruments;
using LevelApp.Instruments.UsbHid;

namespace LevelApp.Tests.UsbHid;

/// <summary>
/// Tests that <see cref="UsbHidDeviceScanner"/> respects timeout and
/// cancellation without requiring real USB hardware.  A VID that has an
/// extremely low probability of matching any connected device (0xFFFE) is
/// used so the device list stays empty; only timing behaviour is verified.
/// </summary>
public sealed class UsbHidDeviceScannerTests
{
    // VID/PID unlikely to match any real device on the test machine
    private static readonly ushort   TestVid  = 0xFFFE;
    private static readonly ushort[] TestPids = { 0xFFFE };

    [Fact]
    public async Task Scanner_RespectsTimeout()
    {
        var scanner = new UsbHidDeviceScanner(TestVid, TestPids);
        var sw      = Stopwatch.StartNew();

        var results = new List<DeviceCandidate>();
        await foreach (var c in scanner.ScanAsync(TimeSpan.FromMilliseconds(300)))
            results.Add(c);

        sw.Stop();

        // The scan should have completed close to the timeout (≤ 3 × timeout)
        // and definitely not hang indefinitely.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"ScanAsync ran for {sw.Elapsed.TotalSeconds:F1} s — expected ≤ 3 s.");
    }

    [Fact]
    public async Task Scanner_RespectsCancel()
    {
        var scanner = new UsbHidDeviceScanner(TestVid, TestPids);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // With a 30-second timeout but a token cancelled at 100 ms, the
        // scanner must stop and propagate OperationCanceledException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in scanner.ScanAsync(TimeSpan.FromSeconds(30), cts.Token))
                { }
        });
    }
}
