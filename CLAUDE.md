\# LevelApp — Claude Code Instructions



\## Project Summary



LevelApp is a Windows desktop application for evaluating precision electronic level measurements, used in machine tool geometry inspection and granite surface plate qualification. It guides operators through a defined measurement procedure, computes a best-fit surface map using least-squares adjustment, detects suspect readings, and displays results graphically.



Full project details, architecture decisions, data models, and roadmap are in `docs/architecture.md`. Read that document before starting any implementation work.



\---



\## Technology Stack



\- \*\*Language:\*\* C# (.NET 8/9)

\- \*\*UI Framework:\*\* WinUI 3 / Windows App SDK

\- \*\*UI Pattern:\*\* MVVM

\- \*\*Persistence:\*\* JSON via System.Text.Json (`.levelproj` files)

\- \*\*IDE:\*\* Visual Studio 2022



\---



\## Solution Structure



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

&#x20;   ├── architecture.md

&#x20;   └── workpackages/      ← One markdown file per work package

```



\---



\## Work Packages



New features are implemented from work package files located in `docs/workpackages/`. Each file contains a complete, self-contained specification. When given a work package, implement it fully and wait for the user to confirm they are satisfied with the result before doing anything else.



\---



\## Coding Standards



Follow good industry practice for C# and WinUI 3 development:

\- MVVM pattern strictly — no business logic in Views or code-behind

\- Depend on interfaces, not concrete implementations

\- Keep `Core` free of any UI dependencies

\- All new services should have a corresponding interface



\---



\## After Completing a Work Package



Only perform the following steps when \*\*explicitly instructed to do so by the user\*\*. Never do them automatically or when work is still in progress:



1\. Update `docs/architecture.md` to reflect what was actually built (correct any deviations from the spec, update the solution structure, interfaces, and data models as needed — do not describe unbuilt features as complete)

2\. Update `README.md` if any user-facing functionality has changed

3\. Commit all changes and push to GitHub with a meaningful commit message referencing the work package (e.g. `WP\_01: Save/Load, menu bar, settings infrastructure`)



\---


\## Sample project files



Sample project files for testing and debugging are located in `docs/sampleProjects/`.


\---



\## Repository



GitHub: https://github.com/soldernerd/LevelApp  

License: GPL v3  

Author: Lukas Fässler



