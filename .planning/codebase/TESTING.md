# Testing Patterns

**Analysis Date:** 2026-06-02

`LevelApp.Tests` is the single xUnit test project for the solution. It targets `net8.0-windows10.0.19041.0` and references `LevelApp.Core`, `LevelApp.Instruments.Manual`, `LevelApp.Instruments.BLE`, and `LevelApp.Instruments.UsbHid`. `LevelApp.App` is intentionally **not** referenced because WinUI 3 projects cannot be referenced from a plain test project without triggering platform-architecture build errors — this is the primary gap in current test coverage.

---

## Test Framework

**Runner:**
- xUnit 2.5.3
- Config: `LevelApp.Tests/LevelApp.Tests.csproj`
- Target framework: `net8.0-windows10.0.19041.0`

**Coverage collection:**
- `coverlet.collector` 6.0.0 is referenced; no coverage thresholds are enforced

**Run commands:**
```bash
dotnet test LevelApp.Tests/LevelApp.Tests.csproj
dotnet test LevelApp.Tests/LevelApp.Tests.csproj --collect:"XPlat Code Coverage"
```

**Global usings:**
- `using Xunit;` is declared globally in the project file — no per-file import needed for `Assert`, `[Fact]`, `[Theory]`, etc.

---

## Test File Organization

**Location:** All test files live directly in `LevelApp.Tests/`, with three named subdirectories for transport-specific and replay infrastructure:

```
LevelApp.Tests/
├── FullGridStrategyTests.cs
├── UnionJackStrategyTests.cs
├── ParallelWaysStrategyTests.cs
├── SurfacePlateCalculatorTests.cs         (LeastSquaresCalculator)
├── SequentialIntegrationCalculatorTests.cs
├── ParallelWaysCalculatorTests.cs
├── CorrectionRoundTests.cs
├── InstrumentProviderTests.cs
├── PluginArchitectureTests.cs
├── DeviceRegistryTests.cs
├── UpdateServiceTests.cs
├── ProjectReplayTests.cs
├── BLE/
│   ├── BleTransportTests.cs
│   └── BleInstrumentProviderBaseTests.cs
├── UsbHid/
│   ├── UsbHidTransportTests.cs
│   ├── UsbHidDeviceScannerTests.cs
│   └── DfuSessionTests.cs
└── Replay/
    ├── ReplayTests.cs
    ├── ActivityReplayRunner.cs
    ├── IReplayTarget.cs
    ├── RecordedInstrumentProvider.cs
    └── EndOfRecordingException.cs
```

**Namespace convention:** Root test files use `namespace LevelApp.Tests;`. Subdirectory files use `namespace LevelApp.Tests.BLE;` and `namespace LevelApp.Tests.UsbHid;` and `namespace LevelApp.Tests.Replay;`.

**Naming:** `{SubjectClass}Tests` — one test class per subject class, one file per test class.

---

## Test Structure

**Typical pattern — `[Fact]` for single-case, `[Theory]` + `[InlineData]` for parameterized:**

```csharp
public class FullGridStrategyTests
{
    private readonly FullGridStrategy _sut = new();

    // ── Section header ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2, 2,  4)]
    [InlineData(3, 3, 12)]
    [InlineData(8, 5, 67)]
    public void StepCount_MatchesFormula(int cols, int rows, int expected)
    {
        var steps = _sut.GenerateSteps(Def(cols, rows));
        Assert.Equal(expected, steps.Count);
    }

    [Fact]
    public void Indices_AreSequentialFromZero()
    {
        var steps = _sut.GenerateSteps(Def(5, 4));
        for (int i = 0; i < steps.Count; i++)
            Assert.Equal(i, steps[i].Index);
    }
}
```

**SUT field pattern:** Stateless subjects are instantiated once as a `private readonly` field. Classes with external state (e.g., `DeviceRegistry` with a file path, `TestProvider` in BLE tests) are instantiated fresh in each test method or set up via `IDisposable`.

