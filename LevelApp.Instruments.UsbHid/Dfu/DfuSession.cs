using LevelApp.Instruments.UsbHid.Dfu.Internal;

namespace LevelApp.Instruments.UsbHid.Dfu;

// ── WinUSB access approach ────────────────────────────────────────────────────
//
// Two approaches were considered for raw USB access to the STM32 DFU device:
//
// 1. Windows.Devices.Usb.UsbDevice (WinRT)
//    Available via the net8.0-windows10.0.19041.0 TFM.  However, this API
//    requires the 'usbDevice' capability declared in the app package manifest.
//    LevelApp is an UNPACKAGED WinUI 3 / Win32 desktop application
//    (WindowsPackageType=None), so it has no package manifest and cannot
//    declare UWP capabilities.  Attempting to use UsbDevice from an unpackaged
//    process throws an UnauthorizedAccessException.  This approach is therefore
//    NOT viable for LevelApp.
//
// 2. P/Invoke to WinUsb.dll  ← CHOSEN APPROACH
//    WinUsb.dll is a system DLL shipped with all Windows editions from Vista
//    onwards.  It is accessible from any process (no capability manifest
//    required) and provides full control over USB control transfers, which is
//    all that DFU requires.  The necessary surface is small:
//      • WinUsb_Initialize  — obtain an interface handle from a file handle
//      • WinUsb_ControlTransfer — send/receive SETUP + data
//      • WinUsb_Free        — release the interface handle
//    These are wrapped in WinUsbControlTransport (Dfu/Internal/) which
//    implements the testable IUsbControlTransport interface, allowing unit
//    tests to inject a mock without real hardware.
//
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Performs an STM32 USB DFU firmware download (host → device).
/// <para>
/// The STM32 DFU protocol operates over USB control transfers on the DFU
/// class interface (bInterfaceClass=0xFE, bInterfaceSubClass=0x01,
/// bInterfaceProtocol=0x02).  The sequence is:
/// <list type="number">
///   <item>
///     For each firmware page (default 2 048 bytes):
///     send <c>DFU_DNLOAD</c> with the page data, then poll
///     <c>DFU_GETSTATUS</c> until the device reaches
///     <c>dfuDNLOAD-IDLE</c> (state = 5).
///   </item>
///   <item>
///     After the last page, send a zero-length <c>DFU_DNLOAD</c> to trigger
///     manifestation, then poll until <c>dfuIDLE</c> (state = 2) or
///     <c>dfuMANIFEST-WAIT-RESET</c> (state = 8).
///   </item>
///   <item>
///     The device reboots automatically; the caller should then wait for
///     the normal-mode HID device to re-appear.
///   </item>
/// </list>
/// </para>
/// <para>
/// Obtain the <paramref name="dfuDeviceId"/> from
/// <see cref="DfuConnectionDetector.WaitForDfuDeviceAsync"/>.
/// </para>
/// </summary>
public sealed class DfuSession : IDisposable
{
    // ── DFU USB request codes (USB DFU specification v1.1, Table 3) ───────────
    private const byte DfuRequestType_Out = 0x21; // Class | Interface | Host→Device
    private const byte DfuRequestType_In  = 0xA1; // Class | Interface | Device→Host

    private const byte DfuRequest_DnLoad    = 0x01;
    private const byte DfuRequest_GetStatus = 0x03;

    // DFU state machine values (USB DFU specification v1.1, Table 4)
    private const byte DfuState_DnLoadSync     = 3;
    private const byte DfuState_DnBusy         = 4;
    private const byte DfuState_DnLoadIdle     = 5;
    private const byte DfuState_ManifestSync   = 6;
    private const byte DfuState_Manifest       = 7;
    private const byte DfuState_ManifestWaitReset = 8;
    private const byte DfuState_Idle           = 2;

    // ── Fields ────────────────────────────────────────────────────────────────
    private readonly IUsbControlTransport _transport;
    private readonly int _pageSize;
    private bool _disposed;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the DFU-mode device at <paramref name="dfuDeviceId"/> and
    /// prepares for a firmware download.
    /// </summary>
    /// <param name="dfuDeviceId">
    /// Device-interface path returned by
    /// <see cref="DfuConnectionDetector.WaitForDfuDeviceAsync"/>.
    /// </param>
    /// <param name="pageSize">
    /// Size in bytes of each DFU_DNLOAD block.  Must match the device's
    /// internal flash page size (2 048 bytes for most STM32 targets).
    /// Override in concrete instrument providers when the target uses a
    /// different page size.
    /// </param>
    public DfuSession(string dfuDeviceId, int pageSize = 2048)
    {
        ArgumentException.ThrowIfNullOrEmpty(dfuDeviceId);
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be positive.");

        _transport = new WinUsbControlTransport(dfuDeviceId);
        _pageSize  = pageSize;
    }

    /// <summary>
    /// Internal constructor that accepts a mock transport for unit testing.
    /// </summary>
    internal DfuSession(IUsbControlTransport transport, int pageSize = 2048)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be positive.");

