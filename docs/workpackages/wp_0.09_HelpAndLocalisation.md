# WP0.09 — Contextual Help System & Localisation (en-US / de-DE)

> Target version: v0.9.0
> Depends on: WP0.08 (theme architecture) ✓ complete

---

## 1. Objective

Add a two-tier contextual help system (tooltips + ⓘ flyout buttons) to every view, driven
entirely by `.resw` resource files. Simultaneously introduce German (de-DE) as a full
second language alongside English (en-US). No strings are to be hardcoded in XAML or C#
after this work package.

Teaching Tips (`TeachingTip`) are intentionally deferred to a later work package.

---

## 2. Scope

### 2.1 New files to create

| Path | Purpose |
|---|---|
| `LevelApp.App/Strings/en-US/Resources.resw` | All UI strings, tooltip strings, and help flyout content — English |
| `LevelApp.App/Strings/de-DE/Resources.resw` | Same keys — German |
| `LevelApp.App/Services/ILocalisationService.cs` | Thin interface wrapping `ResourceLoader` |
| `LevelApp.App/Services/LocalisationService.cs` | Implementation; singleton |
| `LevelApp.App/Styles/HelpButtonStyle.xaml` | Style for the ⓘ button; merged in `App.xaml` |

### 2.2 Files to modify

| File | Change |
|---|---|
| `App.xaml` | Merge `HelpButtonStyle.xaml`; register `LocalisationService` in DI |
| `App.xaml.cs` | Register `ILocalisationService` / `LocalisationService` in DI container |
| `Views/ProjectSetupView.xaml` | Replace hardcoded strings with `x:Uid`; add ⓘ buttons |
| `Views/MeasurementView.xaml` | Same |
| `Views/ResultsView.xaml` | Same |
| `Views/CorrectionView.xaml` | Same |
| `Views/Dialogs/PreferencesDialog.xaml` | Same |
| `Views/Dialogs/NewMeasurementDialog.xaml` | Same |
| `Views/Dialogs/RecalculateDialog.xaml` | Same |
| `Views/Dialogs/AboutDialog.xaml` | Same |
| `MainWindow.xaml` | Menu item strings via `x:Uid` |
| `docs/architecture.md` | Update §2 (stack), §3 (structure), §12 (roadmap), §14 (open questions) |

---

## 3. Localisation Architecture

### 3.1 Resource files

Place `.resw` files under `LevelApp.App/Strings/`:

```
LevelApp.App/
└── Strings/
    ├── en-US/
    │   └── Resources.resw
    └── de-DE/
        └── Resources.resw
```

Both files must contain **identical keys**. The Windows resource loader selects the
correct file automatically based on the OS display language. No code change is needed to
switch language.

### 3.2 Naming convention

```
{Element}_{Property}        → e.g.  ProjectName_Header
{Section}_Help_Title        → e.g.  LeastSquares_Help_Title
{Section}_Help_Body         → e.g.  LeastSquares_Help_Body
{Control}.{Property}        → (x:Uid approach) e.g. SaveButton.Content
```

For XAML elements that support `x:Uid`, use the dot-notation form so the XAML framework
applies the property automatically. For programmatic access (ViewModels, code-behind),
use `ILocalisationService.Get(key)`.

### 3.3 `ILocalisationService`

```csharp
// LevelApp.App/Services/ILocalisationService.cs
public interface ILocalisationService
{
    string Get(string key);
}

// LevelApp.App/Services/LocalisationService.cs
public sealed class LocalisationService : ILocalisationService
{
    private readonly ResourceLoader _loader = new ResourceLoader();
    public string Get(string key) => _loader.GetString(key);
}
```

Register in `App.xaml.cs`:
```csharp
services.AddSingleton<ILocalisationService, LocalisationService>();
```

ViewModels that need dynamic strings (e.g. instruction text generation in strategies)
should receive `ILocalisationService` via constructor injection.

### 3.4 `x:Uid` in XAML

For static labels and button text the `x:Uid` mechanism requires no code at all:

```xml
<!-- XAML -->
<TextBlock x:Uid="ProjectName_Header" />

<!-- Resources.resw entry -->
<!-- Name: ProjectName_Header.Text   Value: Project Name -->
```

Note: the `.resw` key for a `TextBlock.Text` property is `{Uid}.Text`.
For `Button.Content` it is `{Uid}.Content`, etc.

---

## 4. Help System Architecture

### 4.1 Tooltip

Every result metric, input label, and section header that has domain meaning gets a
`ToolTip`. Use `ToolTipService.ToolTip` with a string from resources:

```xml
<TextBlock x:Uid="FlatnessLabel">
    <ToolTipService.ToolTip>
        <ToolTip x:Uid="FlatnessLabel_Tooltip" />
    </ToolTipService.ToolTip>
</TextBlock>
```

`.resw` entries:
```
FlatnessLabel.Text                    Flatness (peak-to-valley)
FlatnessLabel_Tooltip.Content         Peak-to-valley height difference across all grid
                                      nodes after the best-fit surface is computed.
```

### 4.2 ⓘ Flyout button

For section headers and any concept that needs a paragraph-length explanation, place a
small ⓘ button immediately to the right of the label. The button opens a `Flyout`
containing a title `TextBlock` and a body `TextBlock`, both from resources.

```xml
<StackPanel Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
    <TextBlock x:Uid="LeastSquares_Header" Style="{StaticResource SectionHeaderStyle}" />
    <Button Style="{StaticResource HelpButtonStyle}">
        <Button.Flyout>
            <Flyout Placement="Bottom">
                <StackPanel MaxWidth="340" Spacing="8">
                    <TextBlock x:Uid="LeastSquares_Help_Title"
                               Style="{StaticResource SubtitleTextBlockStyle}" />
                    <TextBlock x:Uid="LeastSquares_Help_Body"
                               TextWrapping="Wrap"
                               Style="{StaticResource BodyTextBlockStyle}" />
                </StackPanel>
            </Flyout>
        </Button.Flyout>
    </Button>
</StackPanel>
```

### 4.3 `HelpButtonStyle`

Create `LevelApp.App/Styles/HelpButtonStyle.xaml` and merge it in `App.xaml`:

```xml
<Style x:Key="HelpButtonStyle" TargetType="Button">
    <Setter Property="Content" Value="&#xE946;" />   <!-- Segoe MDL2 Info icon -->
    <Setter Property="FontFamily" Value="Segoe MDL2 Assets" />
    <Setter Property="FontSize" Value="14" />
    <Setter Property="Width" Value="24" />
    <Setter Property="Height" Value="24" />
    <Setter Property="Padding" Value="0" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="AutomationProperties.Name" Value="Help" />
</Style>
```

