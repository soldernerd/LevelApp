namespace LevelApp.Instruments.Leveltronic.Protocol;

/// <summary>A device reply: the status byte plus the data that follows an OK status.</summary>
public sealed record ApiReply(ApiStatus Status, byte[] Data)
{
    public ApiReply(ApiStatus status) : this(status, []) { }

    public bool Ok => Status == ApiStatus.Ok;

    /// <exception cref="ApiException">The status is not OK.</exception>
    public ApiReply OrThrow(string what) => Ok ? this : throw new ApiException(Status, what);
}

/// <summary>The device answered, but not with OK.</summary>
public sealed class ApiException(ApiStatus status, string what) : Exception($"{what}: {status}")
{
    public ApiStatus Status { get; } = status;
    public string What { get; } = what;

    public bool IsServiceModeRequired => Status == ApiStatus.ServiceModeRequired;

    /// <summary>Operator-facing text, the same wording as the phone app.</summary>
    public string UserMessage => Status switch
    {
        ApiStatus.ServiceModeRequired =>
            "Service mode is off. Switch it on at the instrument " +
            "(SETTINGS > Service mode, press the right knob twice).",
        ApiStatus.BusyResource =>
            "The instrument is busy. Make sure the measurement is running and " +
            "no other procedure is active, then try again.",
        ApiStatus.BusyExclusive =>
            "Another activity (raw capture or phasor stream) is using the " +
            "sensor. Wait for it to finish.",
        ApiStatus.InvalidParameter =>
            "The instrument rejected the value (out of range or conflicting " +
            "with another setting).",
        ApiStatus.UnknownResource =>
            $"This instrument firmware does not support \"{What}\".",
        _ => $"The instrument answered {Status} to \"{What}\".",
    };
}

/// <summary>
/// The link itself failed (not connected, timeout, dropped) — as opposed to the
/// device answering with an error status.
/// </summary>
public sealed class LinkException(string message) : Exception(message);

/// <summary>Why a connection was refused, in words fit for the connection bar.</summary>
public abstract class ConnectFailureException(string message) : Exception(message);

/// <summary>The connected instrument does not speak the protocol this app does.</summary>
public sealed class IncompatibleFirmwareException(int? apiVersion, string? firmware)
    : ConnectFailureException(Describe(apiVersion, firmware))
{
    public int? ApiVersion { get; } = apiVersion;
    public string? Firmware { get; } = firmware;

    private static string Describe(int? apiVersion, string? firmware) => apiVersion is null
        ? $"This instrument runs firmware that does not speak API v{ApiV3Codec.ApiVersion}. " +
          "Update it to 0.11 or newer."
        : $"This instrument speaks API v{apiVersion} (firmware {firmware ?? "?"}); " +
          $"this app needs API v{ApiV3Codec.ApiVersion}.";
}

/// <summary>The device answered, but it is not a Leveltronic.</summary>
public sealed class UnsupportedDeviceException(string? product)
    : ConnectFailureException(product is null
        ? "This device did not identify itself. Its firmware may be too old for this app."
        : $"This app does not support \"{product}\".");

/// <summary>A precision measurement was cancelled by the caller.</summary>
public sealed class PrecisionCancelledException() : OperationCanceledException("The measurement was cancelled.");
