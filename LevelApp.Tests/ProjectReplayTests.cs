using LevelApp.Core.Geometry;
using LevelApp.Core.Geometry.ParallelWays;
using LevelApp.Core.Models;
using LevelApp.Core.Serialization;

namespace LevelApp.Tests;

/// <summary>
/// Loads every .levelproj file from docs/sampleProjects/, re-runs the
/// appropriate calculator on each session's recorded readings, and asserts
/// that a valid result is produced.  This catches regressions where a model
/// or calculator change silently breaks the Core computation layer.
/// </summary>
public class ProjectReplayTests
{
    // ── Discovery ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up from the test output folder until LevelApp.slnx is found,
    /// then returns one entry per .levelproj file in docs/sampleProjects/.
    /// Returns an empty sequence (no test cases) if the folder cannot be
    /// located — this is intentional and does NOT cause a test failure.
    /// </summary>
    public static IEnumerable<object[]> SampleProjects()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LevelApp.slnx")))
            dir = dir.Parent;

        if (dir is null) yield break;

        var sampleDir = Path.Combine(dir.FullName, "docs", "sampleProjects");
        if (!Directory.Exists(sampleDir)) yield break;

        foreach (var path in Directory.GetFiles(sampleDir, "*.levelproj"))
            yield return new object[] { path };
    }

    // ── Test ──────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SampleProjects))]
    public void ReplayProject_DoesNotThrow_AndProducesResult(string projectPath)
    {
        var json    = File.ReadAllText(projectPath);
        var project = ProjectSerializer.Deserialize(json);

        Assert.NotNull(project);

        foreach (var session in project.Measurements)
        {
            var steps = session.InitialRound.Steps
                .Where(s => s.Reading.HasValue)
                .ToList();

            if (steps.Count == 0) continue;   // skip sessions with no readings recorded

            var calcParams = session.InitialRound.CalculationParameters
                             ?? new CalculationParameters();

            if (session.StrategyId == "ParallelWays")
            {
                var calc   = new ParallelWaysCalculator();
                var result = calc.Calculate(steps, project.ObjectDefinition, calcParams);

                Assert.NotNull(result);
                Assert.NotEmpty(result.RailProfiles);
            }
            else
            {
                var strategy = StrategyFactory.Create(session.StrategyId);
                var calc     = CalculatorFactory.Create(calcParams.MethodId, strategy);
                var result   = calc.Calculate(steps, project.ObjectDefinition, calcParams);

                Assert.NotNull(result);
                Assert.NotEmpty(result.NodeHeights);
            }
        }
    }
}
