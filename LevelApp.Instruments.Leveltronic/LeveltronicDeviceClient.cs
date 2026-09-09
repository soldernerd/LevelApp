using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using LevelApp.Instruments.Leveltronic.Protocol;

namespace LevelApp.Instruments.Leveltronic;

/// <summary>
/// Typed request/response access to a Leveltronic device over an
/// <see cref="ILeveltronicLink"/>.
/// <para>
/// Every API v2 response echoes its request opcode and carries a status byte;
/// this class correlates the two, enforces a per-request timeout, and decodes
/// the well-known resources. It is transport-agnostic — the same instance works
/// over USB HID or BLE.
/// </para>
/// </summary>
public sealed class LeveltronicDeviceClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly ILeveltronicLink _link;
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<(Api2Status? status, byte[] data)>> _pending = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public LeveltronicDeviceClient(ILeveltronicLink link)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _link.PacketReceived += OnPacketReceived;
    }

    /// <summary>The underlying link. Exposed so a provider can observe its state.</summary>
    public ILeveltronicLink Link => _link;

    // ── Well-known resources ─────────────────────────────────────────────────

    /// <summary>Read firmware version, product and serial (<c>GET 0x0/0x00</c>).</summary>
    public async Task<DeviceIdentity> GetIdentityAsync(CancellationToken ct = default)
    {
        var data = await RequestOkAsync(LeveltronicApi.GetIdentity, ct: ct).ConfigureAwait(false);
        if (data.Length < 27)
            throw new LeveltronicProtocolException($"Identity response too short ({data.Length} B).");

        string product = ReadAsciiZ(data.AsSpan(3, 16));
        string serial = ReadAsciiZ(data.AsSpan(19, 8));
        return new DeviceIdentity(data[0], data[1], data[2], product, serial);
    }

    /// <summary>Read battery / connection state (<c>GET 0x0/0x01</c>).</summary>
    public async Task<DeviceState> GetDeviceStateAsync(CancellationToken ct = default)
    {
        var data = await RequestOkAsync(LeveltronicApi.GetDeviceState, ct: ct).ConfigureAwait(false);
        if (data.Length < 7)
            throw new LeveltronicProtocolException($"Device-state response too short ({data.Length} B).");

        return new DeviceState(
            Battery:            (BatteryState)data[0],
            BatterySocPercent:  data[1],
            BatteryMilliVolts:  BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2, 2)),
            UsbConnected:       data[4] != 0,
            BleConnected:       data[5] != 0,
            CalibrationValid:   data[6] != 0);
    }

    /// <summary>Read the RTC calendar (<c>GET 0x0/0x02</c>).</summary>
    public async Task<RtcDateTime> GetRtcAsync(CancellationToken ct = default)
    {
        var data = await RequestOkAsync(LeveltronicApi.GetRtc, ct: ct).ConfigureAwait(false);
        if (data.Length < 9)
            throw new LeveltronicProtocolException($"RTC response too short ({data.Length} B).");

        return new RtcDateTime(
            Year:    BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0, 2)),
            Month:   data[2],
            Day:     data[3],
            Weekday: data[4],
            Hour:    data[5],
            Minute:  data[6],
            Second:  data[7],
            IsSet:   data[8] != 0);
    }

    /// <summary>Set the RTC calendar (<c>SET 0x0/0x02</c>, 7-byte payload).</summary>
    public Task SetRtcAsync(DateTime when, CancellationToken ct = default)
    {
        var payload = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), (ushort)when.Year);
        payload[2] = (byte)when.Month;
        payload[3] = (byte)when.Day;
        payload[4] = (byte)when.Hour;
        payload[5] = (byte)when.Minute;
        payload[6] = (byte)when.Second;
        return RequestOkAsync(LeveltronicApi.SetRtc, payload, ct);
    }

    /// <summary>Read one Settings resource as raw little-endian bytes.</summary>
    public Task<byte[]> GetSettingAsync(byte resource, CancellationToken ct = default) =>
        RequestOkAsync(LeveltronicApi.GetSetting(resource), ct: ct);

    /// <summary>Read one Settings resource and decode it per its descriptor.</summary>
    public async Task<long> GetSettingValueAsync(
        LeveltronicApi.SettingDescriptor descriptor, CancellationToken ct = default)
    {
        var data = await GetSettingAsync(descriptor.Resource, ct).ConfigureAwait(false);
        int width = LeveltronicApi.WidthOf(descriptor.Type);
        if (data.Length < width)
            throw new LeveltronicProtocolException(
                $"Setting 0x{descriptor.Resource:X2} response too short ({data.Length} B, need {width}).");

        return descriptor.Type switch
        {
            LeveltronicApi.SettingType.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(data),
            LeveltronicApi.SettingType.Int32  => BinaryPrimitives.ReadInt32LittleEndian(data),
            LeveltronicApi.SettingType.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(data),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
        };
    }

    /// <summary>Write one Settings resource from raw little-endian bytes.</summary>
    public Task SetSettingAsync(byte resource, ReadOnlyMemory<byte> value, CancellationToken ct = default) =>
        RequestOkAsync(LeveltronicApi.SetSetting(resource), value, ct);

    /// <summary>Write one Settings resource, encoding <paramref name="value"/> per its descriptor.</summary>
    public Task SetSettingValueAsync(
        LeveltronicApi.SettingDescriptor descriptor, long value, CancellationToken ct = default)
    {
        if (value < descriptor.Min || value > descriptor.Max)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"{descriptor.Field} must be in [{descriptor.Min}, {descriptor.Max}].");

        byte[] payload = new byte[LeveltronicApi.WidthOf(descriptor.Type)];
        switch (descriptor.Type)
        {
            case LeveltronicApi.SettingType.UInt16:
                BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)value);
                break;
            case LeveltronicApi.SettingType.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(payload, (int)value);
                break;
            case LeveltronicApi.SettingType.UInt32:
                BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)value);
                break;
        }
        return SetSettingAsync(descriptor.Resource, payload, ct);
    }

    /// <summary>Beep the buzzer for ~100 ms (<c>EXECUTE 0x1/0x00</c>).</summary>
    public Task TestBeepAsync(CancellationToken ct = default) =>
        RequestOkAsync(LeveltronicApi.CmdTestBeep, ct: ct);

    /// <summary>Arm the one-shot force-charge override (<c>EXECUTE 0x1/0x02</c>).</summary>
    public Task ForceChargeAsync(CancellationToken ct = default) =>
        RequestOkAsync(LeveltronicApi.CmdForceCharge, ct: ct);

    /// <summary>
    /// Command the device into the STM32 ROM bootloader (<c>EXECUTE 0x1/0x05</c>).
    /// <para>
    /// The device acknowledges, drops its USB pull-up and reboots into USB DFU
    /// (<see cref="LeveltronicApi.DfuVendorId"/> / <see cref="LeveltronicApi.DfuProductId"/>).
    /// It then stays in the bootloader on every subsequent boot until it is
    /// reflashed with the <c>nBOOT0</c> option byte restored to 1 — a power cycle
    /// does not recover it (see <c>InclinationMeterFirmware/docs/wp4_reboot_to_dfu.md</c>).
    /// </para>
    /// </summary>
    public Task RebootToDfuAsync(CancellationToken ct = default) =>
        RequestOkAsync(LeveltronicApi.CmdRebootToDfu, ct: ct);

    /// <summary>
    /// Read the current inclination in µm/m.
    /// <para>
    /// PROVISIONAL — see <see cref="LeveltronicApi.MeasInclinationResource"/>.
    /// A firmware build without the inclination resource answers
    /// <see cref="Api2Status.UnknownResource"/>, surfaced here as
    /// <see cref="InstrumentResourceNotSupportedException"/>.
    /// </para>
    /// </summary>
    public async Task<double> GetInclinationMicronsPerMetreAsync(CancellationToken ct = default)
    {
        byte[] data;
        try
        {
            data = await RequestOkAsync(LeveltronicApi.GetInclination, ct: ct).ConfigureAwait(false);
        }
        catch (LeveltronicProtocolException ex) when (ex.Status == Api2Status.UnknownResource)
        {
            throw new InstrumentResourceNotSupportedException(
                "This firmware build does not expose an inclination measurement yet " +
                $"(API v2 Measurements 0x{LeveltronicApi.MeasInclinationResource:X2}). " +
                "Update the instrument firmware.");
        }

        return ParseInclinationMicronsPerMetre(data);
    }

    /// <summary>
    /// Decode the inclination Measurements payload. PROVISIONAL: assumes
    /// <c>int32 LE</c>, units µm/m. Change together with
    /// <see cref="LeveltronicApi.MeasInclinationResource"/> when the firmware
    /// resource format is finalised.
    /// </summary>
    internal static double ParseInclinationMicronsPerMetre(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new LeveltronicProtocolException(
                $"Inclination response too short ({data.Length} B, need 4).");
        return BinaryPrimitives.ReadInt32LittleEndian(data);
    }

    // ── Core request/response ────────────────────────────────────────────────

    /// <summary>
    /// Send <paramref name="opcode"/> with <paramref name="payload"/>, wait for
    /// the echoed response, and return its resource bytes. Throws
    /// <see cref="LeveltronicProtocolException"/> on a non-OK status or timeout.
    /// </summary>
    public async Task<byte[]> RequestOkAsync(
        ushort opcode, ReadOnlyMemory<byte> payload = default, CancellationToken ct = default)
    {
        var (status, data) = await RequestAsync(opcode, payload, ct).ConfigureAwait(false);
        if (status is not Api2Status.Ok)
            throw new LeveltronicProtocolException(opcode, status ?? Api2Status.BadLength);
        return data;
    }

    private async Task<(Api2Status? status, byte[] data)> RequestAsync(
        ushort opcode, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        byte[] frame = ApiV2Codec.BuildFrame(opcode, payload.Span);

        await _requestGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<(Api2Status?, byte[])>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(opcode, tcs))
                throw new InvalidOperationException(
                    $"A request for opcode 0x{opcode:X4} is already in flight.");

            try
            {
                await _link.SendAsync(frame, ct).ConfigureAwait(false);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(DefaultTimeout);
                await using var reg = timeoutCts.Token.Register(static s =>
                    ((TaskCompletionSource<(Api2Status?, byte[])>)s!).TrySetCanceled(), tcs);

                try
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new LeveltronicProtocolException(
                        $"No response to opcode 0x{opcode:X4} within {DefaultTimeout.TotalSeconds:0.#} s.");
                }
            }
            finally
            {
                _pending.TryRemove(opcode, out _);
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private void OnPacketReceived(object? sender, byte[] packet)
    {
        if (!ApiV2Codec.TryParseFrame(packet, out ushort opcode, out var status, out var data))
            return;

        if (_pending.TryGetValue(opcode, out var tcs))
            tcs.TrySetResult((status, data));
        // Unmatched opcode (e.g. a subscription push) — ignored by this client.
    }

    private static string ReadAsciiZ(ReadOnlySpan<byte> span)
    {
        int end = span.IndexOf((byte)0);
        if (end < 0) end = span.Length;
        return Encoding.ASCII.GetString(span[..end]);
    }

    public async ValueTask DisposeAsync()
    {
        _link.PacketReceived -= OnPacketReceived;
        _requestGate.Dispose();
        await _link.DisposeAsync().ConfigureAwait(false);
    }
}
