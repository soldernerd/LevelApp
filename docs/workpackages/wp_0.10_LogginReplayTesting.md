\# WP0.10 — Activity Logging, Session Snapshots \& Replay Testing



> Target version: v0.10.0

> Depends on: WP0.09 (help \& localisation) ✓ complete



\---



\## 1. Objective



Add a self-contained activity logging system that records user interactions to a JSON Lines

file during every app session. Each session captures a snapshot of the open project file(s)

and a recording of instrument readings alongside the log. The resulting session bundle

(`.jsonl` + `.levelproj` + `.instrument`) is a fully reproducible test case that can be

replayed headlessly in the test project to confirm the app does not crash.



Logging is on by default and can be disabled in Preferences. Logs are local only — no

upload or transmission in this work package.



\---



\## 2. Scope



\### 2.1 New files to create



| Path | Purpose |

|---|---|

| `LevelApp.App/Services/IActivityLogger.cs` | Interface for the logging service |

| `LevelApp.App/Services/ActivityLogger.cs` | Singleton implementation; writes `.jsonl` |

| `LevelApp.Tests/Replay/ActivityReplayRunner.cs` | Dispatches log entries to ViewModel calls |

| `LevelApp.Tests/Replay/RecordedInstrumentProvider.cs` | `IInstrumentProvider` replay implementation |

| `LevelApp.Tests/Replay/ReplayTests.cs` | xUnit `\[Theory]` scanning `TestLogs/` |

| `LevelApp.Tests/TestLogs/.gitkeep` | Keeps the folder in source control when empty |



\### 2.2 Files to modify



| File | Change |

|---|---|

| `LevelApp.App/Services/IInstrumentProvider.cs` | Add optional recording hook (see §4.3) |

| `LevelApp.App/App.xaml.cs` | Register `IActivityLogger` in DI; call startup sequence |

| `LevelApp.App/ViewModels/MainViewModel.cs` | Inject `IActivityLogger`; add `Log()` call sites |

| `LevelApp.App/ViewModels/PreferencesViewModel.cs` | Add `ActivityLoggingEnabled` preference property |

| `LevelApp.App/Views/Dialogs/PreferencesDialog.xaml` | Add toggle for activity logging |

| `docs/architecture.md` | Update §2, §3, §12, §14 |



\---



\## 3. Log Format



\### 3.1 File location and naming



```

%LOCALAPPDATA%\\LevelApp\\Logs\\activity\_{yyyyMMdd\_HHmmss}.jsonl

```



A new file is created on each app launch. Files older than 14 days are deleted on startup

before the session header is written.



\### 3.2 Entry schema



Every line is a self-contained JSON object. All entries share these fields:



| Field | Type | Description |

|---|---|---|

| `ts` | string | ISO 8601 local timestamp, millisecond precision: `"2026-04-12T14:03:21.441"` |

| `action` | string | Dot-namespaced action identifier (see §3.3) |

| `detail` | string \\| null | Primary detail value; null if not applicable |



Additional fields may be added per action type (see §3.3). No field may be omitted from

the schema above — use `null` rather than omitting.



\### 3.3 Action vocabulary



\#### Session lifecycle



| Action | Detail | Extra fields |

|---|---|---|

| `Session.Start` | null | `version`: string (e.g. `"0.10.0+20260412"`) |

| `Session.End` | null | — |



\#### File operations



| Action | Detail | Extra fields |

|---|---|---|

| `File.Open` | Full file path | `snapshot`: snapshot filename (e.g. `"activity\_...\_p1.levelproj"`) |

| `File.Save` | Full file path | — |

| `File.SaveAs` | Full file path | — |

| `File.Close` | null | — |

| `File.New` | null | — |



\#### Commands



| Action | Detail | Extra fields |

|---|---|---|

| `Cmd.StartMeasurement` | Session ID (GUID) | — |

| `Cmd.StopMeasurement` | Session ID | — |

| `Cmd.RecalculateFlatness` | null | — |

| `Cmd.ApplyCorrection` | Round index as string | — |

| `Cmd.ExportReport` | null | — |



\#### Input changes



| Action | Detail | Extra fields |

|---|---|---|

| `Input.Changed` | New value as string | `field`: field name |



\#### Errors and crashes



| Action | Detail | Extra fields |

|---|---|---|

| `CRASH` | Exception type + message | `stackTrace`: string |

| `CRASH.UI` | Exception message | `stackTrace`: string |



\*\*Example entries:\*\*



