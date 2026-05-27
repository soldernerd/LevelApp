# Work Package 0.11 — Replay Tests

> Target version: **v0.11.0**
> Prerequisite: WP0.10 complete (v0.10.0) ✓

---

## Goal

Add a headless replay test suite to `LevelApp.Tests` that loads every
`.levelproj` file committed to `docs/sampleProjects/`, deserialises it,
runs the appropriate calculator, and asserts the result is non-null and
consistent. These tests run entirely within `LevelApp.Core` — no UI, no
file dialogs, no WinUI 3 dependencies.

Update the CI pipeline to include replay tests from this point forward.

This work package also defines the process by which a developer promotes a
recorded user session to a committed sample project.

---

## Prerequisites before implementation

Before Claude Code starts, the developer must:

1. Review the locally recorded user sessions (stored by `SettingsService`
   in `%LOCALAPPDATA%\LevelApp\sessions\` or equivalent — check the actual
   log path in the running app)
2. Select at least one representative session per geometry type
   (Surface Plate, Parallel Ways) covering at minimum:
   - A complete session with no flagged steps
   - A complete session with flagged steps and at least one correction round
3. Copy those `.levelproj` files to `docs/sampleProjects/` and commit them
   before starting the work package

If no recorded sessions are available yet, create synthetic `.levelproj`
files using the existing Python data-generation scripts, or manually via
the app, and commit those instead. The replay tests are meaningless without
at least one sample file.

---

## New test class: `ReplayTests.cs`

Add to `LevelApp.Tests/`.

### Design

The test class uses **xUnit's `[Theory]` + `[MemberData]`** pattern to
discover all `.levelproj` files in `docs/sampleProjects/` at test collection
time. Each file becomes a separate test case, named by its filename.

```csharp
// LevelApp.Tests/ReplayTests.cs

public class ReplayTests
{
    /// <summary>
    /// Discovers all .levelproj files relative to the repo root.
    /// Works both locally and in the GitHub Actions runner because
    /// the repo is checked out at a known relative path from the
    /// test binary output folder.
    /// </summary>
    public static IEnumerable<object[]> SampleProjects()
    {
        // Walk up from the test output folder to find the repo root
        // (looks for LevelApp.slnx as a landmark)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LevelApp.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir); // repo root not found

        var sampleDir = Path.Combine(dir.FullName, "docs", "sampleProjects");
        return Directory.EnumerateFiles(sampleDir, "*.levelproj")
            .Select(path => new object[] { path });
    }

    [Theory]
    [MemberData(nameof(SampleProjects))]
    public void ReplayProject_DoesNotThrow_AndProducesResult(string filePath)
    {
        // --- Deserialise ---
        var json = File.ReadAllText(filePath);
        var project = ProjectSerializer.Deserialize(json);

        Assert.NotNull(project);
        Assert.NotNull(project.ObjectDefinition);
        Assert.NotEmpty(project.Measurements);

        // --- Replay each session ---
        foreach (var session in project.Measurements)
        {
            ReplaySession(project.ObjectDefinition, session);
        }
    }

    private static void ReplaySession(ObjectDefinition definition, MeasurementSession session)
    {
        var strategy = StrategyFactory.Create(session.StrategyId);
        Assert.NotNull(strategy);

        var allRounds = GetAllRounds(session);

        foreach (var (round, label) in allRounds)
        {
            var steps = MeasurementRound.MergeWithReplacements(
                session.InitialRound.Steps, round.ReplacedSteps ?? []);

            // All steps must have readings for a completed round
            Assert.All(steps, s => Assert.NotNull(s.Reading));

            if (session.StrategyId == "ParallelWays")
            {
                var calculator = new ParallelWaysCalculator();
                var result = calculator.Calculate(steps, definition);
                Assert.NotNull(result);
                Assert.NotEmpty(result.RailProfiles);
            }
            else
            {
                var calculator = CalculatorFactory.Create(
                    session.CalculationMethodId ?? "LeastSquares", strategy);
                var result = calculator.Calculate(
                    steps, definition, session.CalculationParameters
                    ?? new CalculationParameters());
                Assert.NotNull(result);
                Assert.True(result.FlatnessValueMm >= 0);
            }
        }
    }

    /// <summary>
    /// Returns all rounds in chronological order as (effective round, label) pairs.
    /// InitialRound is the first entry; each CorrectionRound follows.
    /// </summary>
    private static IEnumerable<(MeasurementRound Round, string Label)> GetAllRounds(
        MeasurementSession session)
    {
        yield return (session.InitialRound, "InitialRound");
        if (session.Corrections is null) yield break;
        foreach (var (correction, i) in session.Corrections.Select((c, i) => (c, i)))
            yield return (ToMeasurementRound(correction), $"CorrectionRound[{i}]");
    }
}
```

> **Note to Claude Code:** The exact property names (`CalculationMethodId`,
> `CalculationParameters`, `ReplacedSteps`, etc.) must be verified against
> the actual model classes before writing the test code. Adjust accordingly —
> the intent is what matters, not the exact names above.

---

## Update CI pipeline: `.github/workflows/ci.yml`

The existing `Run unit tests` step already runs all tests in
`LevelApp.Tests`. Because `ReplayTests` is added to the same project, no
new step is needed — the existing step picks it up automatically.

However, the CI runner needs access to `docs/sampleProjects/` at test
time. Verify that the `actions/checkout@v4` step checks out the full repo
including `docs/` (it does by default — no change needed).

Optionally, add a step that fails early with a clear message if
`docs/sampleProjects/` contains no `.levelproj` files:

```yaml
- name: Verify sample projects exist
  shell: pwsh
  run: |
    $files = Get-ChildItem docs/sampleProjects -Filter "*.levelproj" -ErrorAction SilentlyContinue
    if ($files.Count -eq 0) {
      Write-Error "No .levelproj files found in docs/sampleProjects/. Add at least one before replay tests can run."
      exit 1
    }
    Write-Host "Found $($files.Count) sample project(s)."
```

---

## Developer workflow: promoting a session to a test

1. Run the app normally and complete a measurement session
2. The session is recorded automatically (verify the log/session path in
   `SettingsService` — this was implemented as part of the action logging
   feature)
3. Open the log viewer (or file location) and identify a session worth
   preserving as a regression test
4. Copy the corresponding `.levelproj` to `docs/sampleProjects/`
5. Give it a descriptive name:
   `surfaceplate_fullgrid_4x3_with_corrections.levelproj`
6. Commit it — the next CI run will include it automatically

Document this workflow in a new section of `CLAUDE.md`:

```markdown
## Replay Tests

Replay tests load every .levelproj in docs/sampleProjects/, run the
appropriate calculator, and assert a valid result. They run automatically
in CI alongside unit tests.

To add a new replay test: copy a .levelproj file to docs/sampleProjects/
with a descriptive name and commit it. No code changes required.
```

---

## What this work package explicitly does NOT do

- Assert specific numeric results (flatness values, residuals) — only
  non-null and non-negative flatness is checked. Numeric regression
  assertions can be added later once a stable reference dataset exists.
- Test the UI or any WinUI 3 code
- Implement the action logging / session recording infrastructure —
  that is assumed to already exist. If it does not, sample projects must
  be created manually for this work package to proceed.

---

## Acceptance criteria

1. `LevelApp.Tests/ReplayTests.cs` exists
2. At least one `.levelproj` exists in `docs/sampleProjects/`
3. `dotnet test LevelApp.Tests` runs all replay tests headlessly and
   passes locally
4. The CI pipeline runs and passes with replay tests included
5. Adding a new `.levelproj` to `docs/sampleProjects/` and pushing
   causes it to be tested automatically with no code changes
6. A `.levelproj` with a deliberate deserialization error causes the
   corresponding test to fail (not silently pass)

---

## Version bump

Set `AppVersion.Minor` → `11`, `AppVersion.Patch` → `0` in `AppVersion.cs`
before committing. Commit message:

```
[v0.11.0] WP0.11: headless replay tests + sample projects in CI
```
