namespace LevelApp.Instruments.UsbHid.Dfu.Internal;

/// <summary>
/// Abstraction over USB control transfers used by <see cref="DfuSession"/>.
/// Separated from the WinUsb.dll P/Invoke implementation so that unit tests
/// can inject a mock without requiring real USB hardware.
/// </summary>
internal interface IUsbControlTransport : IDisposable
{
    /// <summary>
    /// Sends a USB control transfer in the OUT direction (host → device).
    /// </summary>
    /// <param name="requestType">bmRequestType byte of the SETUP packet.</param>
    /// <param name="request">bRequest byte of the SETUP packet.</param>
    /// <param name="value">wValue field of the SETUP packet.</param>
    /// <param name="index">wIndex field of the SETUP packet (interface number for DFU).</param>
    /// <param name="data">
    /// Data payload to send.  May be empty for zero-length transfers (e.g.
    /// DFU_DNLOAD manifestation trigger).
    /// </param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> on OS error.</returns>
    bool ControlTransferOut(byte requestType, byte request, ushort value, ushort index, byte[] data);

    /// <summary>
    /// Sends a USB control transfer in the IN direction (device → host).
    /// </summary>
    /// <param name="requestType">bmRequestType byte of the SETUP packet.</param>
    /// <param name="request">bRequest byte of the SETUP packet.</param>
    /// <param name="value">wValue field of the SETUP packet.</param>
    /// <param name="index">wIndex field of the SETUP packet.</param>
    /// <param name="buffer">
    /// Buffer to receive data; its <see cref="Array.Length"/> determines the
    /// wLength field of the SETUP packet.
    /// </param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> on OS error.</returns>
    bool ControlTransferIn(byte requestType, byte request, ushort value, ushort index, byte[] buffer);
}
