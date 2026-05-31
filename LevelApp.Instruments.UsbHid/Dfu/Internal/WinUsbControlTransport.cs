using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LevelApp.Instruments.UsbHid.Dfu.Internal;

/// <summary>
/// WinUsb.dll P/Invoke implementation of <see cref="IUsbControlTransport"/>.
/// <para>
/// Opens the WinUSB device by its device-interface path (obtained from
/// <see cref="DfuConnectionDetector"/>), initialises the WinUSB handle, and
/// performs synchronous USB control transfers for the DFU protocol.
/// </para>
/// </summary>
internal sealed class WinUsbControlTransport : IUsbControlTransport
{
    private SafeFileHandle? _fileHandle;
    private IntPtr          _winUsbHandle = IntPtr.Zero;

    public WinUsbControlTransport(string devicePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(devicePath);

        _fileHandle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (_fileHandle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Failed to open WinUSB device at '{devicePath}'.");

        if (!NativeMethods.WinUsb_Initialize(_fileHandle, out _winUsbHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "WinUsb_Initialize failed.");
    }

    /// <inheritdoc/>
    public bool ControlTransferOut(
        byte   requestType, byte request, ushort value, ushort index, byte[] data)
    {
        ObjectDisposedException.ThrowIf(_winUsbHandle == IntPtr.Zero, this);

        var setup = new NativeMethods.WinUsbSetupPacket
        {
            RequestType = requestType,
            Request     = request,
            Value       = value,
            Index       = index,
            Length      = (ushort)(data?.Length ?? 0)
        };

        return NativeMethods.WinUsb_ControlTransfer(
            _winUsbHandle,
            setup,
            data ?? Array.Empty<byte>(),
            (uint)(data?.Length ?? 0),
            out _,
            IntPtr.Zero);
    }

    /// <inheritdoc/>
    public bool ControlTransferIn(
        byte requestType, byte request, ushort value, ushort index, byte[] buffer)
    {
        ObjectDisposedException.ThrowIf(_winUsbHandle == IntPtr.Zero, this);

        var setup = new NativeMethods.WinUsbSetupPacket
        {
            RequestType = requestType,
            Request     = request,
            Value       = value,
            Index       = index,
            Length      = (ushort)buffer.Length
        };

        return NativeMethods.WinUsb_ControlTransfer(
            _winUsbHandle,
            setup,
            buffer,
            (uint)buffer.Length,
            out _,
            IntPtr.Zero);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_winUsbHandle != IntPtr.Zero)
        {
            NativeMethods.WinUsb_Free(_winUsbHandle);
            _winUsbHandle = IntPtr.Zero;
        }

        _fileHandle?.Dispose();
        _fileHandle = null;
    }

    // ── P/Invoke declarations ─────────────────────────────────────────────────

    private static class NativeMethods
    {
        internal const uint GENERIC_READ     = 0x80000000;
        internal const uint GENERIC_WRITE    = 0x40000000;
        internal const uint FILE_SHARE_READ  = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint OPEN_EXISTING    = 3;
        internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        // SETUP packet layout matches WINUSB_SETUP_PACKET (winusb.h)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct WinUsbSetupPacket
        {
            public byte   RequestType;
            public byte   Request;
            public ushort Value;
            public ushort Index;
            public ushort Length;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(
            string  lpFileName,
            uint    dwDesiredAccess,
            uint    dwShareMode,
            IntPtr  lpSecurityAttributes,
            uint    dwCreationDisposition,
            uint    dwFlagsAndAttributes,
            IntPtr  hTemplateFile);

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_Initialize(
            SafeFileHandle DeviceHandle,
            out IntPtr     InterfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_ControlTransfer(
            IntPtr             InterfaceHandle,
            WinUsbSetupPacket  SetupPacket,
            [In, Out] byte[]   Buffer,
            uint               BufferLength,
            out uint           LengthTransferred,
            IntPtr             Overlapped);  // NULL → synchronous

        [DllImport("winusb.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WinUsb_Free(IntPtr InterfaceHandle);
    }
}