```json

{"ts":"2026-04-12T14:03:21.000","action":"Session.Start","detail":null,"version":"0.10.0+20260412"}

{"ts":"2026-04-12T14:03:22.104","action":"File.Open","detail":"C:\\\\Projects\\\\plate1.levelproj","snapshot":"activity\_20260412\_140321\_p1.levelproj"}

{"ts":"2026-04-12T14:03:25.881","action":"Cmd.StartMeasurement","detail":"a3f1c2d4-...","sessionId":"a3f1c2d4-..."}

{"ts":"2026-04-12T14:03:26.210","action":"Input.Changed","detail":"0.005","field":"Tolerance"}

{"ts":"2026-04-12T14:03:58.003","action":"File.Open","detail":"C:\\\\Projects\\\\plate2.levelproj","snapshot":"activity\_20260412\_140321\_p2.levelproj"}

{"ts":"2026-04-12T14:04:12.771","action":"Session.End","detail":null}

```



\---



\## 4. Implementation Architecture



\### 4.1 `IActivityLogger`



```csharp

// LevelApp.App/Services/IActivityLogger.cs

public interface IActivityLogger

{

&#x20;   void Log(string action, string? detail = null,

&#x20;            Dictionary<string, object>? extra = null);



&#x20;   void AttachProjectSnapshot(string projectPath);

&#x20;   void AttachInstrumentRecording(InstrumentReading reading);



&#x20;   bool IsEnabled { get; set; }

}

```



\### 4.2 `ActivityLogger`



Singleton. Resolved from DI. Constructor creates the log folder and output file, writes

the `Session.Start` entry, and deletes files older than 14 days.



```

%LOCALAPPDATA%\\LevelApp\\Logs\\

├── activity\_20260412\_140321.jsonl

├── activity\_20260412\_140321\_p1.levelproj

├── activity\_20260412\_140321\_p2.levelproj

└── activity\_20260412\_140321.instrument

```



Key implementation rules:



\- All file I/O is protected by a `private readonly object \_lock`.

\- `File.AppendAllText` is used for both `.jsonl` and `.instrument` writes — no buffered

&#x20; streams — so each entry is flushed to disk immediately.

\- `IsEnabled` is read from `IPreferencesService` on construction and can be toggled at

&#x20; runtime. When `false`, `Log()` is a no-op.

\- App version is read once in the constructor from

&#x20; `Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()`.



\*\*Project snapshot logic:\*\*



A private counter `\_snapshotCount` increments on each call to `AttachProjectSnapshot`.

The destination filename is:



```

activity\_{sessionTimestamp}\_p{\_snapshotCount}.levelproj

```



The `File.Open` log entry's `snapshot` field is set to this filename (basename only, not

full path).



\*\*Instrument recording:\*\*



Each `InstrumentReading` passed to `AttachInstrumentRecording` is serialised as a JSON

line and appended to `activity\_{sessionTimestamp}.instrument`. The `.instrument` file is

only created if at least one reading is recorded; do not create an empty file.



\*\*Unhandled exception hooks\*\* (registered in `App.xaml.cs`):



```csharp

AppDomain.CurrentDomain.UnhandledException += (s, e) =>

&#x20;   \_logger.Log("CRASH", e.ExceptionObject?.ToString());



Application.Current.UnhandledException += (s, e) =>

{

&#x20;   \_logger.Log("CRASH.UI", e.Message,

&#x20;       new() { \["stackTrace"] = e.Exception?.StackTrace ?? "" });

&#x20;   e.Handled = true;

};

```



\### 4.3 `IInstrumentProvider` recording hook



Add a single nullable property to the existing interface:



```csharp

// Existing interface — add one property

IActivityLogger? RecordingTarget { get; set; }

```



In each concrete `IInstrumentProvider` implementation, after a successful `ReadAsync`,

call `RecordingTarget?.AttachInstrumentRecording(reading)` if the property is set.

The `RecordingTarget` property is set by the ViewModel when a session starts and cleared

when it ends. This keeps recording opt-in and invisible to providers that predate this

work package.



\### 4.4 Preferences integration



Add `ActivityLoggingEnabled` (bool, default `true`) to `IPreferencesService` and its

implementation. Wire it to `IActivityLogger.IsEnabled` on change. Add a toggle

(`ToggleSwitch`) to `PreferencesDialog.xaml` with an appropriate label.



\---



\## 5. Replay Testing Infrastructure



\### 5.1 `RecordedInstrumentProvider`



