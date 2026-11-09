using System.Runtime.InteropServices.WindowsRuntime;
using LevelApp.Instruments.Leveltronic.Protocol;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;

namespace LevelApp.Instruments.Leveltronic.Transport;

/// <summary>
/// <see cref="ILeveltronicLink"/> over USB Custom HID.
/// <para>
/// The HID report is a fixed 64 bytes. An API packet always starts at the
/// beginning of a report and occupies <c>ceil((6 + LEN) / 64)</c> consecutive
/// reports, the last zero-padded (API v3 spec §2.2); both directions use this.
/// The device uses report id 0, sent as the leading byte of every report per
/// the Windows HID convention.
/// </para>
/// </summary>
public sealed class UsbHidLeveltronicLink : ILeveltronicLink
{
    private readonly string _deviceId;
    private readonly HidReportReassembler _reassembler = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
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

        _reassembler.Reset();
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

        // Reports of one packet must go out back to back.
        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var chunk in HidReportReassembler.Split(frame.Span))
            {
                HidOutputReport report = device.CreateOutputReport();

                // report.Data length == MaxOutputReportLength and includes the
                // leading report-id byte (index 0, left 0).
                byte[] buffer = new byte[report.Data.Length];
                if (chunk.Length + 1 > buffer.Length)
                    throw new InvalidOperationException(
                        $"HID output report is {buffer.Length - 1} B, API v3 needs {chunk.Length} B.");
                chunk.CopyTo(buffer, 1);
                report.Data = buffer.AsBuffer();

                await device.SendOutputReportAsync(report).AsTask(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void OnInputReportReceived(HidDevice sender, HidInputReportReceivedEventArgs args)
    {
        byte[] raw = args.Report.Data.ToArray();
        if (raw.Length <= 1) return;

        // Strip the leading report-id byte, then reassemble across reports.
        var packet = _reassembler.Feed(raw.AsSpan(1));
        if (packet is not null)
            PacketReceived?.Invoke(this, packet);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendGate.Dispose();
    }
}