---

## 5. Complete Resource String Tables

### Legend
- **Tooltip** = short one-liner for `ToolTipService.ToolTip`
- **Help Title** = flyout title
- **Help Body** = flyout paragraph

---

### 5.1 en-US/Resources.resw — full content

#### General / Shell

| Key | English value |
|---|---|
| `AppTitle.Text` | LevelApp |
| `Menu_File.Title` | File |
| `Menu_File_OpenProject.Text` | Open Project… |
| `Menu_File_SaveProject.Text` | Save Project |
| `Menu_File_SaveProjectAs.Text` | Save Project As… |
| `Menu_Edit.Title` | Edit |
| `Menu_Edit_Preferences.Text` | Preferences… |
| `Menu_Help.Title` | Help |
| `Menu_Help_About.Text` | About LevelApp… |

#### ProjectSetupView

| Key | English value |
|---|---|
| `ProjectSetup_PageTitle.Text` | Project |
| `ProjectSetup_OpenProject.Content` | Open Project… |
| `ProjectInfo_SectionHeader.Text` | Project Information |
| `ProjectName_Label.Text` | Project name |
| `ProjectName_Label_Tooltip.Content` | A human-readable name for this project. Stored in the .levelproj file. |
| `Operator_Label.Text` | Operator |
| `Operator_Label_Tooltip.Content` | Name of the person performing the measurement. Recorded in the project file for traceability. |
| `Notes_Label.Text` | Notes |
| `Notes_Label_Tooltip.Content` | Free-text notes about this measurement or the object under test. |
| `Object_SectionHeader.Text` | Object |
| `GeometryType_Label.Text` | Geometry type |
| `GeometryType_Label_Tooltip.Content` | The type of object being measured. Each geometry type uses a different measurement strategy and calculation method. |
| `GeometryType_SurfacePlate.Text` | Surface Plate |
| `GeometryType_ParallelWays.Text` | Parallel Ways |
| `Width_Label.Text` | Width (mm) |
| `Width_Label_Tooltip.Content` | Physical width of the surface plate in millimetres. Used to compute the step length between grid nodes. |
| `Height_Label.Text` | Height (mm) |
| `Height_Label_Tooltip.Content` | Physical height (depth) of the surface plate in millimetres. |
| `Columns_Label.Text` | Columns |
| `Columns_Label_Tooltip.Content` | Number of grid columns (nodes in the X direction). More columns increase measurement redundancy and resolution, but also the total number of steps. |
| `Rows_Label.Text` | Rows |
| `Rows_Label_Tooltip.Content` | Number of grid rows (nodes in the Y direction). |
| `Strategy_SectionHeader.Text` | Measurement Strategy |
| `Strategy_Label.Text` | Strategy |
| `Strategy_Label_Tooltip.Content` | The traversal pattern used to visit all measurement points. Different strategies offer different levels of redundancy. |
| `Strategy_FullGrid.Text` | Full Grid (boustrophedon) |
| `Strategy_UnionJack.Text` | Union Jack |
| `Strategy_ParallelWays.Text` | Parallel Ways |
| `StepCount_Label.Text` | Steps |
| `StepCount_Label_Tooltip.Content` | Total number of individual level readings required for this configuration. |
| `StartMeasurement_Button.Content` | Start Measurement |
| `Strategy_Help_Title.Text` | Measurement Strategy |
| `Strategy_Help_Body.Text` | A measurement strategy defines the order and direction in which the instrument visits each grid point. The Full Grid strategy traverses all rows first, then all columns, so each interior node is visited twice from independent directions. This redundancy is what makes the least-squares calculation possible. The Union Jack strategy adds diagonal traversals for higher redundancy. |
| `FullGrid_Help_Title.Text` | Full Grid (boustrophedon) |
| `FullGrid_Help_Body.Text` | The instrument traverses all rows in alternating directions (boustrophedon = "as the ox ploughs"), then all columns in alternating directions. Every interior grid point is visited once during the row pass and once during the column pass. The two readings at each crossing point should agree — any difference is a closure error distributed by the solver. |
| `UnionJack_Help_Title.Text` | Union Jack |
| `UnionJack_Help_Body.Text` | Eight arms radiate from the centre node in the four cardinal (N, S, E, W) and four diagonal (NE, SE, SW, NW) directions. The Full variant adds a circumference ring connecting all arm tips, creating closed loops that the solver uses to detect and distribute closure errors. Higher redundancy than Full Grid, but requires more steps. |

#### MeasurementView

| Key | English value |
|---|---|
| `Measurement_ProgressTitle.Text` | Step {0} of {1} |
| `Measurement_OrientationLabel.Text` | Instrument orientation |
| `Measurement_ReadingLabel.Text` | Reading (mm/m) |
| `Measurement_AcceptButton.Content` | Accept Reading |
| `Measurement_SkipButton.Content` | Skip |
| `Measurement_CancelButton.Content` | Cancel |
| `Measurement_Calculating.Text` | Calculating… |
| `Reading_Help_Title.Text` | Reading (mm/m) |
| `Reading_Help_Body.Text` | Enter the value shown on the electronic level display. The unit is millimetres per metre (mm/m), also written as mm/1000 mm. A positive value means the instrument is tilted so the high end points in the direction of travel. A zero reading means the instrument is perfectly level on that span. |
| `Orientation_Help_Title.Text` | Instrument Orientation |
| `Orientation_Help_Body.Text` | The arrow shows which direction the instrument must face during this step. "East" means the instrument body points to the right; "North" points upward on the grid map shown on screen. The orientation is critical — reversing it by 180° inverts the sign of the reading and introduces a systematic error. |

#### ResultsView