**Teardown via `IDisposable`:** When a test creates temp files or directories, the class implements `IDisposable`:
```csharp
public class DeviceRegistryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    public DeviceRegistryTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
```

---

## Helper / Factory Methods

Every test class that needs non-trivial input data declares private static factory methods in a `// ── Helpers ──` section at the top. These return fully populated domain objects and are named after what they build:

```csharp
// Returns a minimal ObjectDefinition for a SurfacePlate geometry
private static ObjectDefinition Def(int cols, int rows,
    double widthMm = 200.0, double heightMm = 200.0) => new() { ... };

// Returns a CalculationParameters with named defaults
private static CalculationParameters Params(double sigmaThreshold = 2.5,
    bool autoExclude = true) => new() { ... };

// Returns a MeasurementRound whose readings encode a known height field
private static MeasurementRound BuildRound(ObjectDefinition def,
    double[][] trueHeights, int noiseOnStepIndex = -1, double noiseValue = 0.0) { ... }
```

For geometry tests (`ParallelWaysCalculatorTests.cs`, `ParallelWaysStrategyTests.cs`), domain-specific helpers such as `Rail(...)`, `AlongRailTask(...)`, and `BridgeTask(...)` follow the same pattern.

---

## Mocking Strategy

No mocking framework (Moq, NSubstitute, etc.) is used. All test doubles are hand-written:

**Inner sealed classes** — used when the mock needs state, captured calls, or configurable behavior:
```csharp
// In BleInstrumentProviderBaseTests.cs — overrides the protected DoConnectAsync hook
private sealed class TestProvider : BleInstrumentProviderBase
{
    public Func<int, CancellationToken, Task> ConnectBehaviour { get; set; }
        = (_, _) => Task.CompletedTask;
    public int AttemptCount => _attemptCount;
    protected override async Task DoConnectAsync(ulong address, CancellationToken ct) { ... }
    ...
}

// In DfuSessionTests.cs — simulates STM32 DFU USB responses
private sealed class MockUsbTransport : IUsbControlTransport
{
    public int DnloadCount   { get; private set; }
    public int BytesReceived { get; private set; }
    public bool ControlTransferOut(...) { ... }
    public bool ControlTransferIn(...)  { ... }
}

// In UpdateServiceTests.cs — configurable HttpMessageHandler
private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
    : HttpMessageHandler { ... }
```

**Null / stub implementations** — used when behavior is not needed:
```csharp
// In ReplayTests.cs — satisfies IReplayTarget with no members
private sealed class NullReplayTarget : IReplayTarget { }

// In Replay/RecordedInstrumentProvider.cs — NullInstrumentProvider for sessions
// without a recorded instrument file
public sealed class NullInstrumentProvider : IInstrumentProvider { ... }
```

**What to mock:** External I/O (USB, BLE hardware, HTTP, file system), hardware state machines, and async infrastructure.

**What NOT to mock:** Core geometry calculators, strategy classes, serializers, model constructors — test these directly against their real implementations.

---

## Sample Projects and Replay Tests

**Sample `.levelproj` files** (`docs/sampleProjects/`) are the fixtures for integration-level regression testing:

| File | Geometry | Strategy |
|---|---|---|
| `SurfacePlate_FullGrid_5x4.levelproj` | SurfacePlate | FullGrid |
| `SurfacePlate_FullGrid_9x6.levelproj` | SurfacePlate | FullGrid |
| `SurfacePlate_FullGrid_SequentialIntegration.levelproj` | SurfacePlate | FullGrid (SI calc) |
| `SurfacePlate_UnionJack_seg2_None.levelproj` | SurfacePlate | UnionJack (no rings) |
| `SurfacePlate_UnionJack_seg3_Circumference.levelproj` | SurfacePlate | UnionJack (circumference ring) |
| `SurfacePlate_UnionJack_seg4_Full.levelproj` | SurfacePlate | UnionJack (full rings) |
| `ParallelWays.levelproj` | ParallelWays | ParallelWays |
| `ParallelWays_ForwardReturn.levelproj` | ParallelWays | ForwardAndReturn |
| `ParallelWays_IndependentMode.levelproj` | ParallelWays | IndependentThenReconcile |