        _transport = transport;
        _pageSize  = pageSize;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the firmware image to the device page by page.
    /// <para>
    /// Progress is reported as a value in [0.0, 1.0] where 1.0 indicates
    /// that all pages have been downloaded and the manifestation command
    /// has been accepted.  The device reboots after manifestation — the
    /// caller must then wait for the normal-mode device to re-enumerate.
    /// </para>
    /// </summary>
    /// <param name="firmwareImage">Raw firmware binary.</param>
    /// <param name="progress">Optional progress sink. May be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// DFU protocol error (bad status or unexpected state).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was cancelled.
    /// </exception>
    public async Task FlashAsync(
        byte[]            firmwareImage,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(firmwareImage);

        if (firmwareImage.Length == 0)
        {
            progress?.Report(1.0);
            return;
        }

        int totalBytes = firmwareImage.Length;
        int pageCount  = (totalBytes + _pageSize - 1) / _pageSize;
        int bytesFlashed = 0;

        // ── Download all pages ─────────────────────────────────────────────
        for (int page = 0; page < pageCount; page++)
        {
            ct.ThrowIfCancellationRequested();

            int offset = page * _pageSize;
            int length = Math.Min(_pageSize, totalBytes - offset);

            byte[] pageData = new byte[length];
            Buffer.BlockCopy(firmwareImage, offset, pageData, 0, length);

            // DFU_DNLOAD: send page data to device
            bool ok = _transport.ControlTransferOut(
                requestType: DfuRequestType_Out,
                request:     DfuRequest_DnLoad,
                value:       (ushort)page,   // wValue = block number
                index:       0,              // wIndex = DFU interface number
                data:        pageData);

            if (!ok)
                throw new InvalidOperationException(
                    $"DFU_DNLOAD failed on block {page} (offset 0x{offset:X}).");

            // Poll DFU_GETSTATUS until device returns to dfuDNLOAD-IDLE
            await PollUntilDownloadIdleAsync(page, ct).ConfigureAwait(false);

            bytesFlashed += length;
            progress?.Report((double)bytesFlashed / totalBytes);
        }

        ct.ThrowIfCancellationRequested();

        // ── Manifestation: zero-length DFU_DNLOAD triggers reboot ─────────
        bool manifestOk = _transport.ControlTransferOut(
            requestType: DfuRequestType_Out,
            request:     DfuRequest_DnLoad,
            value:       (ushort)pageCount,  // next block number
            index:       0,
            data:        Array.Empty<byte>());

        if (!manifestOk)
            throw new InvalidOperationException("DFU manifestation command failed.");

        await PollUntilManifestCompleteAsync(ct).ConfigureAwait(false);

        progress?.Report(1.0);
    }

    // ── Polling helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Polls DFU_GETSTATUS until the device reaches dfuDNLOAD-IDLE (state 5),
    /// sleeping for the poll timeout the device requests when it is busy.
    /// </summary>
    private async Task PollUntilDownloadIdleAsync(int blockNum, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var (bStatus, pollMs, bState) = GetStatus();

            if (bStatus != 0)
                throw new InvalidOperationException(
                    $"DFU error after block {blockNum}: bStatus=0x{bStatus:X2}, bState=0x{bState:X2}.");

            switch (bState)
            {
                case DfuState_DnLoadIdle:  // 5 — device ready for next block
                    return;

                case DfuState_DnBusy:      // 4 — device is programming; wait
                case DfuState_DnLoadSync:  // 3 — device needs another GETSTATUS
                    if (pollMs > 0)
                        await Task.Delay(pollMs, ct).ConfigureAwait(false);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unexpected DFU state after DFU_DNLOAD (block {blockNum}): 0x{bState:X2}.");
            }
        }
    }

    /// <summary>
    /// Polls DFU_GETSTATUS until the device signals that manifestation is
    /// complete.  Accepts normal completion on dfuIDLE (manifest-tolerant
    /// devices) or dfuMANIFEST-WAIT-RESET (device about to reboot).
    /// Silently returns if the device disconnects — this is expected for
    /// devices that execute a hard reset during manifestation.
    /// </summary>
    private async Task PollUntilManifestCompleteAsync(CancellationToken ct)
    {
        for (int i = 0; i < 60; i++)  // safety cap — max ~3 s at 50 ms/poll
        {
            ct.ThrowIfCancellationRequested();

            bool ok;
            byte bStatus, bState;
            int  pollMs;

            try { (bStatus, pollMs, bState) = GetStatus(); ok = true; }
            catch { ok = false; bStatus = 0; pollMs = 0; bState = 0; }

            if (!ok)
            {
                // Device disconnected during manifestation — expected; we're done.
                return;
            }

            switch (bState)
            {
                case DfuState_Idle:              // 2  — manifest-tolerant, done
                case DfuState_ManifestWaitReset: // 8  — device will reboot
                    return;

                case DfuState_ManifestSync:      // 6
                case DfuState_Manifest:          // 7  — still manifesting
                    await Task.Delay(Math.Max(50, pollMs), ct).ConfigureAwait(false);
                    break;

                default:
                    if (bStatus != 0)
                        throw new InvalidOperationException(
                            $"DFU manifestation error: bStatus=0x{bStatus:X2}, bState=0x{bState:X2}.");
                    await Task.Delay(50, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>
    /// Sends DFU_GETSTATUS and returns (bStatus, pollTimeoutMs, bState).
    /// The 6-byte response layout is defined in USB DFU specification v1.1,
    /// section 6.1.2.
    /// </summary>
    private (byte bStatus, int pollMs, byte bState) GetStatus()
    {
        byte[] buf = new byte[6];
        bool ok = _transport.ControlTransferIn(
            requestType: DfuRequestType_In,
            request:     DfuRequest_GetStatus,
            value:       0,
            index:       0,
            buffer:      buf);

        if (!ok)
            throw new InvalidOperationException("DFU_GETSTATUS transfer failed.");

        byte bStatus = buf[0];
        int  pollMs  = buf[1] | (buf[2] << 8) | (buf[3] << 16); // 24-bit little-endian
        byte bState  = buf[4];
        return (bStatus, pollMs, bState);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transport.Dispose();
    }
}
