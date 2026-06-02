# Session Prompt — WP0.19

```
I'm building LevelApp — a C# WinUI 3 Windows app for precision level
measurement evaluation. The architecture document is in `docs/architecture.md`
and the work package to implement is in
`docs/workpackages/WP0.19-technical-debt.md`. Please read both before
starting any work.

We are implementing Work Package 0.19 — Technical Debt & Reliability Fixes
(target v0.19.0). Current version is v0.18.1.

This work package contains six independent fixes. Implement them in order,
committing after each one so failures are easy to isolate.

Key things to know before you start:

1. HttpClient timeout (Fix 1): Check whether UpdateService uses a static
   HttpClient or HttpClientFactory before choosing the fix approach. Use
   HttpClientFactory if it is already set up; static timeout otherwise.

2. DeviceRegistry error handling (Fix 2): The backup file should go to
   the same directory as devices.json with a .corrupt extension. The
   LoadError message is shown once on the InstrumentsPage InfoBar —
   check how the existing connection warning InfoBar in MeasurementView
   is implemented and follow the same pattern.

3. Dead interface removal (Fix 3): Run the grep commands specified in
   the work package before deleting anything. Only delete files with
   zero references outside their own definition.

4. App.Services cleanup (Fix 4): Do not remove App.Services itself.
   Only eliminate call sites that can be replaced with constructor
   injection. Comment the ones that genuinely cannot be.

5. IWindowContext (Fix 5): Check whether MainViewModel already has an
   IWindowContext or similar abstraction before introducing a new one.
   If something equivalent exists, adapt it rather than duplicating.

6. UpdaterContract (Fix 6): LevelApp.Updater cannot reference
   LevelApp.Core without a problematic dependency. Use the duplicated
   constants approach with the synchronisation comment as specified.

Do not change any measurement logic, data models, UI layout, or file
format. Do not fix anything not listed in the work package — this is a
targeted cleanup, not a general refactor.

All existing tests must pass after each fix. Add the new reliability
tests specified in the work package before committing Fix 1 and Fix 2.
```