**`ProjectReplayTests.cs`** (`LevelApp.Tests/ProjectReplayTests.cs`) discovers all `.levelproj` files via `[MemberData(nameof(SampleProjects))]`. For each file it:
1. Deserializes the project with `ProjectSerializer.Deserialize`
2. Re-runs the appropriate calculator (`ParallelWaysCalculator` or `CalculatorFactory.Create(...)`) on the recorded steps
3. Asserts the result is non-null and non-empty

If `docs/sampleProjects/` cannot be found from the test output folder, `SampleProjects()` returns an empty sequence and no tests run — this is intentional.

**Activity replay tests** (`LevelApp.Tests/Replay/ReplayTests.cs`) use `.jsonl` activity log files placed in `LevelApp.Tests/TestLogs/`. The infrastructure (`ActivityReplayRunner`, `RecordedInstrumentProvider`) is in place but most `DispatchAsync` action handlers are stubs (`// TODO`) because `LevelApp.App` ViewModels are not yet injectable into the test project. When `TestLogs/` is empty, no tests run — this is intentional.

---

## Async Testing

Use `async Task` test methods with `await` directly:

```csharp
[Fact]
public async Task ManualEntryProvider_ConnectAsync_IsNoOp()
{
    var provider = new ManualEntryProvider();
    await provider.ConnectAsync();
    Assert.Equal(InstrumentConnectionState.Connected, provider.ConnectionState);
}
```

**Cancellation tests** use `CancellationTokenSource`:
```csharp
[Fact]
public async Task FlashAsync_ThrowsOnCancel()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => session.FlashAsync(firmware, null, cts.Token));
}
```

**Non-deterministic timing tests** (backoff delays) use `Task.Delay` with a generous ceiling rather than fixed sleeps:
```csharp
await Task.WhenAny(tcs.Task, Task.Delay(2_000));
Assert.True(tcs.Task.IsCompleted, "Reconnect never called DoConnectAsync");
```

---

## Exception Testing

```csharp
// Synchronous throws
Assert.Throws<ArgumentException>(() => _sut.GenerateSteps(Def(1, 3)));
Assert.Throws<InvalidOperationException>(() => Calculator().Calculate(round.Steps, def, Params()));

// Async throws — use ThrowsAnyAsync to catch both the exact type and derived types
await Assert.ThrowsAnyAsync<OperationCanceledException>(
    () => provider.ConnectAsync(cts.Token));
await Assert.ThrowsAsync<ObjectDisposedException>(
    () => session.FlashAsync(new byte[1024], null, CancellationToken.None));
```

Use `ThrowsAnyAsync<OperationCanceledException>` rather than `ThrowsAsync<OperationCanceledException>` for cancellation, because both `OperationCanceledException` and `TaskCanceledException` (a subclass) are possible.

---

## Coverage Areas

