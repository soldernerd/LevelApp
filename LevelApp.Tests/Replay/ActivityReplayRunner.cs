using System.Text.Json;
using LevelApp.Core;
using Xunit.Abstractions;

namespace LevelApp.Tests.Replay;

/// <summary>
/// Reads a <c>.jsonl</c> activity log line by line and dispatches each entry to
/// the appropriate ViewModel method, reproducing the recorded user session.
///
/// Stops replay (without failing) when a <c>CRASH</c> or <c>CRASH.UI</c> entry
/// is encountered — that entry marks where the original session died.
///
/// The replay runner resolves file paths relative to the <c>TestLogs/</c> folder
/// so that sessions are hermetic (no dependency on the original machine).
///
/// NOTE: Most action dispatch cases are stubs (// TODO) because the page ViewModels
/// that handle Cmd.* and Input.Changed actions are not yet wired into the replay
/// infrastructure.  The runner will be extended as those ViewModels are made
/// testable.
/// </summary>
public sealed class ActivityReplayRunner
{
    // TODO: Replace IReplayTarget with MainViewModel when LevelApp.App is
    // referenced from the test project (requires net8.0-windows10.0.19041.0 target).
    private readonly IReplayTarget      _vm;
    private readonly ITestOutputHelper  _output;
    private readonly string             _testLogsFolder;

    public ActivityReplayRunner(IReplayTarget vm, ITestOutputHelper output)
    {
        _vm             = vm;
        _output         = output;
        _testLogsFolder = Path.Combine(AppContext.BaseDirectory, "TestLogs");
    }

    /// <summary>
    /// Replays all entries in <paramref name="logPath"/>. Returns without throwing
    /// on <c>CRASH</c> / <c>CRASH.UI</c> entries.
    /// </summary>
    public async Task ReplayAsync(string logPath)
    {
        string? recordedVersion = null;

        foreach (string line in File.ReadLines(logPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var entry = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line)!;

            if (!entry.TryGetValue("action", out var actionEl)) continue;
            string action = actionEl.GetString() ?? string.Empty;

            string? detail = entry.TryGetValue("detail", out var detailEl)
                             && detailEl.ValueKind != JsonValueKind.Null
                             ? detailEl.GetString()
                             : null;

            // Stop replay at the point where the session crashed
            if (action is "CRASH" or "CRASH.UI")
            {
                _output.WriteLine($"[Replay] Session ended with {action}: {detail}");
                return;
            }

            await DispatchAsync(action, detail, entry);

            if (recordedVersion is null && action == "Session.Start"
                && entry.TryGetValue("version", out var vEl))
            {
                recordedVersion = vEl.GetString();
                if (recordedVersion != AppVersion.Full)
                    _output.WriteLine(
                        $"[Replay] Version mismatch: log={recordedVersion}, current={AppVersion.Full}");
            }
        }
    }

    // ── Dispatcher ────────────────────────────────────────────────────────────

    private async Task DispatchAsync(string action, string? detail,
                                      Dictionary<string, JsonElement> entry)
    {
        switch (action)
        {
            // ── Session lifecycle ─────────────────────────────────────────────
            case "Session.Start":
            case "Session.End":
                // No ViewModel call needed for session boundary markers
                break;

            // ── File operations ───────────────────────────────────────────────
            case "File.Open":
                // Resolve path: use snapshot from TestLogs/ if present
                string? snapshotName = entry.TryGetValue("snapshot", out var snEl)
                    ? snEl.GetString() : null;
                string? resolvedPath = snapshotName is not null
                    ? Path.Combine(_testLogsFolder, snapshotName)
                    : null;
                if (resolvedPath is not null && File.Exists(resolvedPath))
                {
                    // TODO: Call vm.LoadProjectFromPathAsync(resolvedPath) when implemented
                    _output.WriteLine($"[Replay] File.Open → {resolvedPath}");
                }
                else if (detail is not null)
                {
                    // TODO: Call vm.LoadProjectFromPathAsync(detail) when implemented
                    _output.WriteLine($"[Replay] File.Open → {detail} (original path)");
                }
                break;

            case "File.Save":
                // TODO: Call vm.SaveProjectAsync() when ViewModel is injectable
                break;

            case "File.SaveAs":
                // TODO: Call vm.SaveProjectAsAsync() when ViewModel is injectable
                break;

            case "File.Close":
                // TODO: Call vm.ClearProject() when ViewModel is injectable
                break;

            case "File.New":
                // TODO: Call vm.NewProjectAsync() when ViewModel is injectable
                break;

            // ── Commands ──────────────────────────────────────────────────────
            case "Cmd.StartMeasurement":
                // TODO: Call MeasurementViewModel.StartMeasurementCommand when injectable
                break;

            case "Cmd.StopMeasurement":
                // TODO: Call MeasurementViewModel.StopMeasurementCommand when injectable
                break;

            case "Cmd.RecalculateFlatness":
                // TODO: Call ResultsViewModel.RecalculateFlatnessCommand when injectable
                break;

            case "Cmd.ApplyCorrection":
                // TODO: Call CorrectionViewModel.ApplyCorrectionCommand when injectable
                break;

            case "Cmd.ExportReport":
                // TODO: Call ResultsViewModel.ExportReportCommand when injectable
                break;

            // ── Input changes ─────────────────────────────────────────────────
            case "Input.Changed":
                // TODO: Apply field change to the appropriate ViewModel property
                break;

            default:
                _output.WriteLine($"[Replay] Unknown action: {action}");
                break;
        }

        await Task.CompletedTask;
    }
}
