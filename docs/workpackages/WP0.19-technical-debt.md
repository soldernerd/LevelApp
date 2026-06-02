# Work Package 0.19 — Technical Debt & Reliability Fixes

> Target version: **v0.19.0**
> Prerequisite: v0.18.1 ✓

---

## Goal

Fix the real issues identified in the CONCERNS.md audit. All six items are
small, targeted, and independent of each other. No architectural changes —
this is a cleanup and hardening pass.

---

## Fix 1 — `HttpClient` timeout in `UpdateService`

**File:** `LevelApp.App/Services/UpdateService.cs`

**Problem:** The `HttpClient` used to call the GitHub Releases API has no
timeout. A hung network call at startup blocks indefinitely.

**Fix:** Set an explicit timeout on the `HttpClient` instance:

```csharp
private static readonly HttpClient _httpClient = new()
{
    Timeout = TimeSpan.FromSeconds(10)
};
```

If `HttpClient` is injected via DI rather than static, configure the timeout
on the `HttpClientFactory` registration in `App.xaml.cs`:

```csharp
services.AddHttpClient<IUpdateService, UpdateService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        $"LevelApp/{AppVersion.Full}");
});
```

Prefer the `HttpClientFactory` approach if `UpdateService` is already
registered that way — check before choosing.

---

## Fix 2 — `DeviceRegistry` exception handling

**File:** `LevelApp.App/Services/DeviceRegistry.cs`

**Problem:** A corrupt or unreadable `devices.json` causes silent data loss.
The registry starts empty with no user feedback.

**Fix:** Catch `JsonException` (and `IOException`) explicitly, log the error,
back up the corrupt file, and show a one-time `InfoBar` warning on the
Instruments page:

```csharp
private void Load()
{
    if (!File.Exists(_filePath)) return;

    try
    {
        var json = File.ReadAllText(_filePath);
        _devices = JsonSerializer.Deserialize<List<KnownDevice>>(json)
                   ?? new List<KnownDevice>();
    }
    catch (Exception ex) when (ex is JsonException or IOException)
    {
        // Back up the corrupt file so it can be inspected
        var backup = _filePath + ".corrupt";
        try { File.Copy(_filePath, backup, overwrite: true); } catch { }

        _devices = new List<KnownDevice>();
        _loadError = $"Device registry could not be loaded and has been " +
                     $"reset. Corrupt file backed up to {backup}.";
    }
}

// Expose for the UI to display once
public string? LoadError => _loadError;
private string? _loadError;
```

In `IDeviceRegistry`, add:
```csharp
string? LoadError { get; }
```

In `InstrumentPluginTabViewModel` (or `InstrumentsViewModel`), check
`_deviceRegistry.LoadError` on construction and if non-null, set a
bindable `RegistryWarning` string that the `InstrumentsPage` shows in
an `InfoBar` with `Severity="Warning"`.

---

## Fix 3 — Remove dead interfaces from `LevelApp.Core`

**Problem:** `IGeometryCalculator.cs`, `IGeometryModule.cs`, and
`IResultDisplay.cs` remain in `LevelApp.Core/Interfaces/` from an older
architecture iteration. They are not implemented or referenced anywhere in
the current codebase, which misleads developers about what the active
contracts are.

**Fix:**

1. Confirm each file is truly unreferenced:
   ```
   grep -r "IGeometryCalculator" --include="*.cs" .
   grep -r "IGeometryModule" --include="*.cs" .
   grep -r "IResultDisplay" --include="*.cs" .
   ```
2. If zero results (other than the definition itself): delete all three files.
3. If any references exist: investigate before deleting; do not delete blindly.

Add a comment to `architecture.md` (if not already done by WP0.18.1) noting
that these interfaces were superseded by the plugin architecture in WP0.15.

---

## Fix 4 — `App.Services` static service locator

**File:** `LevelApp.App/App.xaml.cs` and any ViewModel using
`App.Services.GetRequiredService<T>()`

**Problem:** `App.Services` is a `public static IServiceProvider` accessed
globally from ViewModels that cannot receive constructor injection. This
bypasses DI testability guarantees and hides dependencies.

**Fix:** Audit every call site of `App.Services.GetRequiredService<T>()`.
For each one:

- If the ViewModel is constructed via DI (the common case): add the
  dependency as a constructor parameter and remove the static call.
- If the ViewModel is constructed directly by XAML or code-behind where
  DI injection is genuinely impossible: leave the static call but add a
  `// TODO: WP0.19 — cannot inject here due to XAML construction` comment
  so the pattern is not mistaken for deliberate design.

The goal is zero unexplained `App.Services` call sites. Commented ones
are acceptable; silent ones are not.

Do **not** remove `App.Services` itself — it may be needed for the
remaining justified cases. Just eliminate unjustified uses.

---

## Fix 5 — `MainViewModel` UI handle assignment

