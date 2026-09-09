using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using LevelApp.Instruments.BLE.Internal;
using LevelApp.Instruments.Leveltronic.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace LevelApp.Instruments.Leveltronic.Transport;

/// <summary>
/// <see cref="ILeveltronicLink"/> over the RN4871 Transparent-UART GATT service.
/// <para>
/// Requests are written (write-without-response) to
/// <see cref="LeveltronicApi.BleRxCharacteristicUuid"/>; responses arrive as a
/// raw notification byte stream on <see cref="LeveltronicApi.BleTxCharacteristicUuid"/>
/// and are reassembled into packets by <see cref="PacketReassembler"/>.
/// </para>
/// </summary>
public sealed class BleLeveltronicLink : ILeveltronicLink
{
    private readonly ulong _address;
    private readonly BleConnectionManager _connection = new();
    private readonly PacketReassembler _reassembler = new();

    private GattCharacteristic? _rx;
    private GattCharacteristic? _tx;
    private bool _connected;

    /// <param name="bluetoothAddress">
    /// 48-bit BLE address as a 12-hex-digit string (the
    /// <see cref="Core.Instruments.KnownDevice.TransportAddress"/> written by the
    /// BLE scanner).
    /// </param>
    public BleLeveltronicLink(string bluetoothAddress)
    {
        ArgumentException.ThrowIfNullOrEmpty(bluetoothAddress);
        _address = ulong.Parse(bluetoothAddress, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public bool IsConnected => _connected;

    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<byte[]>? PacketReceived;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;

        await _connection.OpenAsync(_address, ct).ConfigureAwait(false);
        var device = _connection.Device
            ?? throw new InvalidOperationException($"BLE device 0x{_address:X12} could not be opened.");

        var svcResult = await device
            .GetGattServicesForUuidAsync(LeveltronicApi.BleServiceUuid, BluetoothCacheMode.Uncached)
            .AsTask(ct).ConfigureAwait(false);
        var service = svcResult.Status == GattCommunicationStatus.Success
            ? svcResult.Services.FirstOrDefault()
            : null;
        if (service is null)
            throw new InvalidOperationException(
                "Transparent-UART service not found on the device (is it a Leveltronic?).");

        _rx = await GetCharacteristicAsync(service, LeveltronicApi.BleRxCharacteristicUuid, ct)
            .ConfigureAwait(false);
        _tx = await GetCharacteristicAsync(service, LeveltronicApi.BleTxCharacteristicUuid, ct)
            .ConfigureAwait(false);

        _tx.ValueChanged += OnTxValueChanged;

        var cccd = await _tx
            .WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify)
            .AsTask(ct).ConfigureAwait(false);
        if (cccd != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"Could not enable notifications ({cccd}).");

        device.ConnectionStatusChanged += OnConnectionStatusChanged;

        _connected = true;
        _reassembler.Reset();
        ConnectionChanged?.Invoke(this, true);
    }

    public Task DisconnectAsync()
    {
        if (_tx is not null)
            _tx.ValueChanged -= OnTxValueChanged;
        if (_connection.Device is not null)
            _connection.Device.ConnectionStatusChanged -= OnConnectionStatusChanged;

        _rx = null;
        _tx = null;
        _connection.Close();

        if (_connected)
        {
            _connected = false;
            ConnectionChanged?.Invoke(this, false);
        }
        return Task.CompletedTask;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        var rx = _rx ?? throw new InvalidOperationException("BLE link is not connected.");

        var status = await rx
            .WriteValueAsync(frame.ToArray().AsBuffer(), GattWriteOption.WriteWithoutResponse)
            .AsTask(ct).ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"BLE write failed ({status}).");
    }

    private void OnTxValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        byte[] chunk = args.CharacteristicValue.ToArray();
        foreach (var packet in _reassembler.Feed(chunk))
            PacketReceived?.Invoke(this, packet);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected && _connected)
        {
            _connected = false;
            ConnectionChanged?.Invoke(this, false);
        }
    }

    private static async Task<GattCharacteristic> GetCharacteristicAsync(
        GattDeviceService service, Guid uuid, CancellationToken ct)
    {
        var result = await service
            .GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached)
            .AsTask(ct).ConfigureAwait(false);
        var ch = result.Status == GattCommunicationStatus.Success
            ? result.Characteristics.FirstOrDefault()
            : null;
        return ch ?? throw new InvalidOperationException(
            $"Transparent-UART characteristic {uuid} not found.");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _connection.Dispose();
    }
}
