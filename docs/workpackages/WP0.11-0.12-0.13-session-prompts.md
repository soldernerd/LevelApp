# Session Prompts — WP0.09, WP0.10, WP0.11

---

## WP0.11 — CI/CD Pipeline

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in `docs/workpackages/WP0.11-ci-pipeline.md`.
Please read both before starting any work.

We are implementing Work Package 0.11 — CI/CD Pipeline (target v0.10.0).

Key things to know before you start:

- The solution file is `LevelApp.slnx` at the repo root.
- The version source of truth is `LevelApp.Core/AppVersion.cs`.
- Unit tests are in `LevelApp.Tests/LevelApp.Tests.csproj`.
- The repo is public on GitHub at https://github.com/soldernerd/LevelApp —
  no secrets need to be configured for GITHUB_TOKEN.
- Before writing the publish step, inspect `LevelApp.App/LevelApp.App.csproj`
  to determine whether the project is framework-dependent or self-contained,
  and adjust the dotnet publish arguments accordingly.
- Do not change any application code, models, or tests — this work package
  adds only `.github/workflows/ci.yml` and a CLAUDE.md update.
```

---

## WP0.12 — Auto-Update

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in `docs/workpackages/WP0.12-auto-update.md`.
Please read both before starting any work.

We are implementing Work Package 0.12 — Auto-Update (target v0.12.0).

Key things to know before you start:

- The solution file is `LevelApp.slnx` at the repo root.
- The version source of truth is `LevelApp.Core/AppVersion.cs`.
- The DI container is set up in `LevelApp.App/App.xaml.cs` — register
  IUpdateService/UpdateService as a singleton there.
- `SettingsService` already persists settings to
  %LOCALAPPDATA%\LevelApp\settings.json — no new persistence file needed.
- The GitHub repo is public at https://github.com/soldernerd/LevelApp —
  the Releases API endpoint is
  https://api.github.com/repos/soldernerd/LevelApp/releases/latest
- The install path at runtime is AppContext.BaseDirectory.
- The updater must copy itself to a temp location before doing any file
  replacement — see the work package for the exact pattern.
- The UAC/elevation limitation (installing to Program Files) is a known
  deferred issue — do not attempt to solve it, just implement the feature
  as specified.
- Do not change any Core models, calculators, or existing tests.
```

---

## WP0.13 — Replay Tests

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in `docs/workpackages/WP0.13-replay-tests.md`.
Please read both before starting any work.

We are implementing Work Package 0.13 — Replay Tests (target v0.13.0).

Key things to know before you start:

- The solution file is `LevelApp.slnx` at the repo root.
- Unit tests use xUnit and are in `LevelApp.Tests/`.
- Sample project files are in `docs/sampleProjects/` — verify they exist
  before writing any test code. If none exist, stop and tell me; I will
  add them before you proceed.
- Before writing ReplayTests.cs, read the actual model classes
  (MeasurementSession, MeasurementRound, CorrectionRound, ObjectDefinition)
  and the factory classes (StrategyFactory, CalculatorFactory,
  ParallelWaysCalculator) to get the exact property and method names right.
  The work package uses illustrative names that may not match exactly.
- The repo root landmark for path resolution is the presence of
  LevelApp.slnx — use that to walk up from the test output folder.
- Do not change any application code, Core models, or the CI YAML beyond
  what is specified (adding the sample-projects verification step).
```
