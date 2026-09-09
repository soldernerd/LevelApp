namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>
/// A transport-agnostic byte pipe to a Leveltronic device. Implementations move
/// fully-framed API v2 packets in and out; all protocol logic lives above this
/// interface in <see cref="LeveltronicDeviceClient"/>.
/// </summary>
public interface ILeveltronicLink : IAsyncDisposable
{
    /// <summary>True while the underlying transport is open and usable.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when <see cref="IsConnected"/> changes.</summary>
    event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Raised once per complete, CRC-valid API v2 packet received from the
    /// device (framing bytes included — pass straight to
    /// <see cref="ApiV2Codec.TryParseFrame"/>).
    /// </summary>
    event EventHandler<byte[]>? PacketReceived;

    /// <summary>Open the transport. Throws on failure.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Close the transport. Must not throw.</summary>
    Task DisconnectAsync();

    /// <summary>Send one complete on-wire packet (from <see cref="ApiV2Codec.BuildFrame"/>).</summary>
    Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);
}
