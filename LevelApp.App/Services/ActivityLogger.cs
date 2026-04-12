using System.Reflection;
using System.Text.Json;
using LevelApp.Core;
using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.App.Services;

/// <summary>
/// Singleton activity logger.  On construction it creates the log folder, writes
/// the Session.Start entry, and prunes files older than <see cref="LogRetentionDays"/> days.
///
/// All file I/O is protected by <see cref="_lock"/>.
/// <see cref="System.IO.File.AppendAllText"/> is used for both .jsonl and .instrument
/// writes so every entry is flushed to disk immediately — no buffered streams.
/// </summary>
public sealed class ActivityLogger : IActivityLogger
{
    private const int LogRetentionDays = 14;

    private readonly object _lock             = new();
    private readonly string _sessionTimestamp;
    private readonly string _logFolder;
    private readonly string _jsonlPath;
    private          string? _instrumentPath;
    private          int    _snapshotCount;

    public bool IsEnabled { get; set; }

    public ActivityLogger(ISettingsService settings)
    {
        IsEnabled = settings.ActivityLoggingEnabled;

        _sessionTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _logFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LevelApp", "Logs");

        Directory.CreateDirectory(_logFolder);
        DeleteOldLogs();

        _jsonlPath = Path.Combine(_logFolder, $"activity_{_sessionTimestamp}.jsonl");

        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? AppVersion.Full;

        Log("Session.Start", null, new Dictionary<string, object> { ["version"] = version });
    }

    // ── IActivityLogger ───────────────────────────────────────────────────────

    public void Log(string action, string? detail = null,
                    Dictionary<string, object>? extra = null)
    {
        if (!IsEnabled) return;

        var entry = new Dictionary<string, object?>
        {
            ["ts"]     = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
            ["action"] = action,
            ["detail"] = detail
        };

        if (extra is not null)
            foreach (var (key, value) in extra)
                entry[key] = value;

        string line = JsonSerializer.Serialize(entry) + Environment.NewLine;

        lock (_lock)
            File.AppendAllText(_jsonlPath, line);
    }

    /// <summary>
    /// Copies the project file, increments the snapshot counter, and writes the
    /// <c>File.Open</c> log entry (which includes the <c>snapshot</c> field).
    /// Call this instead of <c>Log("File.Open", …)</c>.
    /// </summary>
    public void AttachProjectSnapshot(string projectPath)
    {
        if (!IsEnabled) return;

        lock (_lock)
        {
            _snapshotCount++;
            string destName = $"activity_{_sessionTimestamp}_p{_snapshotCount}.levelproj";
            string destPath = Path.Combine(_logFolder, destName);

            try { File.Copy(projectPath, destPath, overwrite: true); }
            catch { /* snapshot failure must not crash the app */ }

            // Write the File.Open entry including the snapshot filename
            var entry = new Dictionary<string, object?>
            {
                ["ts"]       = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                ["action"]   = "File.Open",
                ["detail"]   = projectPath,
                ["snapshot"] = destName
            };
            File.AppendAllText(_jsonlPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
    }

    public void AttachInstrumentRecording(InstrumentReading reading)
    {
        if (!IsEnabled) return;

        _instrumentPath ??= Path.Combine(_logFolder, $"activity_{_sessionTimestamp}.instrument");

        string line = JsonSerializer.Serialize(reading) + Environment.NewLine;

        lock (_lock)
            File.AppendAllText(_instrumentPath, line);
    }

    // ── Session end ───────────────────────────────────────────────────────────

    /// <summary>Writes the Session.End entry. Call from App.OnLaunched exit hooks.</summary>
    public void LogSessionEnd() => Log("Session.End");

    // ── Private helpers ───────────────────────────────────────────────────────

    private void DeleteOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-LogRetentionDays);
            foreach (string file in Directory.GetFiles(_logFolder))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { /* log cleanup must not prevent startup */ }
    }
}
