using System.Diagnostics;
using System.IO.Compression;

// Args (index 0 is the process name):
//   [1] zipPath       – path to the downloaded zip
//   [2] installFolder – folder to extract into (contains the running app)
//   [3] mainExeName   – executable to relaunch after update
//   [--from-temp]     – signals we are already running from the temp copy

string logPath = Path.Combine(Path.GetTempPath(), "LevelApp.Updater.log");

void Log(string message)
{
    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
    Console.WriteLine(line);
    File.AppendAllText(logPath, line + Environment.NewLine);
}

try
{
    File.WriteAllText(logPath, $"=== LevelApp Updater started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
    Log($"Args: {string.Join(" | ", Environment.GetCommandLineArgs())}");

    var cmdArgs  = Environment.GetCommandLineArgs(); // [0] = process name
    var argsList = cmdArgs.ToList();

    if (cmdArgs.Length < UpdaterContract.ExpectedArgCount + 1) // +1 for process name
    {
        Log("ERROR: insufficient arguments");
        Log($"Usage: LevelApp.Updater <zipPath> <installFolder> <mainExeName> [{UpdaterContract.FromTempFlag}]");
        Console.ReadKey();
        return 1;
    }

    string zipPath       = cmdArgs[UpdaterContract.ArgZipPath       + 1];
    string installFolder = cmdArgs[UpdaterContract.ArgInstallFolder + 1];
    string mainExeName   = cmdArgs[UpdaterContract.ArgMainExeName   + 1];
    bool   fromTemp      = argsList.Contains(UpdaterContract.FromTempFlag);

    Log($"zipPath:       {zipPath}");
    Log($"installFolder: {installFolder}");
    Log($"mainExeName:   {mainExeName}");
    Log($"fromTemp:      {fromTemp}");

    if (!fromTemp)
    {
        // Step 1–3: copy self to a temp path and re-launch with --from-temp so
        // the install folder is fully unlocked when extraction happens.
        string tempExe = Path.Combine(Path.GetTempPath(), "LevelApp.Updater.tmp.exe");
        Log($"Copying self to {tempExe}");
        File.Copy(Environment.ProcessPath!, tempExe, overwrite: true);

        Log("Launching temp copy...");
        Process.Start(new ProcessStartInfo
        {
            FileName        = tempExe,
            Arguments       = $"\"{zipPath}\" \"{installFolder}\" \"{mainExeName}\" {UpdaterContract.FromTempFlag}",
            UseShellExecute = true   // visible window so errors are readable
        });

        return 0;
    }

    // Step 4: wait for the main app to exit (it should already be exiting, but
    // allow up to 10 seconds in case of a slow shutdown).
    Log("Waiting for main app to exit...");
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < deadline)
    {
        var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(mainExeName));
        if (procs.Length == 0) break;
        Log($"  still running ({procs.Length} instance(s))…");
        Thread.Sleep(200);
    }
    Log("Main app exited (or timed out).");

    // Step 5: extract the zip over the install folder, overwriting all files.
    Log($"Extracting {zipPath} → {installFolder}");
    ZipFile.ExtractToDirectory(zipPath, installFolder, overwriteFiles: true);
    Log("Extraction complete.");

    // Step 6: delete the downloaded zip.
    File.Delete(zipPath);
    Log("Zip deleted.");

    // Step 7: launch the new version.
    string newExePath = Path.Combine(installFolder, mainExeName);
    Log($"Launching {newExePath}");
    Process.Start(new ProcessStartInfo
    {
        FileName        = newExePath,
        UseShellExecute = true
    });

    Log("Done.");
    return 0;
}
catch (Exception ex)
{
    Log($"FATAL: {ex}");
    Console.WriteLine();
    Console.WriteLine("Press any key to close...");
    Console.ReadKey();
    return 1;
}