```csharp

// LevelApp.Tests/Replay/RecordedInstrumentProvider.cs

public sealed class RecordedInstrumentProvider : IInstrumentProvider

{

&#x20;   private readonly Queue<InstrumentReading> \_readings;



&#x20;   public IActivityLogger? RecordingTarget { get; set; }



&#x20;   public RecordedInstrumentProvider(string instrumentFilePath)

&#x20;   {

&#x20;       \_readings = new Queue<InstrumentReading>(

&#x20;           File.ReadLines(instrumentFilePath)

&#x20;               .Where(l => !string.IsNullOrWhiteSpace(l))

&#x20;               .Select(l => JsonSerializer.Deserialize<InstrumentReading>(l)!));

&#x20;   }



&#x20;   public Task<InstrumentReading> ReadAsync(CancellationToken ct = default)

&#x20;   {

&#x20;       if (\_readings.TryDequeue(out var reading))

&#x20;           return Task.FromResult(reading);

&#x20;       throw new EndOfRecordingException(

&#x20;           "No more recorded readings — log may have been truncated.");

&#x20;   }

}

```



If no `.instrument` file exists alongside the `.jsonl`, the replay runner passes a

`NullInstrumentProvider` instead (stub that throws `NotSupportedException` if called).



\### 5.2 `ActivityReplayRunner`



Reads a `.jsonl` file line by line. For each entry, looks up the `action` field and calls

the appropriate ViewModel method. Stops replay (without failing) if a `CRASH` or

`CRASH.UI` entry is encountered — that entry marks where the session died.



Dispatcher method signature:



```csharp

private async Task DispatchAsync(string action, string? detail,

&#x20;   Dictionary<string, JsonElement> entry)

```



Actions that require a file path (`File.Open`, `File.SaveAs`) resolve the path against

the `TestLogs/` folder first; if a corresponding snapshot (e.g. `\_p1.levelproj`) exists

there, that path is substituted for the original. This makes replays hermetic — no

dependency on the original machine's file system.



Version mismatch warning: read `version` from the `Session.Start` entry; compare to

current assembly version; write to `ITestOutputHelper` if they differ. Do not fail the

test.



Add stub `case` entries with a `// TODO` comment for any action string not yet mapped to

a ViewModel call, so the switch does not throw on unknown actions.



\### 5.3 `ReplayTests`



```csharp

// LevelApp.Tests/Replay/ReplayTests.cs

public class ReplayTests(ITestOutputHelper output)

{

&#x20;   \[Theory]

&#x20;   \[MemberData(nameof(GetLogFiles))]

&#x20;   public async Task ReplayLog\_ShouldNotCrash(string logPath)

&#x20;   {

&#x20;       var vm = new MainViewModel(/\* inject test doubles \*/);

&#x20;       var runner = new ActivityReplayRunner(vm, output);



&#x20;       var ex = await Record.ExceptionAsync(() => runner.ReplayAsync(logPath));



&#x20;       Assert.Null(ex);

&#x20;   }



&#x20;   public static IEnumerable<object\[]> GetLogFiles() =>

&#x20;       Directory.GetFiles(

&#x20;           Path.Combine(AppContext.BaseDirectory, "TestLogs"), "\*.jsonl")

&#x20;       .Select(f => new object\[] { f });

}

```



If `TestLogs/` is empty, `GetLogFiles` returns an empty sequence and no tests run — this

is intentional and should not be treated as a test failure.



\---



\## 6. Implementation Steps for Claude Code



Implement in this order:



\### Step 1 — `IActivityLogger` and `ActivityLogger`

1\. Create `LevelApp.App/Services/IActivityLogger.cs` with the interface from §4.1.

2\. Create `LevelApp.App/Services/ActivityLogger.cs` with the singleton implementation

&#x20;  from §4.2. Hard-code the 14-day retention period as a `private const int LogRetentionDays = 14`.

3\. Register in `App.xaml.cs`:

&#x20;  ```csharp

&#x20;  services.AddSingleton<IActivityLogger, ActivityLogger>();

&#x20;  ```

4\. Resolve `IActivityLogger` and register the two unhandled exception hooks in

&#x20;  `App.xaml.cs` `OnLaunched`.



\### Step 2 — Preferences integration

5\. Add `ActivityLoggingEnabled` to `IPreferencesService` and its implementation,

&#x20;  defaulting to `true`.

6\. In `ActivityLogger` constructor, read the initial value. Subscribe to preference

&#x20;  changes and update `IsEnabled` accordingly.

7\. Add a `ToggleSwitch` for activity logging to `PreferencesDialog.xaml`, bound to

&#x20;  `PreferencesViewModel.ActivityLoggingEnabled`.



\### Step 3 — `IInstrumentProvider` recording hook

8\. Add `IActivityLogger? RecordingTarget { get; set; }` to `IInstrumentProvider`.

9\. In each existing concrete provider, call

&#x20;  `RecordingTarget?.AttachInstrumentRecording(reading)` after each successful read.



\### Step 4 — ViewModel call sites

