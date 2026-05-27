using System.Diagnostics;
using System.IO.Compression;

// Args (index 0 is the process name):
//   [1] zipPath       – path to the downloaded zip
//   [2] installFolder – folder to extract into (contains the running app)
//   [3] mainExeName   – executable to relaunch after update
//   [--from-temp]     – signals we are already running from the temp copy

var cmdArgs  = Environment.GetCommandLineArgs();
var argsList = cmdArgs.ToList();

if (cmdArgs.Length < 4)
{
    Console.Error.WriteLine(
        "Usage: LevelApp.Updater <zipPath> <installFolder> <mainExeName> [--from-temp]");
    return 1;
}

string zipPath       = cmdArgs[1];
string installFolder = cmdArgs[2];
string mainExeName   = cmdArgs[3];
bool   fromTemp      = argsList.Contains("--from-temp");

if (!fromTemp)
{
    // Step 1–3: copy self to a temp path and re-launch with --from-temp so
    // the install folder is fully unlocked when extraction happens.
    string tempExe = Path.Combine(Path.GetTempPath(), "LevelApp.Updater.tmp.exe");
    File.Copy(Environment.ProcessPath!, tempExe, overwrite: true);

    Process.Start(new ProcessStartInfo
    {
        FileName        = tempExe,
        Arguments       = $"\"{zipPath}\" \"{installFolder}\" \"{mainExeName}\" --from-temp",
        UseShellExecute = false
    });

    return 0;
}

// Step 4: wait for the main app to exit (it should already be exiting, but
// allow up to 10 seconds in case of a slow shutdown).
var deadline = DateTime.UtcNow.AddSeconds(10);
while (DateTime.UtcNow < deadline)
{
    var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(mainExeName));
    if (procs.Length == 0) break;
    Thread.Sleep(200);
}

// Step 5: extract the zip over the install folder, overwriting all files.
ZipFile.ExtractToDirectory(zipPath, installFolder, overwriteFiles: true);

// Step 6: delete the downloaded zip.
File.Delete(zipPath);

// Step 7: launch the new version.
Process.Start(new ProcessStartInfo
{
    FileName        = Path.Combine(installFolder, mainExeName),
    UseShellExecute = true
});

return 0;