| Key | English value |
|---|---|
| `Results_PageTitle.Text` | Results |
| `Results_SaveProject.Content` | Save Project |
| `Results_NewMeasurement.Content` | New Measurement |
| `Results_StartCorrection.Content` | Start Correction Session |
| `Results_NoFlaggedSteps.Text` | No flagged steps |
| `Results_FlaggedSteps_Header.Text` | Flagged Steps |
| `Flatness_Label.Text` | Flatness (peak-to-valley) |
| `Flatness_Label_Tooltip.Content` | The peak-to-valley height difference across all grid nodes: the range from the lowest to the highest point on the best-fit surface. This is the primary flatness metric for surface plate qualification. |
| `FlatnessValue_Tooltip.Content` | Flatness in micrometres (µm). |
| `ResidualRMS_Label.Text` | Residual RMS |
| `ResidualRMS_Label_Tooltip.Content` | Root mean square of all per-step residuals after the least-squares fit. Indicates overall measurement noise level. |
| `SigmaValue_Tooltip.Content` | σ = residual RMS in micrometres. Smaller is better; values over ~50 µm may indicate instrument settle time issues or a very rough surface. |
| `Flatness_Help_Title.Text` | Flatness (peak-to-valley) |
| `Flatness_Help_Body.Text` | After solving the overdetermined system of equations, every grid node has a computed height. Flatness is simply the range: maximum node height minus minimum node height, expressed in micrometres. This is the standard quantity reported in surface plate calibration certificates (e.g. DIN EN ISO 8512). |
| `ResidualRMS_Help_Title.Text` | Residual RMS (σ) |
| `ResidualRMS_Help_Body.Text` | Each measurement step contributes one equation to the least-squares system. After solving, the residual for a step is the difference between the reading and the height difference implied by the fitted surface. σ is the root mean square of all residuals, corrected for degrees of freedom (DOF = steps − nodes + 1). A large σ suggests noisy readings or an instrument that had not thermally settled. |
| `LeastSquares_Help_Title.Text` | Least-Squares Adjustment |
| `LeastSquares_Help_Body.Text` | With a full grid, every interior node appears in two independent measurement lines: one row pass and one column pass. In theory the computed heights at each crossing must agree; in practice they differ slightly due to instrument noise and thermal drift. Simple sequential integration lets these closure errors accumulate. Least-squares distributes them optimally across all steps simultaneously, producing the statistically best estimate of the true surface shape. |
| `FlaggedSteps_Help_Title.Text` | Flagged Steps |
| `FlaggedSteps_Help_Body.Text` | After the least-squares fit, each step's residual is compared to σ. Any step whose residual exceeds k × σ (default k = 2.5, adjustable in Recalculate settings) is flagged as a suspect reading. Likely causes: instrument not fully settled, accidental knock, bubble not centred, or the plate surface has a genuine local defect. A correction session lets you re-measure only the flagged steps and recompute the result. |
| `SigmaThreshold_Help_Title.Text` | Sigma Threshold (k) |
| `SigmaThreshold_Help_Body.Text` | The outlier detection threshold expressed as a multiple of σ. A step is flagged when |residual| > k × σ. The default value of 2.5 is a standard choice that flags roughly the worst 1 % of readings in a Gaussian noise distribution. Lowering k flags more steps; raising it flags fewer. |
| `CorrectionRound_Help_Title.Text` | Correction Round |
| `CorrectionRound_Help_Body.Text` | A correction round re-measures only the flagged steps and stores the new readings alongside the originals — originals are never overwritten. After all flagged steps are re-measured, the full least-squares calculation is repeated on the merged dataset (original readings with the replacements applied). The process can repeat until no steps are flagged or the operator accepts the result. The complete history of all rounds is preserved in the project file. |

#### CorrectionView

| Key | English value |
|---|---|
| `Correction_PageTitle.Text` | Correction |
| `Correction_ProgressTitle.Text` | Flagged step {0} of {1} |
| `Correction_OriginalReading_Label.Text` | Original reading |
| `Correction_OriginalReading_Tooltip.Content` | The reading recorded during the initial measurement round. Compare this to your new reading — a large difference confirms the original was suspect. |
| `Correction_ReadingLabel.Text` | New reading (mm/m) |
| `Correction_AcceptButton.Content` | Accept Reading |
| `Correction_CancelButton.Content` | Cancel Correction |

#### RecalculateDialog

| Key | English value |
|---|---|
| `Recalculate_Title.Text` | Recalculate |
| `Recalculate_Method_Label.Text` | Calculation method |
| `Recalculate_Method_Tooltip.Content` | Choose the algorithm used to compute node heights from the step readings. |
| `Recalculate_Method_LeastSquares.Text` | Least-Squares (recommended) |
| `Recalculate_Method_Sequential.Text` | Sequential Integration |
| `Recalculate_SigmaThreshold_Label.Text` | Sigma threshold (k) |
| `Recalculate_AutoExclude_Label.Text` | Auto-exclude outliers |
| `Recalculate_AutoExclude_Tooltip.Content` | When enabled, flagged steps are automatically excluded from the least-squares system and do not influence the computed surface. When disabled, they remain in the system but are still reported. |
| `Recalculate_SaveResult_Label.Text` | Save result to project |
| `Recalculate_OkButton.Content` | Recalculate |
| `Recalculate_CancelButton.Content` | Cancel |
| `CalcMethod_Help_Title.Text` | Calculation Method |
| `CalcMethod_Help_Body.Text` | Least-Squares is the recommended method. It solves the overdetermined system of step equations simultaneously, distributing closure errors optimally. Sequential Integration is a simpler method that integrates each traversal line independently and applies a proportional correction at each crossing point — faster to understand, but less accurate when there is significant instrument noise. |
| `SeqIntegration_Help_Title.Text` | Sequential Integration |
| `SeqIntegration_Help_Body.Text` | Each row and column traversal is integrated independently to produce a height profile. Where a row profile and a column profile cross at an interior node, the two heights disagree by a closure error. Sequential integration distributes this error proportionally along both traversals. The method is straightforward but allows errors to accumulate, especially on larger grids. Least-squares is preferred for production measurements. |
| `LinearDrift_Help_Title.Text` | Linear Drift Correction |
| `LinearDrift_Help_Body.Text` | Electronic levels can drift slowly during a long measurement session due to thermal changes in the instrument. Linear drift correction assumes that any systematic change in the readings during a single traversal increases linearly with time (or step count) and subtracts this trend before computing node heights. Enable this if you observe that residuals tend to increase toward the end of each traversal pass. |

#### PreferencesDialog

| Key | English value |
|---|---|
| `Preferences_Title.Text` | Preferences |
| `Preferences_Theme_Label.Text` | Appearance |
| `Preferences_Theme_System.Content` | Follow system |
| `Preferences_Theme_Light.Content` | Light |
| `Preferences_Theme_Dark.Content` | Dark |
| `Preferences_DefaultFolder_Label.Text` | Default project folder |
| `Preferences_DefaultFolder_Tooltip.Content` | The folder that the Open and Save dialogs open to by default. |
| `Preferences_BrowseFolder.Content` | Browse… |
| `Preferences_OkButton.Content` | OK |
| `Preferences_CancelButton.Content` | Cancel |

#### AboutDialog