10\. Inject `IActivityLogger` into `MainViewModel` via constructor.

11\. Add `Log()` calls for every action in the vocabulary table (§3.3) at the appropriate

&#x20;   point in each command or property setter. Use `CultureInfo.InvariantCulture` for all

&#x20;   numeric values written to `detail`.

12\. Set `instrumentProvider.RecordingTarget = \_logger` when a measurement session starts;

&#x20;   clear it (`= null`) when the session ends.



\### Step 5 — Replay infrastructure

13\. Create `LevelApp.Tests/Replay/RecordedInstrumentProvider.cs` (§5.1).

14\. Create `LevelApp.Tests/Replay/ActivityReplayRunner.cs` (§5.2). Add stub `case`

&#x20;   entries for the full action vocabulary with `// TODO` comments where ViewModel calls

&#x20;   are not yet implemented.

15\. Create `LevelApp.Tests/Replay/ReplayTests.cs` (§5.3).

16\. Create `LevelApp.Tests/TestLogs/.gitkeep`.

17\. In `LevelApp.Tests.csproj` ensure `TestLogs/` is copied to the output directory:

&#x20;   ```xml

&#x20;   <None Update="TestLogs\\\*\*">

&#x20;     <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>

&#x20;   </None>

&#x20;   ```



\### Step 6 — Version bump and architecture update

18\. Bump `AppVersion.cs` to `0.10.0`.

19\. Update `docs/architecture.md`:

&#x20;   - §2 Technology Stack: add row for activity logging (`IActivityLogger` / JSON Lines / local only)

&#x20;   - §3 Solution Structure: add new files under `LevelApp.App/Services/` and `LevelApp.Tests/Replay/`

&#x20;   - §12 Roadmap: mark WP0.10 complete

&#x20;   - §14 Open Questions: add note that crash upload / support bundle workflow is deferred



\---



\## 7. Acceptance Criteria



\- \[ ] A `.jsonl` file is created under `%LOCALAPPDATA%\\LevelApp\\Logs\\` on every app launch.

\- \[ ] The first line of every log is a `Session.Start` entry containing the correct app version.

\- \[ ] The last line of a clean session is a `Session.End` entry.

\- \[ ] Opening a project writes a `File.Open` entry and copies the `.levelproj` file to the

&#x20;     log folder with the correct `\_p{n}` suffix.

\- \[ ] Opening a second project in the same session produces a `\_p2.levelproj` snapshot; the

&#x20;     `\_p1.levelproj` snapshot is unchanged.

\- \[ ] All commands and input changes listed in §3.3 produce a log entry.

\- \[ ] Instrument readings are appended to the `.instrument` file when a measurement session

&#x20;     is active; the file is absent when no session was run.

\- \[ ] Disabling activity logging in Preferences stops all writes; re-enabling resumes them

&#x20;     within the same session.

\- \[ ] Log files older than 14 days are deleted on startup.

\- \[ ] `ReplayTests` compiles and passes with an empty `TestLogs/` folder (zero tests, no

&#x20;     failures).

\- \[ ] Placing a `.jsonl` + `.levelproj` pair into `TestLogs/` causes a new test to appear

&#x20;     and pass (manually verified with a sample session bundle).

\- \[ ] No regressions in existing functionality.

\- \[ ] `AppVersion` is `0.10.0`.



\---



\## 8. Notes for Claude Code



\- The current app version is `0.9.0`. Do not assume any earlier work package is pending.

\- `File.AppendAllText` is the required write mechanism for both `.jsonl` and `.instrument`

&#x20; files. Do not use `StreamWriter` with buffering — entries must be flushed to disk

&#x20; immediately so they survive a hard crash.

\- All numeric values written to `detail` or extra fields must use

&#x20; `CultureInfo.InvariantCulture` to ensure the replay runner can parse them regardless of

&#x20; the OS locale.

\- Do not log file paths in any field that would be transmitted outside the local machine.

&#x20; For this work package that constraint is trivially satisfied (logs are local only), but

&#x20; keep it in mind when adding call sites.

\- The `TestLogs/` folder is intentionally committed empty (`.gitkeep`). Real session

&#x20; bundles from crash reproductions are added manually — do not commit any `.jsonl` or

&#x20; `.levelproj` files as part of this work package.

\- `EndOfRecordingException` is a new custom exception class; create it in

&#x20; `LevelApp.Tests/Replay/` alongside `RecordedInstrumentProvider.cs`.

\- Do not introduce any new NuGet packages. `System.Text.Json` is already available via

&#x20; the .NET 8 SDK.

\- Commit message: `\[v0.10.0] WP0.10: activity logging, session snapshots, replay testing`



