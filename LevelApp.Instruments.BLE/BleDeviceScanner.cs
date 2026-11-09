using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using Windows.Devices.Bluetooth.Advertisement;

// Transport-id constant kept private — callers use BleTransport.TransportId


namespace LevelApp.Instruments.BLE;

/// <summary>
/// Scans for BLE devices that advertise one or more of the supplied service UUIDs.
/// Yields <see cref="DeviceCandidate"/> items as they are discovered.
/// Each Bluetooth address is reported at most once per scan.
/// </summary>
public sealed class BleDeviceScanner : IDeviceScanner
{
    private readonly Guid[] _serviceUuids;
    private readonly string[] _namePrefixes;

    /// <param name="serviceUuids">
    /// The service UUIDs to filter on. Pass an empty array to receive all BLE
    /// advertisements (useful during development / testing).
    /// </param>
    /// <param name="namePrefixes">
    /// Optional advertised-local-name prefixes (case-insensitive). When given, only
    /// advertisements whose name starts with one of them are reported — for
    /// peripherals (e.g. an RN4871 module) that do not advertise their service UUID.
    /// </param>
    public BleDeviceScanner(Guid[] serviceUuids, string[]? namePrefixes = null)
    {
        _serviceUuids = serviceUuids ?? throw new ArgumentNullException(nameof(serviceUuids));
        _namePrefixes = namePrefixes ?? [];
    }

    /// <inheritdoc/>
    public ITransport Transport { get; } = new BleTransport();

    /// <inheritdoc/>
    public async IAsyncEnumerable<DeviceCandidate> ScanAsync(
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Combine the caller's token with a timeout token
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var channel = Channel.CreateUnbounded<DeviceCandidate>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });

        var seen = new HashSet<ulong>();
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        foreach (var uuid in _serviceUuids)
            watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(uuid);

        watcher.Received += (_, args) =>
        {
            if (cts.IsCancellationRequested) return;

            var localName = args.Advertisement.LocalName;

            // The name may only arrive in the scan response, so an advertisement
            // without a matching name is skipped without marking the address seen.
            if (_namePrefixes.Length > 0 &&
                !_namePrefixes.Any(p => localName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return;

            bool isNew;
            lock (seen) isNew = seen.Add(args.BluetoothAddress);
            if (!isNew) return;

            var candidate = new DeviceCandidate(
                CandidateId:    args.BluetoothAddress.ToString("X12"),
                TransportId:    "ble",
                DisplayName:    localName is { Length: > 0 } ? localName
                                                              : $"BLE:{args.BluetoothAddress:X12}",
                SignalStrength: (int)args.RawSignalStrengthInDBm);

            channel.Writer.TryWrite(candidate);
        };

        // Complete the channel when the watcher stops so ReadAllAsync returns
        watcher.Stopped += (_, _) => channel.Writer.TryComplete();

        watcher.Start();

        // Stop the watcher (and therefore complete the channel) on timeout or cancellation
        using var reg = cts.Token.Register(() => watcher.Stop());

        try
        {
            // ReadAllAsync with original ct: clean end on timeout, exception on external cancel
            await foreach (var candidate in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return candidate;
        }
        finally
        {
            // Guard against the case where ct was not yet cancelled (e.g. loop exited early)
            watcher.Stop();
        }
    }
}
