using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LevelApp.Core.Instruments;
using LevelApp.Core.Interfaces;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

// Transport-id constant kept private — callers use BleTransport.TransportId


namespace LevelApp.Instruments.BLE;

/// <summary>
/// Scans for BLE devices that advertise one or more of the supplied service UUIDs.
/// Yields <see cref="DeviceCandidate"/> items as they are discovered.
/// Each Bluetooth address is reported at most once per scan.
/// </summary>
public sealed class BleDeviceScanner : IDeviceScanner, IScanDiagnostics
{
    private readonly Guid[] _serviceUuids;
    private readonly string[] _namePrefixes;

    // What the last scan saw, for the "nothing found" explanation.
    private readonly object _diagGate = new();
    private int _advertisements;
    private readonly HashSet<ulong> _allAddresses = [];
    private readonly SortedSet<string> _namesSeen = new(StringComparer.OrdinalIgnoreCase);
    private string? _stopError;
    private string? _summary;

    /// <inheritdoc/>
    public string? LastScanSummary { get { lock (_diagGate) return _summary; } }

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

        lock (_diagGate)
        {
            _advertisements = 0;
            _allAddresses.Clear();
            _namesSeen.Clear();
            _stopError = null;
            _summary = null;
        }

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

            lock (_diagGate)
            {
                _advertisements++;
                _allAddresses.Add(args.BluetoothAddress);
                if (!string.IsNullOrEmpty(localName) && _namesSeen.Count < 40) _namesSeen.Add(localName);
            }

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
        watcher.Stopped += (_, e) =>
        {
            // An aborted watcher (Bluetooth switched off, no permission) reports why.
            if (e.Error != BluetoothError.Success)
                lock (_diagGate) _stopError = e.Error.ToString();
            channel.Writer.TryComplete();
        };

        watcher.Start();

        // A device that is paired in Windows (Settings > Bluetooth) is often not reported by
        // the advertisement watcher (bonded peripherals use a resolvable private address, or
        // advertise only to their bonded host), yet Windows lists it by name. Report those too.
        _ = ReportPairedAsync(channel.Writer, seen, cts.Token);

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
            BuildSummary();
        }
    }

    private async Task ReportPairedAsync(ChannelWriter<DeviceCandidate> writer, HashSet<ulong> seen, CancellationToken ct)
    {
        if (_namePrefixes.Length == 0) return;

        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var paired = await DeviceInformation.FindAllAsync(selector).AsTask(ct).ConfigureAwait(false);

            foreach (var info in paired)
            {
                if (!_namePrefixes.Any(p => info.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                using var device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(ct).ConfigureAwait(false);
                if (device is null) continue;

                lock (_diagGate)
                {
                    _allAddresses.Add(device.BluetoothAddress);
                    _namesSeen.Add(info.Name + " (paired)");
                }

                bool isNew;
                lock (seen) isNew = seen.Add(device.BluetoothAddress);
                if (!isNew) continue;

                writer.TryWrite(new DeviceCandidate(
                    CandidateId:    device.BluetoothAddress.ToString("X12"),
                    TransportId:    "ble",
                    DisplayName:    info.Name,
                    SignalStrength: null));
            }
        }
        catch
        {
            // Paired-device lookup is a bonus; the advertisement scan still works without it.
        }
    }

    private void BuildSummary()
    {
        lock (_diagGate)
        {
            if (_stopError is not null)
            {
                _summary = $"Bluetooth scan stopped: {_stopError}. Check that Bluetooth is switched on in Windows.";
                return;
            }

            if (_advertisements == 0)
            {
                _summary = "Windows received no Bluetooth advertisements at all. Check that Bluetooth is " +
                           "switched on and that the PC has a Bluetooth LE adapter.";
                return;
            }

            string names = _namesSeen.Count == 0 ? "none with a name" : string.Join(", ", _namesSeen.Take(12));
            string filter = _namePrefixes.Length == 0 ? "" : $" (looking for names starting with {string.Join(" / ", _namePrefixes)})";
            _summary = $"Saw {_allAddresses.Count} Bluetooth device(s){filter}; names: {names}. " +
                       "An instrument that is connected to a phone or another PC does not advertise.";
        }
    }
}
