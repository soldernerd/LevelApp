// IMPORTANT: This file is duplicated verbatim in LevelApp.App and LevelApp.Updater.
// LevelApp.Updater targets net8.0 (no Windows TFM) and cannot reference LevelApp.App,
// so the constants are kept in sync manually. If you change any value here, update
// LevelApp.Updater/UpdaterContract.cs to match and bump AppVersion.

namespace LevelApp.App.Services;

/// <summary>
/// Positional argument contract between UpdateService (which launches the updater)
/// and LevelApp.Updater (which consumes the arguments).
/// Arguments: zipPath installFolder mainExeName [--from-temp]
/// </summary>
internal static class UpdaterContract
{
    public const int    ArgZipPath       = 0;
    public const int    ArgInstallFolder = 1;
    public const int    ArgMainExeName   = 2;
    public const int    ExpectedArgCount = 3;
    public const string FromTempFlag     = "--from-temp";
}
