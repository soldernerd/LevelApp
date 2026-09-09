using System.Runtime.InteropServices.WindowsRuntime;
using LevelApp.Instruments.Leveltronic.Protocol;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;

namespace LevelApp.Instruments.Leveltronic.Transport;

/// <summary>
/// <see cref="ILeveltronicLink"/> over USB Custom HID.
/// <para>
/// One HID OUT report carries one request packet; one HID IN report carries one
/// response packet (zero-padded to the report length — <see cref="ApiV2Codec"/>
/// reads <c>LEN</c> and ignores the pad). The device uses report id 0, sent as
/// the leading byte of every report per the Windows HID convention.
/// </para>
/// </summary>
public sealed class UsbHidLeveltronicLink : ILeveltronicLink
{
    private readonly string _deviceId;
    private HidDevice? _device;

    public UsbHidLeveltronicLink(string deviceInterfaceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceInterfaceId);
        _deviceId = deviceInterfaceId;
    }

    public bool IsConnected => _device is not null;

    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<byte[]>? PacketReceived;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_device is not null) return;

        ct.ThrowIfCancellationRequested();

        var device = await HidDevice
            .FromIdAsync(_deviceId, FileAccessMode.ReadWrite)
            .AsTask(ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"USB HID device '{_deviceId}' not found or in use by another process.");

        device.InputReportReceived += OnInputReportReceived;
        _device = device;
        ConnectionChanged?.Invoke(this, true);
    }

    public Task DisconnectAsync()
    {
        if (_device is not null)
        {
            _device.InputReportReceived -= OnInputReportReceived;
            _device.Dispose();
            _device = null;
            ConnectionChanged?.Invoke(this, false);
        }
        return Task.CompletedTask;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        var device = _device
            ?? throw new InvalidOperationException("USB HID link is not connected.");

        HidOutputReport report = device.CreateOutputReport();

        // report.Data length == MaxOutputReportLength and includes the leading
        // report-id byte (index 0). Leave it 0; copy the packet after it.
        byte[] buffer = new byte[report.Data.Length];
        if (frame.Length + 1 > buffer.Length)
            throw new ArgumentException(
                $"Frame ({frame.Length} B) does not fit one HID report ({buffer.Length - 1} B).",
                nameof(frame));
        frame.Span.CopyTo(buffer.AsSpan(1));
        report.Data = buffer.AsBuffer();

        await device.SendOutputReportAsync(report).AsTask(ct).ConfigureAwait(false);
    }

    private void OnInputReportReceived(HidDevice sender, HidInputReportReceivedEventArgs args)
    {
        byte[] raw = args.Report.Data.ToArray();
        if (raw.Length <= 1) return;

        // Strip the leading report-id byte, then parse the fixed-size report.
        var packet = raw.AsSpan(1);
        if (ApiV2Codec.TryParseFrame(packet, out _, out _, out _))
        {
            // Hand up the exact framed length so the client parses cleanly.
            int payLen = packet[2] | (packet[3] << 8);
            int total = ApiV2Codec.HeaderBytes + payLen + ApiV2Codec.CrcBytes;
            PacketReceived?.Invoke(this, packet[..total].ToArray());
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
