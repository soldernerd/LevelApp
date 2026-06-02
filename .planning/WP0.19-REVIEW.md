---
phase: WP0.19
reviewed: 2026-06-02T00:00:00Z
depth: standard
files_reviewed: 21
files_reviewed_list:
  - LevelApp.App/App.xaml.cs
  - LevelApp.App/Services/IWindowContext.cs
  - LevelApp.App/Services/UpdateService.cs
  - LevelApp.App/Services/UpdaterContract.cs
  - LevelApp.App/Services/WindowContext.cs
  - LevelApp.App/ViewModels/InstrumentsViewModel.cs
  - LevelApp.App/ViewModels/MainViewModel.cs
  - LevelApp.App/Views/CorrectionView.xaml.cs
  - LevelApp.App/Views/Dialogs/UpdateDialog.xaml.cs
  - LevelApp.App/Views/InstrumentsPage.xaml.cs
  - LevelApp.App/Views/MeasurementView.xaml.cs
  - LevelApp.App/Views/ProjectSetupView.xaml.cs
  - LevelApp.App/Views/ResultsView.xaml.cs
  - LevelApp.App/MainWindow.xaml.cs
  - LevelApp.Core/AppVersion.cs
  - LevelApp.Core/Instruments/DeviceRegistry.cs
  - LevelApp.Core/Interfaces/IDeviceRegistry.cs
  - LevelApp.Tests/DeviceRegistryTests.cs
  - LevelApp.Tests/UpdateServiceTests.cs
  - LevelApp.Updater/Program.cs
  - LevelApp.Updater/UpdaterContract.cs
findings:
  critical: 2
  warning: 4
  info: 3
  total: 9
status: issues_found
---

# WP0.19: Code Review Report

**Reviewed:** 2026-06-02
**Depth:** standard
**Files Reviewed:** 21
**Status:** issues_found

## Summary

WP0.19 addresses five technical debt items: HttpClient timeout, DeviceRegistry corrupt-file handling, dead interface removal, App.Services call-site cleanup, IWindowContext, and UpdaterContract. The HttpClient fix, DeviceRegistry corrupt-file path, and IWindowContext injection are implemented correctly. However, two critical issues stand out: (1) `ConfirmDiscardChangesAsync` in `MainViewModel` passes a potentially-null `XamlRoot` directly to `ContentDialog.ShowAsync()`, which throws at runtime — the same guard used in `ShowErrorAsync` was not applied here; and (2) the `UpdaterContract` named-constant class defined as part of this work package is entirely unused by its intended caller — `UpdateDialog.xaml.cs` builds the argument string manually, making the contract dead code that provides no actual protection.

---

## Critical Issues

### CR-01: `ConfirmDiscardChangesAsync` passes null `XamlRoot` to `ContentDialog.ShowAsync()`

**File:** `LevelApp.App/ViewModels/MainViewModel.cs:212`

**Issue:** `ConfirmDiscardChangesAsync` assigns `_windowContext.XamlRoot` directly to the `ContentDialog` without checking for null. `XamlRoot` is null until `MainWindow`'s `Loaded` event fires (by design — the `IWindowContext` doc comment explicitly warns callers to guard). WinUI 3 throws `InvalidOperationException` from `ContentDialog.ShowAsync()` when `XamlRoot` is null. The sibling method `ShowErrorAsync` (line 321) correctly guards with `if (_windowContext.XamlRoot is null) return;`, but `ConfirmDiscardChangesAsync` does not.

Practical trigger: the window-close handler (`OnAppWindowClosing`) is attached during `MainWindow` constructor; if a window close event fires before the `Loaded` callback populates `XamlRoot` (narrow timing window, but non-zero on slow machines or during system suspend/resume), `ConfirmDiscardChangesAsync` will throw.

**Fix:**
```csharp
public async Task<bool> ConfirmDiscardChangesAsync()
{
    if (!IsDirty || ActiveProject is null) return true;
    if (_windowContext.XamlRoot is null)   return true; // guard: window not yet loaded

    var dialog = new ContentDialog
    {
        // ...
        XamlRoot = _windowContext.XamlRoot
    };
    // ...
}
```

---

### CR-02: `UpdaterContract` class is dead code — `UpdateDialog` builds arguments manually

**File:** `LevelApp.App/Views/Dialogs/UpdateDialog.xaml.cs:54`
**Also:** `LevelApp.App/Services/UpdaterContract.cs` (entire file)

