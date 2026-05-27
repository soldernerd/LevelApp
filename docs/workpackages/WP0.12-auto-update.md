# Work Package 0.10 — Auto-Update

> Target version: **v0.10.0**
> Prerequisite: WP0.09 complete (v0.9.0) ✓

---

## Goal

When a newer version is available on GitHub Releases, the app prompts the
user at startup. If the user accepts, the new version is downloaded, the app
exits, a small updater process swaps the files, and the new version launches
automatically.

The updater is itself included in the release zip and is therefore also
updated on every release.

---

## New project: `LevelApp.Updater`

A plain .NET 8 console application with no UI dependencies and no reference
to `LevelApp.Core` or `LevelApp.App`.

Add to `LevelApp.slnx`.

### Arguments

```
LevelApp.Updater.exe <zipPath> <installFolder> <mainExeName>
```

| Argument | Example |
|---|---|
| `zipPath` | `C:\Users\Lukas\AppData\Local\Temp\LevelApp-0.10.0.zip` |
| `installFolder` | `C:\Program Files\LevelApp` |
| `mainExeName` | `LevelApp.App.exe` |

### Behaviour

```
1. Copy self (LevelApp.Updater.exe) to %TEMP%\LevelApp.Updater.tmp.exe
2. Re-launch from the temp copy with the same arguments + flag --from-temp
3. Original process exits

(Now running from temp location — install folder is fully unlocked)

4. Wait for LevelApp.App.exe to exit (it has already been told to exit
   by the time the updater is launched, but allow up to 10 seconds)
5. Extract zipPath over installFolder (overwrite all files)
6. Delete zipPath
7. Launch installFolder\mainExeName
8. Exit
```

The `--from-temp` flag prevents infinite self-copy loops. If the flag is
present, skip steps 1–3 and go straight to step 4.

### `Program.cs` (outline)

```csharp
// LevelApp.Updater/Program.cs
using System.Diagnostics;
using System.IO.Compression;

var args = Environment.GetCommandLineArgs();
// Parse: zipPath, installFolder, mainExeName, optional --from-temp flag

if (!fromTemp)
{
    // Copy self to temp and relaunch
    var tempExe = Path.Combine(Path.GetTempPath(), "LevelApp.Updater.tmp.exe");
    File.Copy(Environment.ProcessPath!, tempExe, overwrite: true);
    Process.Start(tempExe, $"\"{zipPath}\" \"{installFolder}\" \"{mainExeName}\" --from-temp");
    return;
}

// Wait for main app to exit (max 10 s)
var mainExePath = Path.Combine(installFolder, mainExeName);
var deadline = DateTime.UtcNow.AddSeconds(10);
while (DateTime.UtcNow < deadline)
{
    var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(mainExeName));
    if (procs.Length == 0) break;
    Thread.Sleep(200);
}

// Extract zip over install folder
ZipFile.ExtractToDirectory(zipPath, installFolder, overwriteFiles: true);
File.Delete(zipPath);

// Launch new version
Process.Start(Path.Combine(installFolder, mainExeName));
```

No NuGet dependencies beyond the .NET 8 base class library.

---

## New service: `IUpdateService` / `UpdateService`

Add to `LevelApp.App/Services/`.

### `IUpdateService.cs`

```csharp
public interface IUpdateService
{
    /// <summary>
    /// Checks GitHub Releases for a version newer than the running app.
    /// Returns null if up to date or check fails.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync();

    /// <summary>
    /// Downloads the release zip to a temp file and returns the local path.
    /// Reports progress (0.0–1.0) via the callback.
    /// </summary>
    Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double> progress);
}

public record UpdateInfo(string Version, string TagName, string ZipUrl, string ReleaseNotes);
```

### `UpdateService.cs`

- Uses `HttpClient` to call
  `https://api.github.com/repos/soldernerd/LevelApp/releases/latest`
- Parses the JSON response: `tag_name`, `assets[0].browser_download_url`,
  `body` (release notes)
- Compares `tag_name` (e.g. `"v0.10.0"`) against `AppVersion.Full`
  using `Version.Parse` for correct numeric ordering
- On download, streams the zip to
  `%TEMP%\LevelApp-<version>.zip` with progress reporting
- Sets `User-Agent` header (required by GitHub API):
  `LevelApp/{AppVersion.Full}`
- If the HTTP call fails for any reason (no network, rate limit, etc.),
  returns `null` silently — a failed update check must never prevent the
  app from starting

Register `UpdateService` as a singleton in `App.xaml.cs` DI container:
```csharp
services.AddSingleton<IUpdateService, UpdateService>();
```

---

## New dialog: `UpdateDialog.xaml`

