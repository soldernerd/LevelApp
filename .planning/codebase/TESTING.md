# Testing

## Test Project

**`LevelApp.Tests/`** — xUnit-based test project targeting .NET 8.

### NuGet Packages
- `xunit` 2.5.3 — test runner
- `xunit.runner.visualstudio` — VS Test Explorer integration
- `coverlet.collector` — code coverage collection
- No mocking framework (tests use real implementations)

## Directory Structure

```
LevelApp.Tests/
├── BLE/              # BLE transport tests
├── UsbHid/           # USB HID transport tests
├── Replay/           # Project replay regression tests
└── TestLogs/         # Activity log files used by replay tests
```

## Test Categories

### Unit Tests
- Calculator tests (geometry, least-squares, surface fitting)
- Strategy tests (measurement strategy logic)
- Instrument plugin contract tests

### Data-Driven Tests
- `[InlineData]` and `[MemberData]` theory tests for parametric cases
- Floating-point assertions: `Assert.Equal(expected, actual, precision: 9)`

### Integration / Regression Tests
- **`ProjectReplayTests`** — loads real `.levelproj` files from `docs/sampleProjects/` and verifies computed results
- **`ReplayTests`** — replays `.jsonl` activity logs from `TestLogs/` to verify deterministic behavior

## Naming Convention

```
MethodOrProperty_Scenario_ExpectedOutcome
```

Example: `Compute_WithSuspectReading_ExcludesOutlier`

## What Is NOT Tested

- `LevelApp.App` (WinUI 3) — not referenced from the test project; WinUI is not headless-testable
- Views and ViewModels — covered by manual UAT only

## CI Execution

GitHub Actions (`windows-latest`):
```yaml
dotnet test --configuration Release --no-build
```
Test failure blocks the release artifact creation step.

## Coverage Notes

- `LevelApp.Core` has the highest coverage (pure logic, no UI deps)
- `LevelApp.Instruments` has BLE and USB HID transport tests
- Integration tests use sample `.levelproj` files as ground truth