**Issue:** The work package explicitly introduces `UpdaterContract` as a "named cross-process argument contract" to prevent positional argument drift between the caller (`UpdateDialog`) and the receiver (`LevelApp.Updater/Program.cs`). However, `UpdateDialog.xaml.cs` never references the `UpdaterContract` constants — the argument string is assembled by hand:

```csharp
// Line 54 — positional order is hardcoded, not driven by UpdaterContract:
Arguments = $"\"{zipPath}\" \"{installFolder}\" \"LevelApp.App.exe\""
```

The `UpdaterContract` class in `LevelApp.App/Services/` has zero usages. Only the Updater's copy uses `UpdaterContract`, and only for parsing (not for the caller). If someone reorders the constants in either copy of `UpdaterContract`, the call site in `UpdateDialog` is unaffected — the contract provides no compile-time safety at all.

**Fix:** Use the contract constants to build the argument string so a change to the contract is caught at the call site:

```csharp
// Build arguments in contract-defined order so positional changes cause a compile error.
// UpdaterContract.ArgZipPath=0, ArgInstallFolder=1, ArgMainExeName=2
var argArray = new string[UpdaterContract.ExpectedArgCount];
argArray[UpdaterContract.ArgZipPath]       = $"\"{zipPath}\"";
argArray[UpdaterContract.ArgInstallFolder] = $"\"{installFolder}\"";
argArray[UpdaterContract.ArgMainExeName]   = "\"LevelApp.App.exe\"";

Process.Start(new ProcessStartInfo
{
    FileName        = updaterPath,
    Arguments       = string.Join(" ", argArray),
    UseShellExecute = true
});
```

Alternatively, make `UpdaterContract` expose a static `BuildArguments(string zip, string folder, string exe)` factory method to centralise formatting.

---

## Warnings

### WR-01: Concrete downcast `(WindowContext)` from `IWindowContext` breaks interface substitutability

**File:** `LevelApp.App/MainWindow.xaml.cs:38`

**Issue:** `MainWindow` resolves `IWindowContext` from the DI container then immediately downcasts it to the concrete `WindowContext` type in order to call the setters:

```csharp
var windowCtx = (WindowContext)App.Services.GetRequiredService<IWindowContext>();
windowCtx.Hwnd      = _hwnd;        // line 43
windowCtx.XamlRoot  = RootFrame.XamlRoot;  // line 70
```

`IWindowContext` exposes `XamlRoot` and `Hwnd` as read-only. The setters live only on the concrete class. This means any DI registration of a different `IWindowContext` implementation (e.g., a test double or a future platform variant) will throw `InvalidCastException` at line 38 with no useful diagnostic. The mutation path for the window context leaks the concrete type back through a cast.

**Fix:** Add a mutation method to the interface so the setter is part of the contract:
```csharp
public interface IWindowContext
{
    XamlRoot? XamlRoot { get; }
    nint      Hwnd     { get; }

    /// <summary>Called once by MainWindow after the handles are available.</summary>
    void Initialize(nint hwnd, XamlRoot xamlRoot);
}
```
Or, if setter visibility is the concern, use `internal set` and expose `IWindowContextInternal` for the App layer only.

---

### WR-02: `UpdateDialog` catch-all hides `Process.Start` failure with a misleading message

**File:** `LevelApp.App/Views/Dialogs/UpdateDialog.xaml.cs:61-66`

**Issue:** The single catch block covers the entire `try` body: download, argument construction, `Process.Start`, and `Application.Current.Exit`. If the download succeeds but `Process.Start` throws (e.g., `LevelApp.Updater.exe` not present in the install directory — possible after a partial update or non-standard install), the user is shown "Download failed. Please try again later." The download did not fail; the updater binary is missing. The zip has already been written to `%TEMP%` and is never cleaned up because `File.Delete(zipPath)` runs in the Updater, not here.

**Fix:** Split the catch into at least two regions, or check whether the updater executable exists before starting the download:
```csharp
string zipPath = await _updateService.DownloadUpdateAsync(_update, progress);

StatusText.Text = "Restarting…";
string installFolder = AppContext.BaseDirectory.TrimEnd(
    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
string updaterPath = Path.Combine(AppContext.BaseDirectory, "LevelApp.Updater.exe");

if (!File.Exists(updaterPath))
{
    StatusText.Text = "Update failed: updater binary not found.";
    // restore button state and return
    args.Cancel = true;
    IsPrimaryButtonEnabled = true;
    _downloading = false;
    return;
}
// ... Process.Start ...
```

