\# Work Package 0.02 — Versioning \& About



> Target version on completion: \*\*0.2.0\*\*



\---



\## Goals



1\. Introduce a single-source-of-truth version constant (`AppVersion.cs`)

2\. Wire the version into `.csproj` assembly metadata

3\. Include `appVersion` in saved `.levelproj` files

4\. Add a `Help` menu with an \*\*About\*\* dialog

5\. Establish the commit message convention going forward



\---



\## 1. `AppVersion.cs` — single source of truth



Create `LevelApp.Core/AppVersion.cs`:



```csharp

namespace LevelApp.Core;



public static class AppVersion

{

&#x20;   public const int Major = 0;

&#x20;   public const int Minor = 2;

&#x20;   public const int Patch = 0;



&#x20;   public static string Full    => $"{Major}.{Minor}.{Patch}";

&#x20;   public static string Display => $"v{Full}";

}

```



\*\*Rules:\*\*

\- Every other place that needs a version string reads from this class.

\- Never hardcode a version number anywhere else — not in XAML, not in strings, not in comments.

\- Bump `AppVersion.cs` \*\*before\*\* committing a work package or patch so the delivered commit already carries the correct version.



\---



\## 2. Assembly version — `.csproj` metadata



In `LevelApp.App/LevelApp.App.csproj` add (or update) inside `<PropertyGroup>`:



```xml

<Version>0.2.0</Version>

<AssemblyVersion>0.2.0.0</AssemblyVersion>

<FileVersion>0.2.0.0</FileVersion>

```



These values are kept in sync with `AppVersion.cs` manually. They surface in Windows Explorer → file Properties and in crash reports.



\---



\## 3. `.levelproj` file — `appVersion` field



\### Model change



In `LevelApp.Core/Models/Project.cs`, add a nullable string property:



```csharp

public string? AppVersion { get; set; }

```



\### Serialisation



In `ProjectSerializer` (or wherever `Save` is implemented), set this field before serialising:



```csharp

project.AppVersion = Core.AppVersion.Full;   // e.g. "0.2.0"

```



On \*\*load\*\*: read the field as-is. No validation. It is informational only — useful for bug reports. Do not block loading if the field is absent (existing files won't have it).



\### Resulting JSON shape



```json

{

&#x20; "schemaVersion": "1.0",

&#x20; "appVersion": "0.2.0",

&#x20; "project": { ... }

}

```



\---



\## 4. Help menu \& About dialog



\### 4a. Add a `MenuBar`



The app currently has no menu bar. Add a `MenuBar` to the shell / main window, above or integrated into the existing title/toolbar area.



Initial menu structure (minimal — grows in future work packages):



```

Help

&#x20; └── About LevelApp...

```



Future packages will add `File` (Open, Save, Preferences) and possibly `Edit`. Design the layout so those menus slot in naturally to the left of `Help`.



\### 4b. `AboutDialog` — `ContentDialog`



Create `LevelApp.App/Views/Dialogs/AboutDialog.xaml` (+ code-behind or ViewModel binding).



\*\*Content:\*\*



```

LevelApp

{AppVersion.Display}           ← bound, not hardcoded



Copyright © 2026 Lukas Fässler

Licensed under the GNU General Public License v3.0



\[View license]      ← HyperlinkButton → https://www.gnu.org/licenses/gpl-3.0.html

\[View on GitHub]    ← HyperlinkButton → https://github.com/soldernerd/LevelApp

```



Use a standard WinUI 3 `ContentDialog` with a single \*\*Close\*\* button (`CloseButtonText="Close"`).



The version string \*\*must\*\* be read from `AppVersion.Display` at runtime — do not hardcode it in XAML or code-behind.



\---



\## 5. Commit message convention



Format for all future commits:



```

\[v{Major}.{Minor}.{Patch}] Short imperative description

```



Examples:

```

\[v0.2.0] WP0.02: versioning, appVersion in project file, About dialog

\[v0.2.1] Fix About dialog hyperlinks not opening on first click

\[v0.3.0] WP0.03: ...

```



The version tag reflects the state of `AppVersion.cs` \*\*at the time of the commit\*\*.



\---



\## Acceptance Criteria



\- \[ ] `AppVersion.cs` exists in `LevelApp.Core`; no other file contains a hardcoded version string

\- \[ ] `.csproj` `<Version>` matches `AppVersion.Full`

\- \[ ] Saving a project writes `"appVersion": "0.2.0"` into the JSON; loading an old file without that field succeeds without error

\- \[ ] A `Help` menu appears in the app; clicking \*\*About LevelApp...\*\* opens a `ContentDialog`

\- \[ ] The dialog displays the version from `AppVersion.Display`, copyright, license name, and two working hyperlinks

\- \[ ] The dialog closes cleanly with the \*\*Close\*\* button

\- \[ ] All tests still pass