| Key | English value |
|---|---|
| `About_Title.Text` | About LevelApp |
| `About_Description.Text` | Precision electronic level measurement evaluation for machine tool geometry inspection and surface plate qualification. |
| `About_License.Text` | MIT License |
| `About_GitHub.Content` | View on GitHub |
| `About_CloseButton.Content` | Close |

#### NewMeasurementDialog

| Key | English value |
|---|---|
| `NewMeasurement_Title.Text` | New Measurement |
| `NewMeasurement_Label_Label.Text` | Label |
| `NewMeasurement_Operator_Label.Text` | Operator |
| `NewMeasurement_Notes_Label.Text` | Notes |
| `NewMeasurement_OkButton.Content` | Start |
| `NewMeasurement_CancelButton.Content` | Cancel |

#### Parallel Ways specific strings

| Key | English value |
|---|---|
| `PW_Rails_SectionHeader.Text` | Rails |
| `PW_AddRail_Button.Content` | Add Rail |
| `PW_RailLabel_Label.Text` | Label |
| `PW_RailLength_Label.Text` | Length (mm) |
| `PW_RailLength_Tooltip.Content` | Physical length of this rail in millimetres. Used to compute the station spacing. |
| `PW_RailSeparation_Label.Text` | Lateral separation (mm) |
| `PW_RailSeparation_Tooltip.Content` | Distance between this rail and the reference rail, measured perpendicular to the direction of travel. |
| `PW_Tasks_SectionHeader.Text` | Tasks |
| `PW_AddTask_Button.Content` | Add Task |
| `PW_TaskType_AlongRail.Text` | Along Rail |
| `PW_TaskType_Bridge.Text` | Bridge |
| `PW_Orientation_Label.Text` | Orientation |
| `PW_Orientation_Tooltip.Content` | The physical direction in which the rails run. Used for display purposes and to label step instructions. |
| `PW_DriftCorrection_Label.Text` | Drift correction |
| `PW_DriftCorrection_Tooltip.Content` | Applies a linear correction to compensate for slow instrument drift during a long traversal. |
| `PW_SolverMode_Label.Text` | Solver mode |
| `PW_ReferenceRail_Label.Text` | Reference rail |
| `PW_ReferenceRail_Tooltip.Content` | The rail whose first station is used as the height datum (height = 0). All other heights are expressed relative to this point. |
| `PW_Straightness_Label.Text` | Straightness (peak-to-valley) |
| `PW_Straightness_Tooltip.Content` | Peak-to-valley height deviation of this rail after removing the best-fit straight line. This is the standard straightness metric. |
| `PW_Parallelism_Label.Text` | Parallelism (peak-to-valley) |
| `PW_Parallelism_Tooltip.Content` | Peak-to-valley variation of the height difference between two rails along their common stations. A perfectly parallel pair of rails has a parallelism value of zero. |
| `PW_Straightness_Help_Title.Text` | Straightness |
| `PW_Straightness_Help_Body.Text` | Straightness measures how much a rail deviates from a perfect straight line. First, all station heights are computed by integrating the level readings along the rail. Then, the best-fit straight line through all station heights is subtracted. The remaining deviations are the straightness profile; the peak-to-valley range of this profile is the reported straightness value. |
| `PW_Parallelism_Help_Title.Text` | Parallelism |
| `PW_Parallelism_Help_Body.Text` | Parallelism is the variation in height difference between two rails at their common station positions. At each station the height of rail B minus the height of rail A is computed. If the rails were perfectly parallel, this difference would be constant along the full length. The peak-to-valley range of these differences is the parallelism value. |
| `PW_SolverMode_Help_Title.Text` | Solver Mode |
| `PW_SolverMode_Help_Body.Text` | Global Least-Squares solves all rail and bridge readings simultaneously in one joint system, producing the statistically optimal result when bridge readings are available. Independent then Reconcile solves each rail independently first, then uses a secondary step to align the independent rail heights using the bridge readings. Both modes produce the same result when bridge readings are noise-free; Global Least-Squares handles noisy bridge readings more robustly. |

---

### 5.2 de-DE/Resources.resw — full content

#### General / Shell

| Key | German value |
|---|---|
| `AppTitle.Text` | LevelApp |
| `Menu_File.Title` | Datei |
| `Menu_File_OpenProject.Text` | Projekt öffnen… |
| `Menu_File_SaveProject.Text` | Projekt speichern |
| `Menu_File_SaveProjectAs.Text` | Projekt speichern unter… |
| `Menu_Edit.Title` | Bearbeiten |
| `Menu_Edit_Preferences.Text` | Einstellungen… |
| `Menu_Help.Title` | Hilfe |
| `Menu_Help_About.Text` | Über LevelApp… |

#### ProjectSetupView

