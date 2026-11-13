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

        var service = await FindServiceAsync(device, ct).ConfigureAwait(false);

        (_rx, _tx) = await FindCharacteristicsAsync(device, service, ct).ConfigureAwait(false);

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

    /// <summary>
    /// Finds the Transparent-UART service. Windows often answers the first discovery after
    /// opening a device with Unreachable / AccessDenied (or an empty list), so it is retried,
    /// then all services are enumerated. The failure text names the status and the services
    /// that were found, because "not found" alone hides whether the link or the device is at fault.
    /// </summary>
    private static async Task<GattDeviceService> FindServiceAsync(BluetoothLEDevice device, CancellationToken ct)
    {
        GattCommunicationStatus lastStatus = GattCommunicationStatus.Unreachable;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var result = await device
                .GetGattServicesForUuidAsync(LeveltronicApi.BleServiceUuid, BluetoothCacheMode.Uncached)
                .AsTask(ct).ConfigureAwait(false);
            lastStatus = result.Status;
            if (result.Status == GattCommunicationStatus.Success && result.Services.Count > 0)
                return result.Services[0];

            await Task.Delay(700, ct).ConfigureAwait(false);
        }

        // Fall back to listing everything the device offers.
        var all = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct).ConfigureAwait(false);
        if (all.Status == GattCommunicationStatus.Success)
        {
            var match = all.Services.FirstOrDefault(x => x.Uuid == LeveltronicApi.BleServiceUuid);
            if (match is not null) return match;

            string found = all.Services.Count == 0
                ? "none"
                : string.Join(", ", all.Services.Select(x => x.Uuid.ToString()));
            throw new InvalidOperationException(
                "Transparent-UART service not found on the device (is it a Leveltronic?). " +
                $"Services offered: {found}.");
        }

        throw new InvalidOperationException(
            $"Could not read the device's services ({all.Status}; last attempt {lastStatus}). " +
            "The module accepts one connection at a time: close the phone app (or switch the phone's " +
            "Bluetooth off) and try again; if the PC never paired with it, pair it in Windows Bluetooth settings.");
    }

    /// <summary>
    /// Finds the receive and transmit characteristics. Right after a (re)connection Windows can report the
    /// service but not yet its characteristics ("characteristic not found" although the device is fine);
    /// so the lookup is retried, the service is looked up afresh once, and a final failure says which
    /// characteristics the device did offer.
    /// </summary>
    private static async Task<(GattCharacteristic Rx, GattCharacteristic Tx)> FindCharacteristicsAsync(
        BluetoothLEDevice device, GattDeviceService service, CancellationToken ct)
    {
        GattCommunicationStatus lastStatus = GattCommunicationStatus.Unreachable;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var all = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(ct).ConfigureAwait(false);
            lastStatus = all.Status;
            if (all.Status == GattCommunicationStatus.Success)
            {
                var rx = all.Characteristics.FirstOrDefault(c => c.Uuid == LeveltronicApi.BleRxCharacteristicUuid);
                var tx = all.Characteristics.FirstOrDefault(c => c.Uuid == LeveltronicApi.BleTxCharacteristicUuid);
                if (rx is not null && tx is not null) return (rx, tx);

                if (attempt == 4)
                {
                    string found = all.Characteristics.Count == 0
                        ? "none"
                        : string.Join(", ", all.Characteristics.Select(c => c.Uuid.ToString()));
                    throw new InvalidOperationException(
                        "The instrument's Transparent-UART service has no usable characteristics " +
                        $"(offered: {found}). Switch the instrument off and on and try again.");
                }
            }

            await Task.Delay(700, ct).ConfigureAwait(false);

            // The service object itself may be stale after a reconnection: look it up again once.
            if (attempt == 1)
                service = await FindServiceAsync(device, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Could not read the instrument's characteristics ({lastStatus}). " +
            "Close other programs or phones connected to it and try again.");
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
