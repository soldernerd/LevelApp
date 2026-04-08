# LevelApp — Claude Code Instructions

## Project Summary

LevelApp is a Windows desktop application for evaluating precision electronic level measurements, used in machine tool geometry inspection and granite surface plate qualification. It guides operators through a defined measurement procedure, computes a best-fit surface map using least-squares adjustment, detects suspect readings, and displays results graphically.

Full project details, architecture decisions, data models, and roadmap are in `docs/architecture.md`. Read that document before starting any implementation work.

---

## Technology Stack

- **Language:** C# (.NET 8/9)
- **UI Framework:** WinUI 3 / Windows App SDK
- **UI Pattern:** MVVM
- **Persistence:** JSON via System.Text.Json (`.levelproj` files)
- **IDE:** Visual Studio 2022

---

## Solution Structure

```
LevelApp/
├── LevelApp.sln
├── Core/                  ← No UI dependencies. Fully unit-testable.
│   ├── Models/
│   ├── Interfaces/
│   └── Geometry/
├── Instruments/
├── App/                   ← WinUI 3 application (Views, ViewModels, Services)
└── docs/
    ├── architecture.md
    └── workpackages/      ← One markdown file per work package
```

---

## Versioning

The app uses **semantic versioning** in the form `Major.Minor.Patch` (e.g. `0.2.0`).

### Rules

- **Patch** increments by 1 automatically after every code change. No instruction from the user is needed. Applies to every file except `.levelproj` project data files.
- **Minor** increments by 1 automatically when a work package is fully implemented. Patch resets to 0. No instruction from the user is needed. Applies to every file except `.levelproj` project data files.
- **Major** increments only when explicitly instructed by the user. Resets both Minor and Patch to 0. Applies to every file except `.levelproj` project data files.

### Single source of truth

All version information lives in `LevelApp.Core/AppVersion.cs`:

```csharp
public static class AppVersion
{
    public const int Major = 0;
    public const int Minor = 2;
    public const int Patch = 0;

    public static string Full    => $"{Major}.{Minor}.{Patch}";
    public static string Display => $"v{Full}";
}
```

- **Never** hardcode a version string anywhere else — not in XAML, not in code-behind, not in comments.
- The `.csproj` `<Version>` / `<AssemblyVersion>` / `<FileVersion>` fields are kept in sync with `AppVersion.cs` at all times.
- Bump `AppVersion.cs` and the `.csproj` fields **before** committing so every commit already carries the correct version.
- Version bumps apply to all source files. **Never** bump the version inside `.levelproj` data files — those carry their own `schemaVersion` field which is unrelated.

---

## Git Workflow

### Local commits — automatic, after every code change

After every code change (i.e. every time the version is bumped), stage all relevant modified files and commit to the **master** branch locally. Do this without waiting for instructions from the user.

```
git add <changed files>
git commit -m "[vX.Y.Z] Short imperative description"
```

- Always commit on **master**. Never create or switch to any other branch — there is only one developer.
- The commit message **must** start with `[vX.Y.Z]` where `X.Y.Z` matches the version in `AppVersion.cs` at the time of the commit.

### Commit message format

```
[v{Major}.{Minor}.{Patch}] Short imperative description
```

Examples:
```
[v0.2.0] WP0.02: versioning, AppVersion in project file, About dialog
[v0.2.1] Fix About dialog hyperlinks not opening on first click
[v0.3.0] WP0.03: implement measurement view
```

### Pushing to remote — only when explicitly instructed

**Never push to the remote (GitHub) automatically.** Only push when the user explicitly says to do so (e.g. "push" or "push to GitHub"). Before pushing, verify that:

1. `docs/architecture.md` is up to date with what was actually built.
2. `README.md` reflects any user-facing changes.
3. `docs/levelproj.md` reflects any changes to the project file format.
4. `AppVersion.cs` and all `.csproj` version fields carry the correct version.
5. All changes are committed locally on master.

Then push:

```
git push origin master
```

Remote URL: https://github.com/soldernerd/LevelApp

---

## Work Packages

New features are implemented from work package files located in `docs/workpackages/`. Each file contains a complete, self-contained specification. When given a work package, implement it fully and wait for the user to confirm they are satisfied with the result before doing anything else.

---

## Coding Standards

Follow good industry practice for C# and WinUI 3 development:

- MVVM pattern strictly — no business logic in Views or code-behind
- Depend on interfaces, not concrete implementations
- Keep `Core` free of any UI dependencies
- All new services should have a corresponding interface

---

## After Completing a Work Package

When the user confirms they are satisfied with a completed work package, and only then, perform the following steps **in order**:

1. Update `docs/architecture.md` to reflect what was actually built (correct any deviations from the spec, update the solution structure, interfaces, and data models — do not describe unbuilt features as complete).
2. Update `README.md` if any user-facing functionality has changed.
3. Update `docs/levelproj.md` if anything in the project file format has changed.
4. Bump Minor, reset Patch to 0, update `AppVersion.cs` and all `.csproj` version fields.
5. Commit all changes locally on master with a message following the convention above.

Do **not** push to GitHub unless the user explicitly asks.

---

## Sample Project Files

Sample project files for testing and debugging are located in `docs/sampleProjects/`.

---

## Repository

- **Remote:** https://github.com/soldernerd/LevelApp
- **Branch:** master (only — never create other branches)
- **License:** GPL v3
- **Author:** Lukas Fässler