| Key | German value |
|---|---|
| `ProjectSetup_PageTitle.Text` | Projekt |
| `ProjectSetup_OpenProject.Content` | Projekt öffnen… |
| `ProjectInfo_SectionHeader.Text` | Projektinformationen |
| `ProjectName_Label.Text` | Projektname |
| `ProjectName_Label_Tooltip.Content` | Ein lesbarer Name für dieses Projekt. Wird in der .levelproj-Datei gespeichert. |
| `Operator_Label.Text` | Bediener |
| `Operator_Label_Tooltip.Content` | Name der Person, die die Messung durchführt. Wird für die Rückverfolgbarkeit gespeichert. |
| `Notes_Label.Text` | Anmerkungen |
| `Notes_Label_Tooltip.Content` | Freitext-Anmerkungen zu dieser Messung oder dem Messobjekt. |
| `Object_SectionHeader.Text` | Objekt |
| `GeometryType_Label.Text` | Geometrietyp |
| `GeometryType_Label_Tooltip.Content` | Der Typ des Messobjekts. Jeder Geometrietyp verwendet eine andere Messstrategie und Berechnungsmethode. |
| `GeometryType_SurfacePlate.Text` | Tuschierplatte |
| `GeometryType_ParallelWays.Text` | Parallele Führungsbahnen |
| `Width_Label.Text` | Breite (mm) |
| `Width_Label_Tooltip.Content` | Physische Breite der Tuschierplatte in Millimetern. Wird zur Berechnung der Schrittweite zwischen Gitterpunkten verwendet. |
| `Height_Label.Text` | Höhe (mm) |
| `Height_Label_Tooltip.Content` | Physische Tiefe (Höhe) der Tuschierplatte in Millimetern. |
| `Columns_Label.Text` | Spalten |
| `Columns_Label_Tooltip.Content` | Anzahl der Gitterspalten (Knoten in X-Richtung). Mehr Spalten erhöhen die Redundanz und Auflösung, aber auch die Gesamtzahl der Schritte. |
| `Rows_Label.Text` | Zeilen |
| `Rows_Label_Tooltip.Content` | Anzahl der Gitterzeilen (Knoten in Y-Richtung). |
| `Strategy_SectionHeader.Text` | Messstrategie |
| `Strategy_Label.Text` | Strategie |
| `Strategy_Label_Tooltip.Content` | Das Traversierungsmuster, das verwendet wird, um alle Messpunkte zu besuchen. Verschiedene Strategien bieten unterschiedliche Redundanzniveaus. |
| `Strategy_FullGrid.Text` | Vollraster (Boustrophedon) |
| `Strategy_UnionJack.Text` | Union Jack |
| `Strategy_ParallelWays.Text` | Parallele Führungsbahnen |
| `StepCount_Label.Text` | Schritte |
| `StepCount_Label_Tooltip.Content` | Gesamtzahl der für diese Konfiguration erforderlichen Einzelmessungen. |
| `StartMeasurement_Button.Content` | Messung starten |
| `Strategy_Help_Title.Text` | Messstrategie |
| `Strategy_Help_Body.Text` | Eine Messstrategie legt fest, in welcher Reihenfolge und Richtung das Instrument jeden Gitterpunkt ansteuert. Bei der Vollrasterstrategie werden zunächst alle Zeilen, dann alle Spalten traversiert, sodass jeder innere Knoten zweimal aus unabhängigen Richtungen besucht wird. Diese Redundanz ermöglicht die Kleinste-Quadrate-Berechnung. Die Union-Jack-Strategie fügt diagonale Traversierungen für höhere Redundanz hinzu. |
| `FullGrid_Help_Title.Text` | Vollraster (Boustrophedon) |
| `FullGrid_Help_Body.Text` | Das Instrument traversiert alle Zeilen in wechselnden Richtungen (Boustrophedon = „wie der Ochse pflügt"), dann alle Spalten in wechselnden Richtungen. Jeder innere Gitterpunkt wird einmal während des Zeilendurchlaufs und einmal während des Spaltendurchlaufs besucht. Die beiden Messwerte an jedem Kreuzungspunkt sollten übereinstimmen – jede Abweichung ist ein Schließungsfehler, der vom Solver verteilt wird. |
| `UnionJack_Help_Title.Text` | Union Jack |
| `UnionJack_Help_Body.Text` | Acht Arme strahlen vom Mittelpunkt in die vier Hauptrichtungen (N, S, O, W) und vier Diagonalrichtungen (NO, SO, SW, NW) aus. Die Vollvariante fügt einen Umfangsring hinzu, der alle Armspitzen verbindet und geschlossene Schleifen erzeugt, die der Solver zur Erkennung und Verteilung von Schließungsfehlern nutzt. Höhere Redundanz als Vollraster, aber mehr Schritte erforderlich. |

#### MeasurementView

| Key | German value |
|---|---|
| `Measurement_ProgressTitle.Text` | Schritt {0} von {1} |
| `Measurement_OrientationLabel.Text` | Instrumentenausrichtung |
| `Measurement_ReadingLabel.Text` | Messwert (mm/m) |
| `Measurement_AcceptButton.Content` | Messwert übernehmen |
| `Measurement_SkipButton.Content` | Überspringen |
| `Measurement_CancelButton.Content` | Abbrechen |
| `Measurement_Calculating.Text` | Berechnung läuft… |
| `Reading_Help_Title.Text` | Messwert (mm/m) |
| `Reading_Help_Body.Text` | Geben Sie den auf dem elektronischen Nivellierdisplay angezeigten Wert ein. Die Einheit ist Millimeter pro Meter (mm/m), auch als mm/1000 mm geschrieben. Ein positiver Wert bedeutet, dass das Instrument so geneigt ist, dass das hohe Ende in Fahrtrichtung zeigt. Ein Nullwert bedeutet, dass das Instrument auf dieser Strecke vollkommen waagerecht ist. |
| `Orientation_Help_Title.Text` | Instrumentenausrichtung |
| `Orientation_Help_Body.Text` | Der Pfeil zeigt die Richtung, in die das Instrument während dieses Schritts zeigen muss. „Ost" bedeutet, dass das Instrumentengehäuse nach rechts zeigt; „Nord" zeigt auf der Gittertafel auf dem Bildschirm nach oben. Die Ausrichtung ist entscheidend – eine Umkehrung um 180° kehrt das Vorzeichen des Messwerts um und führt zu einem systematischen Fehler. |

#### ResultsView

| Key | German value |
|---|---|
| `Results_PageTitle.Text` | Ergebnisse |
| `Results_SaveProject.Content` | Projekt speichern |
| `Results_NewMeasurement.Content` | Neue Messung |
| `Results_StartCorrection.Content` | Korrektursitzung starten |
| `Results_NoFlaggedSteps.Text` | Keine markierten Schritte |
| `Results_FlaggedSteps_Header.Text` | Markierte Schritte |
| `Flatness_Label.Text` | Ebenheit (Spanne) |
| `Flatness_Label_Tooltip.Content` | Die Spanne der Höhenabweichungen über alle Gitterknoten: der Bereich vom tiefsten zum höchsten Punkt auf der ausgeglichenen Fläche. Dies ist die primäre Ebenheitskennzahl für die Tuschierplattenqualifizierung. |
| `FlatnessValue_Tooltip.Content` | Ebenheit in Mikrometern (µm). |
| `ResidualRMS_Label.Text` | Residuals-RMS |
| `ResidualRMS_Label_Tooltip.Content` | Quadratischer Mittelwert aller Schrittresiduen nach dem Kleinste-Quadrate-Ausgleich. Gibt das allgemeine Messrauschenniveau an. |
| `SigmaValue_Tooltip.Content` | σ = Residuals-RMS in Mikrometern. Kleinere Werte sind besser; Werte über ~50 µm können auf Probleme mit der Einschwingzeit des Instruments oder eine sehr raue Oberfläche hinweisen. |
| `Flatness_Help_Title.Text` | Ebenheit (Spanne) |
| `Flatness_Help_Body.Text` | Nach der Lösung des überbestimmten Gleichungssystems hat jeder Gitterknoten eine berechnete Höhe. Die Ebenheit ist einfach die Spanne: maximale Knotenhöhe minus minimale Knotenhöhe, ausgedrückt in Mikrometern. Dies ist die Standardgröße, die in Kalibrierzertifikaten für Tuschierplatten angegeben wird (z. B. DIN EN ISO 8512). |
| `ResidualRMS_Help_Title.Text` | Residuals-RMS (σ) |
| `ResidualRMS_Help_Body.Text` | Jeder Messschritt trägt eine Gleichung zum Kleinste-Quadrate-System bei. Nach der Lösung ist das Residuum eines Schritts die Differenz zwischen dem Messwert und der durch die ausgeglichene Fläche implizierten Höhendifferenz. σ ist der quadratische Mittelwert aller Residuen, korrigiert für Freiheitsgrade (FG = Schritte − Knoten + 1). Ein großes σ deutet auf verrauschte Messwerte oder ein thermisch nicht eingeschwungenes Instrument hin. |
| `LeastSquares_Help_Title.Text` | Kleinste-Quadrate-Ausgleich |
| `LeastSquares_Help_Body.Text` | Bei einem Vollraster erscheint jeder innere Knoten in zwei unabhängigen Messlinien: einem Zeilendurchlauf und einem Spaltendurchlauf. Theoretisch müssen die berechneten Höhen an jedem Kreuzungspunkt übereinstimmen; in der Praxis weichen sie leicht ab, bedingt durch Messrauschen und thermische Drift. Einfache sequentielle Integration lässt diese Schließungsfehler akkumulieren. Die Kleinste-Quadrate-Methode verteilt sie optimal über alle Schritte gleichzeitig und liefert die statistisch beste Schätzung der wahren Oberflächenform. |
| `FlaggedSteps_Help_Title.Text` | Markierte Schritte |
| `FlaggedSteps_Help_Body.Text` | Nach dem Kleinste-Quadrate-Ausgleich wird das Residuum jedes Schritts mit σ verglichen. Jeder Schritt, dessen Residuum k × σ überschreitet (Standard k = 2,5, in den Neuberechnungseinstellungen einstellbar), wird als verdächtiger Messwert markiert. Mögliche Ursachen: Instrument nicht vollständig eingeschwungen, versehentlicher Stoß, Libelle nicht zentriert oder die Plattenoberfläche hat einen echten lokalen Defekt. Eine Korrektursitzung ermöglicht die erneute Messung nur der markierten Schritte und eine Neuberechnung des Ergebnisses. |
| `SigmaThreshold_Help_Title.Text` | Sigma-Schwellenwert (k) |
| `SigmaThreshold_Help_Body.Text` | Der Ausreißer-Erkennungsschwellenwert, ausgedrückt als Vielfaches von σ. Ein Schritt wird markiert, wenn |Residuum| > k × σ. Der Standardwert von 2,5 ist eine übliche Wahl, die in einer Normalverteilung ungefähr das schlechteste 1 % der Messwerte markiert. Ein niedrigeres k markiert mehr Schritte; ein höheres markiert weniger. |
| `CorrectionRound_Help_Title.Text` | Korrekturschritt |
| `CorrectionRound_Help_Body.Text` | Ein Korrekturschritt misst nur die markierten Schritte erneut und speichert die neuen Messwerte neben den ursprünglichen – Originale werden nie überschrieben. Nach der erneuten Messung aller markierten Schritte wird der vollständige Kleinste-Quadrate-Ausgleich auf dem zusammengeführten Datensatz wiederholt. Der Vorgang kann so oft wiederholt werden, bis keine Schritte mehr markiert sind oder der Bediener das Ergebnis akzeptiert. Die vollständige Historie aller Schritte wird in der Projektdatei gespeichert. |

#### CorrectionView

| Key | German value |
|---|---|
| `Correction_PageTitle.Text` | Korrektur |
| `Correction_ProgressTitle.Text` | Markierter Schritt {0} von {1} |
| `Correction_OriginalReading_Label.Text` | Ursprünglicher Messwert |
| `Correction_OriginalReading_Tooltip.Content` | Der während der ersten Messrunde aufgezeichnete Messwert. Vergleichen Sie diesen mit Ihrem neuen Messwert – eine große Abweichung bestätigt, dass der ursprüngliche Wert verdächtig war. |
| `Correction_ReadingLabel.Text` | Neuer Messwert (mm/m) |
| `Correction_AcceptButton.Content` | Messwert übernehmen |
| `Correction_CancelButton.Content` | Korrektur abbrechen |

#### RecalculateDialog

| Key | German value |
|---|---|
| `Recalculate_Title.Text` | Neuberechnung |
| `Recalculate_Method_Label.Text` | Berechnungsmethode |
| `Recalculate_Method_Tooltip.Content` | Wählen Sie den Algorithmus, der zur Berechnung der Knotenhöhen aus den Messschritten verwendet wird. |
| `Recalculate_Method_LeastSquares.Text` | Kleinste Quadrate (empfohlen) |
| `Recalculate_Method_Sequential.Text` | Sequentielle Integration |
| `Recalculate_SigmaThreshold_Label.Text` | Sigma-Schwellenwert (k) |
| `Recalculate_AutoExclude_Label.Text` | Ausreißer automatisch ausschließen |
| `Recalculate_AutoExclude_Tooltip.Content` | Wenn aktiviert, werden markierte Schritte automatisch aus dem Kleinste-Quadrate-System ausgeschlossen und beeinflussen die berechnete Fläche nicht. Wenn deaktiviert, verbleiben sie im System, werden aber dennoch gemeldet. |
| `Recalculate_SaveResult_Label.Text` | Ergebnis im Projekt speichern |
| `Recalculate_OkButton.Content` | Neu berechnen |
| `Recalculate_CancelButton.Content` | Abbrechen |
| `CalcMethod_Help_Title.Text` | Berechnungsmethode |
| `CalcMethod_Help_Body.Text` | Kleinste Quadrate ist die empfohlene Methode. Sie löst das überbestimmte Schrittegleichungssystem gleichzeitig und verteilt Schließungsfehler optimal. Sequentielle Integration ist eine einfachere Methode, die jede Traversierungslinie unabhängig integriert und an jedem Kreuzungspunkt eine proportionale Korrektur anwendet – leichter nachvollziehbar, aber bei signifikantem Messrauschen weniger genau. |
| `SeqIntegration_Help_Title.Text` | Sequentielle Integration |
| `SeqIntegration_Help_Body.Text` | Jede Zeilen- und Spaltendurchquerung wird unabhängig integriert, um ein Höhenprofil zu erzeugen. Wo sich ein Zeilenprofil und ein Spaltenprofil an einem inneren Knoten kreuzen, weichen die beiden Höhen durch einen Schließungsfehler voneinander ab. Die sequentielle Integration verteilt diesen Fehler proportional entlang beider Durchquerungen. Die Methode ist unkompliziert, erlaubt aber die Fehlerakkumulation, besonders bei größeren Rastern. Kleinste Quadrate wird für Produktionsmessungen bevorzugt. |
| `LinearDrift_Help_Title.Text` | Lineare Driftkorrektur |
| `LinearDrift_Help_Body.Text` | Elektronische Libellen können während einer langen Messsitzung aufgrund thermischer Änderungen im Instrument langsam driften. Die lineare Driftkorrektur nimmt an, dass jede systematische Änderung der Messwerte während einer einzelnen Durchquerung linear mit der Zeit (oder der Schrittzahl) zunimmt, und subtrahiert diesen Trend vor der Berechnung der Knotenhöhen. Aktivieren Sie diese Option, wenn Sie feststellen, dass die Residuen gegen Ende jeder Durchquerung tendenziell zunehmen. |

#### PreferencesDialog

| Key | German value |
|---|---|
| `Preferences_Title.Text` | Einstellungen |
| `Preferences_Theme_Label.Text` | Erscheinungsbild |
| `Preferences_Theme_System.Content` | Systemeinstellung folgen |
| `Preferences_Theme_Light.Content` | Hell |
| `Preferences_Theme_Dark.Content` | Dunkel |
| `Preferences_DefaultFolder_Label.Text` | Standard-Projektordner |
| `Preferences_DefaultFolder_Tooltip.Content` | Der Ordner, den die Öffnen- und Speichern-Dialoge standardmäßig öffnen. |
| `Preferences_BrowseFolder.Content` | Durchsuchen… |
| `Preferences_OkButton.Content` | OK |
| `Preferences_CancelButton.Content` | Abbrechen |

#### AboutDialog

| Key | German value |
|---|---|
| `About_Title.Text` | Über LevelApp |
| `About_Description.Text` | Auswertung präziser elektronischer Nivelliermessungen für die Maschinengeometriemessung und Tuschierplattenqualifizierung. |
| `About_License.Text` | MIT-Lizenz |
| `About_GitHub.Content` | Auf GitHub ansehen |
| `About_CloseButton.Content` | Schließen |

#### NewMeasurementDialog

| Key | German value |
|---|---|
| `NewMeasurement_Title.Text` | Neue Messung |
| `NewMeasurement_Label_Label.Text` | Bezeichnung |
| `NewMeasurement_Operator_Label.Text` | Bediener |
| `NewMeasurement_Notes_Label.Text` | Anmerkungen |
| `NewMeasurement_OkButton.Content` | Starten |
| `NewMeasurement_CancelButton.Content` | Abbrechen |

#### Parallel Ways specific strings

| Key | German value |
|---|---|
| `PW_Rails_SectionHeader.Text` | Führungsbahnen |
| `PW_AddRail_Button.Content` | Führungsbahn hinzufügen |
| `PW_RailLabel_Label.Text` | Bezeichnung |
| `PW_RailLength_Label.Text` | Länge (mm) |
| `PW_RailLength_Tooltip.Content` | Physische Länge dieser Führungsbahn in Millimetern. Wird zur Berechnung des Stationsabstands verwendet. |
| `PW_RailSeparation_Label.Text` | Seitlicher Abstand (mm) |
| `PW_RailSeparation_Tooltip.Content` | Abstand zwischen dieser Führungsbahn und der Referenzführungsbahn, gemessen senkrecht zur Fahrtrichtung. |
| `PW_Tasks_SectionHeader.Text` | Aufgaben |
| `PW_AddTask_Button.Content` | Aufgabe hinzufügen |
| `PW_TaskType_AlongRail.Text` | Entlang der Bahn |
| `PW_TaskType_Bridge.Text` | Querbrücke |
| `PW_Orientation_Label.Text` | Ausrichtung |
| `PW_Orientation_Tooltip.Content` | Die physische Richtung, in der die Führungsbahnen verlaufen. Wird für Anzeigezwecke und zur Beschriftung von Schrittanweisungen verwendet. |
| `PW_DriftCorrection_Label.Text` | Driftkorrektur |
| `PW_DriftCorrection_Tooltip.Content` | Wendet eine lineare Korrektur an, um langsame Instrumentendrift während einer langen Durchquerung zu kompensieren. |
| `PW_SolverMode_Label.Text` | Solver-Modus |
| `PW_ReferenceRail_Label.Text` | Referenzbahn |
| `PW_ReferenceRail_Tooltip.Content` | Die Führungsbahn, deren erste Station als Höhendatum verwendet wird (Höhe = 0). Alle anderen Höhen werden relativ zu diesem Punkt ausgedrückt. |
| `PW_Straightness_Label.Text` | Geradheit (Spanne) |
| `PW_Straightness_Tooltip.Content` | Spanne der Höhenabweichungen dieser Führungsbahn nach Subtraktion der Ausgleichsgeraden. Dies ist die Standardkennzahl für Geradheit. |
| `PW_Parallelism_Label.Text` | Parallelität (Spanne) |
| `PW_Parallelism_Tooltip.Content` | Spanne der Höhendifferenzvariation zwischen zwei Führungsbahnen entlang ihrer gemeinsamen Stationen. Ein perfekt paralleles Bahnpaar hat einen Parallelitätswert von null. |
| `PW_Straightness_Help_Title.Text` | Geradheit |
| `PW_Straightness_Help_Body.Text` | Geradheit misst, wie stark eine Führungsbahn von einer perfekten Geraden abweicht. Zunächst werden alle Stationshöhen durch Integration der Nivelliermesswerte entlang der Bahn berechnet. Dann wird die Ausgleichsgerade durch alle Stationshöhen subtrahiert. Die verbleibenden Abweichungen bilden das Geradheitsprofil; die Spanne dieses Profils ist der gemeldete Geradheitswert. |
| `PW_Parallelism_Help_Title.Text` | Parallelität |
| `PW_Parallelism_Help_Body.Text` | Parallelität ist die Variation der Höhendifferenz zwischen zwei Führungsbahnen an ihren gemeinsamen Stationspositionen. An jeder Station wird die Höhe von Bahn B minus der Höhe von Bahn A berechnet. Wären die Bahnen perfekt parallel, wäre diese Differenz entlang der gesamten Länge konstant. Die Spanne dieser Differenzen ist der Parallelitätswert. |
| `PW_SolverMode_Help_Title.Text` | Solver-Modus |
| `PW_SolverMode_Help_Body.Text` | Globale Kleinste Quadrate löst alle Bahn- und Brückenmesswerte gleichzeitig in einem gemeinsamen System, was bei verfügbaren Brückenmesswerten das statistisch optimale Ergebnis liefert. Unabhängig dann Abstimmen löst zunächst jede Bahn unabhängig, dann werden in einem zweiten Schritt die unabhängigen Bahnhöhen mithilfe der Brückenmesswerte auf ein gemeinsames Datum gebracht. Beide Modi liefern dasselbe Ergebnis bei rauschfreien Brückenmesswerten; Globale Kleinste Quadrate behandelt verrauschte Brückenmesswerte robuster. |

---

## 6. Implementation Steps for Claude Code

Implement in this order:

### Step 1 — Resource files
1. Create `LevelApp.App/Strings/en-US/Resources.resw` with all keys from §5.1 above.
   The `.resw` file is XML; each entry has a `<data name="KEY" xml:space="preserve"><value>VALUE</value></data>` element.
2. Create `LevelApp.App/Strings/de-DE/Resources.resw` with all keys from §5.2.
3. In `LevelApp.App/LevelApp.App.csproj` verify (or add) the standard WinUI 3 resw build action:
   ```xml
   <PRIResource Include="Strings\en-US\Resources.resw" />
   <PRIResource Include="Strings\de-DE\Resources.resw" />
   ```
   If the project already uses `<Content>` or `<EmbeddedResource>` for `.resw`, switch to `<PRIResource>` so WinUI 3's resource system picks them up.

### Step 2 — `ILocalisationService`
4. Create `LevelApp.App/Services/ILocalisationService.cs` and `LocalisationService.cs` as shown in §3.3.
5. Register in `App.xaml.cs` DI container: `services.AddSingleton<ILocalisationService, LocalisationService>();`

### Step 3 — `HelpButtonStyle`
6. Create `LevelApp.App/Styles/HelpButtonStyle.xaml` as shown in §4.3.
7. Merge into `App.xaml`:
   ```xml
   <ResourceDictionary Source="Styles/HelpButtonStyle.xaml" />
   ```

### Step 4 — Views: replace hardcoded strings with `x:Uid` and add ⓘ buttons
Apply to all four views and all four dialogs. For each view:
- Replace every hardcoded `Content`, `Text`, `PlaceholderText`, `Header` attribute with `x:Uid="{KeyName}"` where the key name without the property suffix matches a row in the resource tables.
- Add a `<StackPanel Orientation="Horizontal">` wrapper + ⓘ `Button` with `Style="{StaticResource HelpButtonStyle}"` and a `Flyout` wherever a `_Help_Title` / `_Help_Body` pair exists in the resource tables.
- Add `<ToolTipService.ToolTip>` to every label and result value that has a corresponding `_Tooltip` key.

Priority order (implement in this order):
1. `ResultsView.xaml` — highest user value; Flatness, σ, Flagged Steps, Least-Squares sections
2. `MeasurementView.xaml` — Reading and Orientation ⓘ buttons
3. `ProjectSetupView.xaml` — Strategy ⓘ buttons, all parameter tooltips
4. `CorrectionView.xaml` — Original Reading tooltip
5. `RecalculateDialog.xaml` — Method and threshold ⓘ buttons
6. `PreferencesDialog.xaml`, `NewMeasurementDialog.xaml`, `AboutDialog.xaml` — labels and buttons

### Step 5 — `MainWindow.xaml` menu strings
Replace hardcoded menu item strings with `x:Uid` references using the `Menu_*` keys.

### Step 6 — Version bump & architecture update
- Bump `AppVersion.cs` to `0.9.0`.
- Update `docs/architecture.md`:
  - §2 Technology Stack: add row `| Localisation | Windows Resource (.resw) | Standard WinUI 3 mechanism; automatic language selection by OS locale |`
  - §3 Solution Structure: add `Strings/en-US/Resources.resw` and `Strings/de-DE/Resources.resw` under `LevelApp.App/`; add `Services/ILocalisationService.cs`, `Services/LocalisationService.cs`; add `Styles/HelpButtonStyle.xaml`
  - §12 Roadmap: mark WP0.09 complete
  - §14 Open Questions: update localisation question to "English (en-US) and German (de-DE) added in WP0.09. Further languages via additional .resw files."

---

## 7. Acceptance Criteria

- [ ] App builds and runs with no hardcoded UI strings in XAML or C# (except `AppVersion.cs`).
- [ ] All labels, button text, section headers, and dialog titles are driven by `.resw` keys.
- [ ] Every result metric (`Flatness`, `σ`) has a tooltip and an ⓘ flyout.
- [ ] Every algorithm concept (`Least-Squares`, `Sequential Integration`, `Linear Drift`, `Flagged Steps`, `Sigma Threshold`, `Correction Round`, `Measurement Strategy`, `Full Grid`, `Union Jack`, `Parallel Ways Solver Mode`, `Straightness`, `Parallelism`) has an ⓘ flyout with title + body.
- [ ] Every parameter input (`Width`, `Height`, `Columns`, `Rows`, `Sigma Threshold`, `Rail Length`, etc.) has a tooltip.
- [ ] `de-DE/Resources.resw` contains all keys from `en-US/Resources.resw` with no missing entries.
- [ ] Switching OS display language to German shows German strings throughout.
- [ ] No regressions in existing functionality.
- [ ] `AppVersion` is `0.9.0`.

---

## 8. Notes for Claude Code

- The app is currently at v0.8.4. Do not assume any earlier work package is pending.
- Do not use `ResourceLoader` directly in ViewModels. Use `ILocalisationService.Get(key)` only where XAML `x:Uid` is not sufficient (e.g. dynamically composed instruction strings).
- The `.resw` XML format uses `xml:space="preserve"` on each `<data>` element; do not omit it.
- Flyout body text (`_Help_Body`) can be long. Set `TextWrapping="Wrap"` and `MaxWidth="340"` on the `StackPanel` inside each `Flyout`.
- Do not add `TeachingTip` controls in this work package — they are deferred.
- Do not introduce any new NuGet packages. `ResourceLoader` is part of `Microsoft.Windows.SDK.BuildTools` / WinRT and requires no additional dependency.
- The `HelpButtonStyle` uses the Segoe MDL2 Assets icon `&#xE946;` (Information). Verify it renders correctly; if not, `&#xE897;` (Help) is an acceptable fallback.
- Commit message: `[v0.9.0] WP0.09: contextual help system (tooltips + flyouts) and en-US/de-DE localisation`
