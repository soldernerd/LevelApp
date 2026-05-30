using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace LevelApp.Instruments.BLE.Internal;

/// <summary>
/// Manages the lifetime of a <see cref="BluetoothLEDevice"/> and its associated
/// <see cref="GattSession"/>.  Setting <c>MaintainConnection = true</c> on the
/// session tells the Windows BLE stack to keep the link alive and surface
/// ConnectionStatus events when the peripheral goes away.
/// </summary>
internal sealed class BleConnectionManager : IDisposable
{
    private BluetoothLEDevice? _device;
    private GattSession?       _session;

    /// <summary>
    /// The managed device, available after a successful <see cref="OpenAsync"/> call.
    /// </summary>
    public BluetoothLEDevice? Device => _device;

    /// <summary>
    /// Opens a connection to the peripheral at <paramref name="address"/> and
    /// requests that the Windows BLE stack maintain the GATT session.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the OS cannot resolve the address to a device object.
    /// </exception>
    public async Task OpenAsync(ulong address, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address)
            .AsTask(ct)
            .ConfigureAwait(false);

        if (device is null)
            throw new InvalidOperationException(
                $"BLE device 0x{address:X12} could not be found.");

        ct.ThrowIfCancellationRequested();

        var sessionId = BluetoothDeviceId.FromId(device.DeviceId);
        var session   = await GattSession.FromDeviceIdAsync(sessionId)
            .AsTask(ct)
            .ConfigureAwait(false);

        session.MaintainConnection = true;

        // Replace previous handles if re-opening
        Close();
        _device  = device;
        _session = session;
    }

    /// <summary>Releases both the session and the device handle.</summary>
    public void Close()
    {
        _session?.Dispose();
        _device?.Dispose();
        _session = null;
        _device  = null;
    }

    /// <inheritdoc/>
    public void Dispose() => Close();
}