**Files:** `LevelApp.App/ViewModels/MainViewModel.cs`,
`LevelApp.App/MainWindow.xaml.cs`

**Problem:** `XamlRoot` and `Hwnd` are set on `MainViewModel` from
`MainWindow` code-behind after construction. This ties the ViewModel to
WinUI lifecycle ordering and makes it untestable without a real window.

**Fix:** Introduce a narrow interface for the UI context the ViewModel
actually needs:

```csharp
// LevelApp.App/Services/IWindowContext.cs
public interface IWindowContext
{
    XamlRoot XamlRoot { get; }
    IntPtr Hwnd { get; }
}
```

Implement it in `MainWindow`:

```csharp
// MainWindow.xaml.cs
public sealed partial class MainWindow : Window, IWindowContext
{
    public XamlRoot XamlRoot => Content.XamlRoot;
    public IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);
}
```

Register in DI:
```csharp
services.AddSingleton<IWindowContext>(
    _ => (IWindowContext)App.MainWindow);
```

Inject into `MainViewModel` via constructor:
```csharp
public MainViewModel(IWindowContext windowContext, ...)
{
    _windowContext = windowContext;
}
```

Remove the post-construction property assignments from `MainWindow`
code-behind. `MainViewModel` no longer has public `XamlRoot` or `Hwnd`
setters.

---

## Fix 6 — `UpdaterContract` — document the cross-process argument contract

**Files:** `LevelApp.App/Services/UpdateService.cs`,
`LevelApp.Updater/Program.cs`

**Problem:** The argument contract between `UpdateService` (which launches
the updater) and `LevelApp.Updater` (which consumes the arguments) is
implicit. Version drift between the two could silently break updates.

**Fix:** Create a shared constants file that both projects reference:

```csharp
// LevelApp.Core/Instruments/UpdaterContract.cs
/// <summary>
/// Defines the command-line argument contract between LevelApp.App
/// (UpdateService) and LevelApp.Updater. Both sides must stay in sync.
/// Arguments are positional: zipPath installFolder mainExeName [--from-temp]
/// </summary>
public static class UpdaterContract
{
    public const int ArgZipPath      = 0;
    public const int ArgInstallFolder = 1;
    public const int ArgMainExeName  = 2;
    public const string FromTempFlag = "--from-temp";
    public const int ExpectedArgCount = 3;
}
```

Update both `UpdateService.cs` (where it launches the updater) and
`LevelApp.Updater/Program.cs` (where it parses arguments) to reference
`UpdaterContract` constants instead of positional magic numbers.

Since `LevelApp.Updater` is a standalone executable, it cannot reference
`LevelApp.Core` without creating a dependency chain. In that case, copy
the constants into a local `UpdaterContract.cs` in both projects and add
a comment:

```csharp
// IMPORTANT: This file is duplicated in LevelApp.Core and LevelApp.Updater.
// If you change the argument contract, update both copies and bump the
// version in AppVersion.cs.
```

---

## Unit tests to add

```
UpdateServiceTests.cs
  ✓ CheckForUpdateAsync_ReturnsNull_OnNetworkTimeout
  ✓ CheckForUpdateAsync_ReturnsNull_OnHttpError

DeviceRegistryTests.cs
  ✓ Load_CorruptJson_StartsEmptyAndSetsLoadError
  ✓ Load_CorruptJson_BacksUpCorruptFile
  ✓ Load_MissingFile_StartsEmpty_NoError
```

---

## What this work package explicitly does NOT do

- Fix the display module rendering contract (no shared base class —
  deferred to when a fifth renderer is needed)
- Add ViewModel unit tests (separate future work package)
- Add `ActivityLogger` tests (belongs in WP0.11 scope)
- Change any data models, measurement logic, or file format
- Change any UI layout or behaviour visible to the user

---

## Acceptance criteria

1. `UpdateService._httpClient.Timeout` is set to 10 seconds
2. A corrupt `devices.json` produces a visible warning in the Instruments
   page and does not crash; the corrupt file is backed up
3. `IGeometryCalculator`, `IGeometryModule`, `IResultDisplay` are deleted
   (or confirmed still referenced with a note explaining why)
4. Every `App.Services.GetRequiredService<T>()` call site is either
   removed (replaced with constructor injection) or has an explanatory comment
5. `MainViewModel` no longer has public `XamlRoot` or `Hwnd` setters;
   `IWindowContext` is used instead
6. `UpdaterContract` constants are referenced in both `UpdateService`
   and `LevelApp.Updater/Program.cs`
7. All existing tests pass; new reliability tests pass

---

## Version bump

Set `AppVersion.Minor` → `19`, `AppVersion.Patch` → `0`. Commit message:

```
[v0.19.0] WP0.19: technical debt — HttpClient timeout, DeviceRegistry error
handling, dead interfaces removed, App.Services cleanup, IWindowContext,
UpdaterContract
```
