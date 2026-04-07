\# WP\_01 — Save / Load, Menu Bar \& Settings



> Status: Ready for implementation  

> Author: Lukas Fässler  

> Created: 2026-04-06



\---



\## 1. Overview



This work package implements project persistence (Open, Save, Save As), a menu bar replacing the current title bar buttons, an application settings infrastructure, and proper unsaved-changes handling throughout the app.



\---



\## 2. Menu Bar



Replace the current title bar buttons with a WinUI 3 `MenuBar` with the following structure:



```

File

├── New Project          (always enabled)

├── Open Project...      (always enabled)

├── Save Project         (enabled when project active AND dirty flag is set)

├── Save Project As...   (enabled when project active)

├── ─────────────────

└── Exit



Settings

└── Preferences...       (always enabled)

```



\*\*New Measurement\*\* is NOT in the menu bar. It remains a button in the Results view, enabled only when a project is active (see architecture.md §8).



\---



\## 3. Dirty Flag



Introduce a dirty flag on the active project, managed in the main ViewModel or a dedicated `ProjectService`.



\- \*\*Set\*\* whenever any change is made: new measurement started, reading accepted, correction added, project metadata edited.

\- \*\*Cleared\*\* on successful Save or Save As.

\- \*\*Title bar\*\* reflects dirty state: `LevelApp — SurfacePlate\_1200x800 \*` when dirty, `LevelApp — SurfacePlate\_1200x800` when clean. Shows just `LevelApp` when no project is loaded.



\---



\## 4. Unsaved Changes Prompt



Whenever the user triggers an action that would discard the current project (New Project, Open Project, Exit, window X button), check the dirty flag. If set, show a WinUI 3 `ContentDialog`:



```

Title:   "Unsaved changes"

Body:    "You have unsaved changes to \[project name].

&#x20;         Do you want to save before continuing?"

Buttons: \[ Save ]   \[ Discard ]   \[ Cancel ]

```



\- \*\*Save\*\* — runs Save Project, then proceeds with the original action.

\- \*\*Discard\*\* — proceeds without saving.

\- \*\*Cancel\*\* — dismisses the dialog, returns user to the app. Original action is abandoned.



This check applies to:

\- File → New Project

\- File → Open Project

\- File → Exit

\- Window close button (X)



\---



\## 5. Save Project



Serialises the active `Project` to a `.levelproj` JSON file.



\- If the project has already been saved (file path known), saves in place silently.

\- If the project has never been saved, behaves as Save As (see §6).



\---



\## 6. Save Project As



Opens a `FileSavePicker` with:

\- Suggested filename: auto-generated from project metadata (see §8)

\- File type filter: `.levelproj`

\- Initial directory: default project folder from Settings (see §9), falling back to the user's Documents folder if not set



On confirmation, saves to the chosen path and updates the stored file path for future saves.



\---



\## 7. Open Project



Opens a `FileOpenPicker` with:

\- File type filter: `.levelproj`

\- Initial directory: default project folder from Settings (see §9), falling back to Documents



On confirmation, deserialises the `.levelproj` file and loads the project into the app, navigating to the appropriate view (Results view if measurements exist, otherwise ProjectSetupView).



\---



\## 8. Suggested Filename



When first saving a project, auto-generate a suggested filename from the object definition:



\*\*Format:\*\* `{GeometryType}\_{WidthMm}x{HeightMm}`



\*\*Examples:\*\*

\- `SurfacePlate\_1200x800`

\- `LatheBed\_2000x300`



\*\*Rules:\*\*

\- No spaces

\- No special or non-ASCII characters (normalise accented characters where possible)

\- CamelCase geometry type name, underscore separator, dimensions in mm

\- Grid size (columns × rows) is NOT included — it belongs to the measurement session, not the project



\---



\## 9. Settings Infrastructure



Create a `SettingsService` backed by a `settings.json` file stored in `ApplicationData.Current.LocalFolder`.



\*\*Interface:\*\*

```csharp

public interface ISettingsService

{

&#x20;   string DefaultProjectFolder { get; set; }

&#x20;   void Load();

&#x20;   void Save();

}

```



\*\*Default value:\*\* `Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)`



\*\*Preferences dialog\*\* (opened via Settings → Preferences):

\- WinUI 3 `ContentDialog`

\- Single control: labelled folder path text box + "Browse..." button (opens a `FolderPicker`)

\- Buttons: \[ OK ] \[ Cancel ]

\- OK persists the setting via `SettingsService.Save()`



`SettingsService` should be registered as a singleton and injected where needed (file pickers, future settings consumers).



\---



\## 10. New Measurement (Revised Flow)



When the user clicks "New Measurement" in the Results view:



1\. Show a slim `ContentDialog` with:

&#x20;  - Geometry summary shown \*\*read-only\*\* (type, dimensions) — for reference only, cannot be changed

&#x20;  - Editable: Operator name, Notes, Strategy selector (Full Grid, Union Jack, etc.)

2\. On confirm, create a new `MeasurementSession` on the existing project and navigate to `MeasurementView`

3\. The project geometry is \*\*locked\*\* — changing it requires starting a New Project



\---



\## 11. Files to Create / Modify



| File | Action |

|---|---|

| `App/Services/ISettingsService.cs` | Create |

| `App/Services/SettingsService.cs` | Create |

| `App/Views/MainWindow.xaml` | Modify — add MenuBar, update title binding |

| `App/ViewModels/MainViewModel.cs` | Modify (or create) — dirty flag, title, menu commands |

| `App/Views/ResultsView.xaml` | Modify — remove Save/New buttons, keep New Measurement button |

| `App/Views/Dialogs/PreferencesDialog.xaml` | Create |

| `App/Views/Dialogs/NewMeasurementDialog.xaml` | Create |

| `docs/workpackages/WP\_01\_SaveLoad.md` | Add to repo |



\---



\## 12. Out of Scope for This Work Package



\- Multiple recent files list

\- Auto-save

\- File association (double-click `.levelproj` opens app)

\- Any settings other than default project folder



