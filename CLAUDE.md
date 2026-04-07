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

- **Minor** increments by 1 with every completed work package. Patch resets to 0.
- **Patch** increments by 1 with every round of debugging / bug-fix within a work package.
- **Major** only increments on an explicit decision by the project owner; resets both minor and patch to 0.

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
- The `.csproj` `<Version>` / `<AssemblyVersion>` / `<FileVersion>` fields are kept in sync with this file manually.
- Bump `AppVersion.cs` **before** committing so the delivered commit already carries the correct version.

### Commit message format

```
[v{Major}.{Minor}.{Patch}] Short imperative description
```

Examples:
```
[v0.2.0] WP0.02: versioning, appVersion in project file, About dialog
[v0.2.1] Fix About dialog hyperlinks not opening on first click
[v0.3.0] WP0.03: ...
```

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

Only perform the following steps when **explicitly instructed to do so by the user**. Never do them automatically or when work is still in progress:

1. Update `docs/architecture.md` to reflect what was actually built (correct any deviations from the spec, update the solution structure, interfaces, and data models as needed — do not describe unbuilt features as complete)
2. Update `README.md` if any user-facing functionality has changed
3. Commit all changes and push to GitHub with a commit message following the convention above (e.g. `[v0.2.0] WP0.02: versioning, appVersion in project file, About dialog`)

---

## Sample project files

Sample project files for testing and debugging are located in `docs/sampleProjects/`.

---

## Repository

GitHub: https://github.com/soldernerd/LevelApp  
License: GPL v3  
Author: Lukas Fässler
