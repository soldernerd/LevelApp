using Windows.Devices.Enumeration;

namespace LevelApp.Instruments.UsbHid.Dfu;

/// <summary>
/// Watches for a USB device with a specific VID/PID to appear after a
/// DFU_DETACH command has been sent, triggering the device to re-enumerate
/// in DFU mode (typically with a different Product ID).
/// </summary>
/// <remarks>
/// <para>
/// The detector uses <see cref="DeviceInformation.CreateWatcher"/> with a
/// WinUSB device-interface class selector
/// (<c>{DEE824EF-729B-4A0E-9C14-B7117D33A817}</c>).  It also performs an
/// initial <see cref="DeviceInformation.FindAllAsync"/> to handle the (rare)
/// case where the DFU device re-enumerated before this method was called.
/// </para>
/// <para>
/// VID/PID matching uses the device-interface ID string, which on Windows
/// always embeds the VID and PID in the form <c>VID_xxxx&amp;PID_xxxx</c>
/// (case-insensitive).  This approach is independent of WinUSB AQS property
/// support and therefore works reliably across Windows 10 builds.
/// </para>
/// </remarks>
public sealed class DfuConnectionDetector
{
    // WinUSB device-interface class GUID — registered by the WinUSB driver
    // for any device that uses WinUSB (including STM32 DFU devices after
    // the appropriate driver is installed via Zadig or a custom INF).
    private const string WinUsbInterfaceGuid = "{DEE824EF-729B-4A0E-9C14-B7117D33A817}";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits for a WinUSB device with the given VID/DFU PID to appear.
    /// Should be called immediately after sending the DFU_DETACH command
    /// to give the device time to re-enumerate.
    /// </summary>
    /// <param name="vendorId">USB Vendor ID (same as the normal-mode device).</param>
    /// <param name="dfuProductId">
    /// The Product ID the device advertises when in DFU mode.  This is often
    /// different from the normal-mode PID.
    /// </param>
    /// <param name="ct">External cancellation token.</param>
    /// <returns>
    /// The device-interface path (suitable as the <c>dfuDeviceId</c> parameter
    /// of <see cref="DfuSession"/>).
    /// </returns>
    /// <exception cref="TimeoutException">
    /// The device did not appear within 10 seconds.
    /// </exception>
    public async Task<string> WaitForDfuDeviceAsync(
        ushort            vendorId,
        ushort            dfuProductId,
        CancellationToken ct = default)
    {
        // Combine external cancellation with a 10-second timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeout);

        // Windows embeds VID/PID in device-interface IDs as VID_xxxx&PID_xxxx
        string fragment = $"VID_{vendorId:X4}&PID_{dfuProductId:X4}";
        string selector = $"System.Devices.InterfaceClassGuid:=\"{WinUsbInterfaceGuid}\"";

        // ── Phase 1: check already-present devices ────────────────────────────
        try
        {
            var existing = await DeviceInformation
                .FindAllAsync(selector)
                .AsTask(cts.Token)
                .ConfigureAwait(false);

            var match = existing.FirstOrDefault(d =>
                d.Id.Contains(fragment, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match.Id;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Timeout during FindAllAsync — fall through to the timeout exception below
            throw new TimeoutException(
                $"DFU device (VID={vendorId:X4}h, PID={dfuProductId:X4}h) did not appear within {DefaultTimeout.TotalSeconds:0} s.");
        }

        // ── Phase 2: watch for the device to arrive ───────────────────────────
        var tcs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var watcher = DeviceInformation.CreateWatcher(selector);

        watcher.Added += (_, info) =>
        {
            if (info.Id.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                tcs.TrySetResult(info.Id);
        };

        watcher.Start();

        using var reg = cts.Token.Register(() =>
        {
            if (ct.IsCancellationRequested)
                tcs.TrySetCanceled(ct);
            else
                tcs.TrySetException(new TimeoutException(
                    $"DFU device (VID={vendorId:X4}h, PID={dfuProductId:X4}h) did not appear within {DefaultTimeout.TotalSeconds:0} s."));
        });

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            watcher.Stop();
        }
    }
}