| Area | Test class(es) | Coverage notes |
|---|---|---|
| FullGrid step generation | `FullGridStrategyTests.cs` | Step count, boustrophedon order, grid bounds, instruction text, validation |
| UnionJack step generation | `UnionJackStrategyTests.cs` | Step count formulas, index sequence, node IDs |
| ParallelWays step generation | `ParallelWaysStrategyTests.cs` | Step count, node ID parsing, single/forward-return/bridge tasks |
| Least-squares calculator | `SurfacePlateCalculatorTests.cs` | Flat surface, slopes, outlier detection, sigma threshold, missing readings, empty input |
| Sequential integration calculator | `SequentialIntegrationCalculatorTests.cs` | Same assertions as LS; exercises the alternative calculation path |
| ParallelWays calculator | `ParallelWaysCalculatorTests.cs` | Flat rails, slopes, parallelism, independent mode, ForwardReturn, bridge, outlier detection |
| Correction round merge | `CorrectionRoundTests.cs` | No replacements, single/multi replacement, field preservation, non-existent index, end-to-end recalculation |
| ManualEntry plugin/provider | `InstrumentProviderTests.cs`, `PluginArchitectureTests.cs` | Connection state, capabilities, null optional capabilities, built-in device |
| DeviceRegistry persistence | `DeviceRegistryTests.cs` | Missing file, corrupt JSON, corrupt file backup |
| BLE transport metadata | `BLE/BleTransportTests.cs` | TransportId, DisplayName, Capabilities flags |
| BLE connection state machine | `BLE/BleInstrumentProviderBaseTests.cs` | Initial state, connect/disconnect, retry/backoff, cancellation, unexpected disconnect |
| USB HID transport metadata | `UsbHid/UsbHidTransportTests.cs` | TransportId, DisplayName, Capabilities flags |
| USB HID scanner | `UsbHid/UsbHidDeviceScannerTests.cs` | Timeout respected, cancellation respected (no real hardware) |
| STM32 DFU session | `UsbHid/DfuSessionTests.cs` | Progress reporting, monotonicity, correct DNLOAD count, empty firmware, cancellation, post-dispose throw |
| UpdateService HTTP contract | `UpdateServiceTests.cs` | Null on timeout, null on HTTP error |
| Full project round-trips | `ProjectReplayTests.cs` | All 9 sample projects deserialized and recalculated without exception |

---

## Coverage Gaps

**LevelApp.App (ViewModels, Services, Navigation) is not tested at all.** The WinUI 3 project cannot be referenced from the test project without platform build errors. Specifically untested:

- `MainViewModel`, `MeasurementViewModel`, `ResultsViewModel`, `CorrectionViewModel`, `ProjectSetupViewModel` — all business logic in these classes
- `ProjectFileService`, `ThemeService`, `SettingsService`, `ActivityLogger`, `LocalisationService`
- Navigation flow (`NavigationService`, `PageKey`, navigation args)
- `UpdateService` — the real implementation is only tested indirectly via `UpdateServiceTests` which mirrors the catch-all pattern, not the GitHub API parsing logic

**Serialization round-trips** — `ProjectSerializer` is exercised implicitly by `ProjectReplayTests` but has no dedicated unit tests for schema migration, edge cases in `ObjectDefinition.Parameters`, or `levelproj` version handling.

**Display modules** (`SurfacePlot3DDisplay`, `MeasurementsGridRenderer`, `StrategyPreviewRenderer`, `ParallelWaysDisplay`) — static rendering classes with no tests; rendering correctness is only validated visually.

**`LevelApp.Updater`** — the standalone updater executable has no test coverage. The copy-to-temp, zip extraction, and relaunch logic are untested.

**Replay infrastructure is incomplete** — `ActivityReplayRunner.DispatchAsync` contains stubs for all `Cmd.*`, `Input.Changed`, and `File.*` actions. The replay harness exists but fires no real ViewModel calls.

---

## Adding New Tests

**For a new Core geometry class:**
1. Create `LevelApp.Tests/{ClassName}Tests.cs` in `namespace LevelApp.Tests;`
2. Declare a `private readonly {ClassName} _sut = new();` field if stateless
3. Add private static factory helpers for `ObjectDefinition`, `CalculationParameters`, etc.
4. Use `[Fact]` for single cases, `[Theory][InlineData(...)]` for parameterized
5. Add a corresponding `.levelproj` in `docs/sampleProjects/` if an end-to-end regression fixture is appropriate

**For a new instrument transport:**
1. Create `LevelApp.Tests/{Transport}/{TransportName}TransportTests.cs`
2. Test `TransportId`, `DisplayName`, and `Capabilities` flags as minimum
3. Mock hardware using an inner sealed class implementing the relevant `IUsbControlTransport` / `IBleTransport` interface

**For a new service in LevelApp.App:**
- Currently blocked by the WinUI 3 reference limitation. Until the project is restructured, extract the logic to be tested into a Core class and test it there.

---

*Testing analysis: 2026-06-02*
