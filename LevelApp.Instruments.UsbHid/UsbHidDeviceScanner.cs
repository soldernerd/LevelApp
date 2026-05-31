using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using Windows.Devices.Enumeration;

namespace LevelApp.Instruments.UsbHid;

/// <summary>
/// Enumerates USB HID devices matching a supplied VID/PID filter.
/// <para>
/// Unlike BLE, USB HID devices do not need to be actively scanned — they are
/// already in the OS device list.  The scanner first yields all currently
/// connected matching devices synchronously, then watches for new arrivals via
/// <see cref="DeviceWatcher"/> until the timeout elapses or the caller cancels.
/// </para>
/// </summary>
public sealed class UsbHidDeviceScanner : IDeviceScanner
{
    // Windows HID device-interface class GUID
    // {4D1E55B2-F16F-11CF-88CB-001111000030}
    private const string HidInterfaceGuid = "{4D1E55B2-F16F-11CF-88CB-001111000030}";

    private readonly ushort  _vendorId;
    private readonly ushort[] _productIds;

    /// <inheritdoc/>
    public ITransport Transport { get; } = new UsbHidTransport();

    /// <param name="vendorId">USB Vendor ID to filter by.</param>
    /// <param name="productIds">
    /// One or more Product IDs.  Multiple PIDs handle the case where the same
    /// physical device presents different PIDs in normal vs DFU mode.
    /// </param>
    public UsbHidDeviceScanner(ushort vendorId, ushort[] productIds)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Length == 0)
            throw new ArgumentException("At least one product ID must be supplied.", nameof(productIds));

        _vendorId   = vendorId;
        _productIds = productIds;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DeviceCandidate> ScanAsync(
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        string selector = BuildAqsSelector(_vendorId, _productIds);
        var channel = Channel.CreateUnbounded<DeviceCandidate>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        // ── Phase 1: enumerate already-connected devices ──────────────────────
        var existing = await DeviceInformation.FindAllAsync(selector)
            .AsTask(cts.Token)
            .ConfigureAwait(false);

        foreach (var info in existing)
            channel.Writer.TryWrite(MakeCandidate(info));

        // ── Phase 2: watch for new arrivals ───────────────────────────────────
        var watcher = DeviceInformation.CreateWatcher(selector);

        watcher.Added   += (_, info) => channel.Writer.TryWrite(MakeCandidate(info));
        watcher.Stopped += (_, _)    => channel.Writer.TryComplete();

        watcher.Start();

        // Stop the watcher (→ fires Stopped → completes the channel) when
        // the timeout fires or the caller cancels.
        using var reg = cts.Token.Register(() => TryStop(watcher));

        try
        {
            await foreach (var candidate in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return candidate;
        }
        finally
        {
            // Guard against double-stop: WinRT DeviceWatcher.Stop() throws
            // if the watcher is already in the Stopping or Stopped state.
            TryStop(watcher);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an AQS selector string for the Windows DeviceInformation APIs.
    /// Filters on the HID device-interface class GUID, vendor ID, and one or
    /// more product IDs.
    /// <para>
    /// Property names follow the Windows Shell property system.  The HID
    /// properties (VendorId, ProductId) store 16-bit unsigned integers; AQS
    /// expects them as decimal literals.
    /// </para>
    /// </summary>
    private static string BuildAqsSelector(ushort vendorId, ushort[] productIds)
    {
        // Base filter: must be a HID device interface
        var sb = new System.Text.StringBuilder();
        sb.Append($"System.Devices.InterfaceClassGuid:=\"{HidInterfaceGuid}\"");
        sb.Append($" AND System.DeviceInterface.Hid.VendorId:={vendorId}");

        if (productIds.Length == 1)
        {
            sb.Append($" AND System.DeviceInterface.Hid.ProductId:={productIds[0]}");
        }
        else
        {
            var pidParts = productIds.Select(p => $"System.DeviceInterface.Hid.ProductId:={p}");
            sb.Append($" AND ({string.Join(" OR ", pidParts)})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Calls <see cref="DeviceWatcher.Stop"/> only when the watcher is in a
    /// state that permits it.  WinRT throws if Stop() is called while the
    /// watcher is already stopping or stopped.
    /// </summary>
    private static void TryStop(DeviceWatcher watcher)
    {
        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started
                                or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch
        {
            // Ignore — race between status check and Stop is benign
        }
    }

    private static DeviceCandidate MakeCandidate(DeviceInformation info)
    {
        // DeviceInformation.Id for a HID device is the device-interface path,
        // e.g. \\?\HID#VID_1234&PID_5678#...
        // Use it directly as the CandidateId so concrete providers can call
        // HidDevice.FromIdAsync() with it.
        var name = string.IsNullOrWhiteSpace(info.Name)
            ? info.Id
            : info.Name;

        return new DeviceCandidate(
            CandidateId:   info.Id,
            TransportId:   "usb-hid",
            DisplayName:   name,
            SignalStrength: null);   // N/A for USB
    }
}
