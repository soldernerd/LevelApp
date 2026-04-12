using Xunit.Abstractions;

namespace LevelApp.Tests.Replay;

/// <summary>
/// Theory-based replay tests.  Each <c>.jsonl</c> file placed in
/// <c>TestLogs/</c> (alongside any companion <c>.levelproj</c> and
/// <c>.instrument</c> files) becomes a separate test case.
///
/// When <c>TestLogs/</c> is empty, <see cref="GetLogFiles"/> returns an empty
/// sequence and no tests run — this is intentional and is NOT treated as a
/// test failure.
/// </summary>
public class ReplayTests(ITestOutputHelper output)
{
    [Theory]
    [MemberData(nameof(GetLogFiles))]
    public async Task ReplayLog_ShouldNotCrash(string logPath)
    {
        // TODO: Replace NullReplayTarget with:
        //   new MainViewModel(new StubNavigationService(), new StubProjectFileService(), ...)
        // once LevelApp.App is referenced from this project.
        var vm = new NullReplayTarget();
        var runner = new ActivityReplayRunner(vm, output);

        var ex = await Record.ExceptionAsync(() => runner.ReplayAsync(logPath));

        Assert.Null(ex);
    }

    public static IEnumerable<object[]> GetLogFiles() =>
        Directory.GetFiles(
                Path.Combine(AppContext.BaseDirectory, "TestLogs"), "*.jsonl")
            .Select(f => new object[] { f });

    // ── Minimal stub ──────────────────────────────────────────────────────────

    private sealed class NullReplayTarget : IReplayTarget { }
}