A `ContentDialog` in `LevelApp.App/Views/Dialogs/`.

```xml
<ContentDialog
    Title="Update Available"
    PrimaryButtonText="Update Now"
    CloseButtonText="Not Now">

  <StackPanel Spacing="12">
    <TextBlock>
      <Run Text="Version "/>
      <Run x:Name="VersionRun"/>
      <Run Text=" is available (you have "/>
      <Run Text="{x:Bind CurrentVersion}"/>
      <Run Text=")."/>
    </TextBlock>

    <ScrollViewer MaxHeight="200">
      <TextBlock x:Name="ReleaseNotesText"
                 TextWrapping="Wrap"
                 Style="{StaticResource HelperTextStyle}"/>
    </ScrollViewer>

    <!-- Shown only during download -->
    <ProgressBar x:Name="DownloadProgress"
                 Visibility="Collapsed"
                 Minimum="0" Maximum="1"/>

    <TextBlock x:Name="StatusText"
               Style="{StaticResource HelperTextStyle}"
               Visibility="Collapsed"/>
  </StackPanel>

</ContentDialog>
```

Behaviour:
- On open: shows version info and release notes; "Update Now" and "Not Now"
  enabled
- On "Update Now" click: disable both buttons, show `DownloadProgress` and
  `StatusText` ("Downloading…"), download via `IUpdateService.DownloadUpdateAsync`
- On download complete: `StatusText` → "Restarting…", launch
  `LevelApp.Updater.exe` with correct arguments, call `Application.Current.Exit()`

---

## Changes to `MainWindow.xaml.cs`

Add an update check call early in the startup sequence, after the DI
container is built and the main window is shown:

```csharp
private async void CheckForUpdateOnStartup()
{
    var updateService = App.Services.GetRequiredService<IUpdateService>();
    var update = await updateService.CheckForUpdateAsync();
    if (update is null) return;

    var dialog = new UpdateDialog(update)
    {
        XamlRoot = Content.XamlRoot
    };
    await dialog.ShowAsync();
}
```

Call `CheckForUpdateOnStartup()` from the window's `Loaded` event (not the
constructor) so the window is fully rendered before the dialog appears.

---

## `LevelApp.Updater` in the release zip

The CI pipeline (WP0.09) publishes `LevelApp.App`. Update the publish step
to also publish `LevelApp.Updater` into the same output folder before
zipping:

```yaml
- name: Package release zip
  shell: pwsh
  run: |
    $publishDir = "publish"
    dotnet publish LevelApp.App/LevelApp.App.csproj `
      --configuration Release --output $publishDir --no-build
    dotnet publish LevelApp.Updater/LevelApp.Updater.csproj `
      --configuration Release --output $publishDir --no-build
    Compress-Archive -Path "$publishDir\*" `
      -DestinationPath "LevelApp-${{ steps.version.outputs.VERSION }}.zip"
```

This ensures every release zip contains a fresh `LevelApp.Updater.exe`,
so the updater is updated along with everything else.

---

## Install path convention

The updater needs to know the install folder. The app determines this at
runtime as:

```csharp
var installFolder = AppContext.BaseDirectory;
```

This is the folder containing the running `LevelApp.App.exe`, which is
exactly the folder the updater should overwrite.

---

## What this work package explicitly does NOT do

- Add any UI for manually triggering an update (startup check only)
- Handle delta/incremental updates (always full zip)
- Support rollback
- Handle the case where the install folder requires elevation (UAC).
  If `C:\Program Files\LevelApp` requires admin rights to write,
  the updater will fail silently. Document this limitation; resolution
  is deferred (simplest fix: advise beta testers to install to a user-
  writable folder such as `%LOCALAPPDATA%\LevelApp`).

---

## Acceptance criteria

1. `LevelApp.Updater` project exists, builds, and is included in the
   release zip produced by the CI pipeline
2. On startup, if GitHub Releases reports a newer version, an
   `UpdateDialog` appears showing the new version number and release notes
3. Clicking "Update Now" downloads the zip with a visible progress bar,
   then exits the app and launches the updater
4. The updater extracts the zip over the install folder and launches the
   new `LevelApp.App.exe`
5. After the update, `AppVersion.Display` in the About dialog reflects
   the new version
6. If the update check fails (no network, API error), the app starts
   normally with no error shown to the user
7. Clicking "Not Now" dismisses the dialog; the app starts normally

---

## Version bump

Set `AppVersion.Minor` → `10`, `AppVersion.Patch` → `0` in `AppVersion.cs`
before committing. Commit message:

```
[v0.10.0] WP0.10: auto-update via GitHub Releases + LevelApp.Updater
```
