using System.Collections.Concurrent;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Tests.Leveltronic;

/// <summary>
/// In-memory <see cref="ILeveltronicLink"/> for client/provider tests. Each sent
/// frame is parsed for its opcode; a registered responder (if any) produces the
/// reply, which is raised back on <see cref="PacketReceived"/>.
/// </summary>
internal sealed class FakeLeveltronicLink : ILeveltronicLink
{
    private readonly ConcurrentDictionary<ushort, Func<byte[], byte[]?>> _responders = new();

    public bool IsConnected { get; private set; }
    public int SentCount { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<byte[]>? PacketReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        ConnectionChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (IsConnected)
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, false);
        }
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        SentCount++;
        byte[] sent = frame.ToArray();
        ApiV2Codec.TryParseFrame(sent, out ushort op, out _, out _);

        if (_responders.TryGetValue(op, out var responder))
        {
            byte[]? reply = responder(sent);
            if (reply is not null)
                PacketReceived?.Invoke(this, reply);
        }
        return Task.CompletedTask;
    }

    /// <summary>Reply to <paramref name="opcode"/> with an OK status + <paramref name="data"/>.</summary>
    public FakeLeveltronicLink RespondOk(ushort opcode, params byte[] data)
    {
        byte[] payload = [(byte)Api2Status.Ok, .. data];
        _responders[opcode] = _ => ApiV2Codec.BuildFrame(opcode, payload);
        return this;
    }

    /// <summary>Reply to <paramref name="opcode"/> with a non-OK status byte only.</summary>
    public FakeLeveltronicLink RespondStatus(ushort opcode, Api2Status status)
    {
        _responders[opcode] = _ => ApiV2Codec.BuildFrame(opcode, [(byte)status]);
        return this;
    }

    /// <summary>Capture the payload the host sent for <paramref name="opcode"/>, then ACK.</summary>
    public FakeLeveltronicLink CaptureThenAck(ushort opcode, Action<byte[]> capturePayload)
    {
        _responders[opcode] = sent =>
        {
            ApiV2Codec.TryParseFrame(sent, out _, out _, out _);
            int payLen = sent[2] | (sent[3] << 8);
            capturePayload(sent[4..(4 + payLen)]);
            return ApiV2Codec.BuildFrame(opcode, [(byte)Api2Status.Ok]);
        };
        return this;
    }

    /// <summary>Register no responder — the request will time out.</summary>
    public FakeLeveltronicLink Silent(ushort opcode)
    {
        _responders[opcode] = _ => null;
        return this;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
