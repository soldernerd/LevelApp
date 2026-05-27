namespace LevelApp.App.Services;

public interface IUpdateService
{
    /// <summary>
    /// Checks GitHub Releases for a version newer than the running app.
    /// Returns null if up to date or the check fails for any reason.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync();

    /// <summary>
    /// Downloads the release zip to a temp file and returns the local path.
    /// Reports progress (0.0–1.0) via the callback.
    /// </summary>
    Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double> progress);
}

public record UpdateInfo(string Version, string TagName, string ZipUrl, string ReleaseNotes);