---

### WR-03: `DeviceRegistry.Save()` silently swallows `IOException` with no feedback

**File:** `LevelApp.Core/Instruments/DeviceRegistry.cs:111`

**Issue:** `Save()` catches `IOException` silently. If the directory cannot be created (`Directory.CreateDirectory` throws) or the file cannot be written (`File.WriteAllText` throws), device registrations made during the session — including the built-in manual device seeded in `App.xaml.cs` — are silently discarded. On next launch, `App.xaml.cs` re-seeds the manual device but any user-registered BLE/USB devices are gone with no warning.

```csharp
catch (IOException) { /* best-effort */ }  // line 111 — data loss, no signal
```

**Fix:** Surface the error via `LoadError` or a separate `SaveError` property so the `InstrumentsViewModel` can show a warning InfoBar (the same mechanism already used for load failures):
```csharp
catch (IOException ex)
{
    SaveError = $"Device registry could not be saved: {ex.Message}";
}
```
At minimum, the catch should not swallow `UnauthorizedAccessException`, which is a distinct signal from a transient `IOException`.

---

### WR-04: Misleading comment — deferral IS completed on the success path

**File:** `LevelApp.App/Views/Dialogs/UpdateDialog.xaml.cs:59`

**Issue:** The comment reads:
```csharp
Application.Current.Exit();
// App exits; deferral is never completed but that is intentional.
```
This is factually incorrect. The `finally` block at line 68 unconditionally calls `deferral.Complete()`, which runs even after `Application.Current.Exit()` because `Application.Current.Exit()` does not immediately terminate the process — it schedules a shutdown. The `finally` fires and completes the deferral before or during teardown. The comment will mislead a future maintainer into thinking the `finally` block is dead on the success path (and might lead to its removal, which would break the error path).

**Fix:**
```csharp
Application.Current.Exit();
// deferral.Complete() is called in the finally block below — this is fine because
// Application.Current.Exit() schedules shutdown rather than immediately terminating.
```

---

## Info

### IN-01: `UpdaterContract` (App-side copy) is unreferenced — static analysis will flag it

**File:** `LevelApp.App/Services/UpdaterContract.cs`

**Issue:** As a consequence of CR-02, the `internal static class UpdaterContract` defined in `LevelApp.App/Services/UpdaterContract.cs` has no usages anywhere in the `LevelApp.App` project. Roslyn / Visual Studio will show this as dead code. The duplication comment says "update both copies if you change any value here", but if the App-side copy is never read, changes to the Updater-side copy cannot be verified by the compiler.

**Fix:** Resolve CR-02 first (use the constants in `UpdateDialog`), which will give this file at least one reference. Once referenced, the "keep in sync" comment has practical enforcement.

---

### IN-02: `Program.cs` mixes `DateTime.Now` (local) and `DateTime.UtcNow` (UTC) for related time operations

**File:** `LevelApp.Updater/Program.cs:14` and `:67`

**Issue:** Log timestamps use `DateTime.Now` (local time) while the shutdown wait deadline uses `DateTime.UtcNow`. These are consistent with their respective purposes, but a future maintainer doing log-based timing analysis across the deadline loop may be confused when the log timestamps don't match the UTC deadline arithmetic. The log also shows `DateTime.Now` on line 21 for the session header.

**Fix:** Use `DateTime.UtcNow` uniformly for all timestamps and format with `"O"` (ISO 8601 with UTC indicator) for unambiguous log entries:
```csharp
void Log(string message)
{
    string line = $"{DateTime.UtcNow:O} {message}";
    // ...
}
```

---

### IN-03: `UpdateServiceTests` test for HTTP error does not set `Timeout`

**File:** `LevelApp.Tests/UpdateServiceTests.cs:35`

**Issue:** The test `CheckForUpdateAsync_ReturnsNull_OnHttpError` creates an `HttpClient` without setting `Timeout`:
```csharp
var client = new HttpClient(handler);   // line 35 — no Timeout
```
The test for the timeout case (line 22) correctly sets `Timeout = TimeSpan.FromSeconds(10)`. The policy being tested by WP0.19 fix 1 is that an explicit timeout is always set. The test that validates the HTTP-error path should mirror the same construction as the real `UpdateService` to demonstrate that the timeout and error handling work together.

**Fix:**
```csharp
var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
```

---

_Reviewed: 2026-06-02_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
