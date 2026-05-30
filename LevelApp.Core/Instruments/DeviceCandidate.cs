namespace LevelApp.Core.Instruments;

/// <summary>
/// A device seen on the bus during a scan but not yet registered as a KnownDevice.
/// </summary>
public record DeviceCandidate(
    string CandidateId,    // transport-level address
    string TransportId,
    string DisplayName,    // e.g. "Wyler BT-Level #4A2F"
    int?   SignalStrength  // dBm for BLE, null for USB
);
