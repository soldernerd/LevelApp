namespace LevelApp.Core.Instruments;

public record FirmwareInfo(
    string Version,
    string? ReleaseNotes,
    string? DownloadUrl
);
