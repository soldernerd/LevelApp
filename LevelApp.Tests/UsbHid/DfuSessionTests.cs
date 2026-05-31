using LevelApp.Instruments.UsbHid.Dfu;
using LevelApp.Instruments.UsbHid.Dfu.Internal;

namespace LevelApp.Tests.UsbHid;

/// <summary>
/// Unit tests for <see cref="DfuSession"/> using an in-process mock transport.
/// No real USB hardware is required.
/// </summary>
public sealed class DfuSessionTests
{
    // ── Mock transport ────────────────────────────────────────────────────────

    /// <summary>
    /// Simulates a well-behaved STM32 DFU device:
    /// <list type="bullet">
    ///   <item>DFU_DNLOAD always succeeds.</item>
    ///   <item>DFU_GETSTATUS returns dfuDNLOAD-IDLE (state=5) for data pages
    ///         and dfuIDLE (state=2) after the manifestation trigger.</item>
    ///   <item>All other requests return success with a zeroed buffer.</item>
    /// </list>
    /// </summary>
    private sealed class MockUsbTransport : IUsbControlTransport
    {
        private bool _manifestTriggered;

        /// <summary>Number of DFU_DNLOAD requests received (including the zero-length manifestation one).</summary>
        public int DnloadCount   { get; private set; }
        /// <summary>Total bytes received in DFU_DNLOAD data transfers (excluding the zero-length one).</summary>
        public int BytesReceived { get; private set; }

        public bool ControlTransferOut(
            byte requestType, byte request, ushort value, ushort index, byte[] data)
        {
            if (request == 0x01) // DFU_DNLOAD
            {
                DnloadCount++;
                if (data.Length == 0)
                    _manifestTriggered = true;
                else
                    BytesReceived += data.Length;
            }
            return true;
        }

        public bool ControlTransferIn(
            byte requestType, byte request, ushort value, ushort index, byte[] buffer)
        {
            if (request == 0x03 && buffer.Length >= 6) // DFU_GETSTATUS
            {
                buffer[0] = 0; // bStatus = OK
                buffer[1] = 0; buffer[2] = 0; buffer[3] = 0; // bwPollTimeout = 0 ms
                buffer[4] = _manifestTriggered ? (byte)2 : (byte)5; // dfuIDLE or dfuDNLOAD-IDLE
                buffer[5] = 0; // iString
            }
            return true;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Captures IProgress&lt;double&gt; reports synchronously without a
    /// SynchronizationContext, which is the xUnit execution environment.
    /// </summary>
    private sealed class CapturingProgress : IProgress<double>
    {
        private readonly List<double> _values = new();
        public IReadOnlyList<double> Values => _values;
        public double Last => _values.Count > 0 ? _values[^1] : double.NaN;
        public void Report(double value) => _values.Add(value);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FlashAsync_ProgressReachesOneOnCompletion()
    {
        var transport = new MockUsbTransport();
        using var session = new DfuSession(transport, pageSize: 2048);

        var progress = new CapturingProgress();

        // 5 KiB firmware → 3 pages (2048 + 2048 + 1024)
        byte[] firmware = new byte[5 * 1024];
        new Random(42).NextBytes(firmware);

        await session.FlashAsync(firmware, progress, CancellationToken.None);

        Assert.Equal(1.0, progress.Last);
    }

    [Fact]
    public async Task FlashAsync_ReportsProgressMonotonically()
    {
        var transport = new MockUsbTransport();
        using var session = new DfuSession(transport, pageSize: 512);

        var progress = new CapturingProgress();
        byte[] firmware = new byte[4 * 512]; // exactly 4 pages

        await session.FlashAsync(firmware, progress, CancellationToken.None);

        // Progress values should be strictly non-decreasing
        for (int i = 1; i < progress.Values.Count; i++)
            Assert.True(progress.Values[i] >= progress.Values[i - 1],
                $"Progress decreased from {progress.Values[i - 1]} to {progress.Values[i]}.");

        Assert.Equal(1.0, progress.Last);
    }

    [Fact]
    public async Task FlashAsync_SendsCorrectNumberOfDnloadRequests()
    {
        var transport = new MockUsbTransport();
        using var session = new DfuSession(transport, pageSize: 1024);

        // 3 KiB → 3 data pages + 1 manifestation DNLOAD = 4 total
        byte[] firmware = new byte[3 * 1024];
        await session.FlashAsync(firmware, null, CancellationToken.None);

        Assert.Equal(4, transport.DnloadCount);
        Assert.Equal(3 * 1024, transport.BytesReceived);
    }

    [Fact]
    public async Task FlashAsync_EmptyFirmware_ReportsCompletionImmediately()
    {
        var transport = new MockUsbTransport();
        using var session = new DfuSession(transport, pageSize: 2048);

        var progress = new CapturingProgress();
        await session.FlashAsync(Array.Empty<byte>(), progress, CancellationToken.None);

        Assert.Equal(1.0, progress.Last);
        Assert.Equal(0, transport.DnloadCount); // no USB traffic
    }

    [Fact]
    public async Task FlashAsync_ThrowsOnCancel()
    {
        // Pre-cancel the token so the very first ct.ThrowIfCancellationRequested()
        // at the start of the page loop fires immediately — deterministic.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var transport = new MockUsbTransport();
        using var session = new DfuSession(transport, pageSize: 2048);
        byte[] firmware = new byte[4096];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.FlashAsync(firmware, null, cts.Token));
    }

    [Fact]
    public async Task FlashAsync_ThrowsAfterDispose()
    {
        var transport = new MockUsbTransport();
        var session   = new DfuSession(transport, pageSize: 2048);
        session.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.FlashAsync(new byte[1024], null, CancellationToken.None));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativePageSize()
    {
        var transport = new MockUsbTransport();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DfuSession(transport, pageSize: 0));
    }
}
