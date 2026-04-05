\# LevelApp — Project Architecture \& Design Reference



> Living document. Update as the project evolves.

> Last updated: 2026-04-04



\---



\## 1. Purpose



A Windows desktop application for evaluating precision electronic level measurements used in machine tool geometry inspection and granite surface plate qualification. The software acquires readings from an instrument (manually entered initially, Bluetooth/USB HID later), guides the operator through a defined measurement procedure, computes a best-fit surface map using least-squares adjustment, detects suspect readings, and displays results graphically.



The industry reference for this domain is \*\*Wyler AG, Winterthur\*\* (wylerag.com) and their wylerSOFT suite.



\---



\## 2. Technology Stack



| Concern | Choice | Rationale |

|---|---|---|

| Language | C# (.NET 8/9) | Best WinUI 3 ecosystem support |

| UI Framework | WinUI 3 / Windows App SDK | Modern Windows-native, Fluent Design, access to WinRT APIs |

| IDE | Visual Studio 2022 Community | Free, full WinUI 3 tooling, XAML designer |

| UI Pattern | MVVM | Mandatory for WinUI 3; decouples UI from logic |

| Persistence | JSON via System.Text.Json | Human-readable, no dependencies, diffable |

| Bluetooth (future) | Windows.Devices.Bluetooth (WinRT) | First-class Windows API, no third-party libs needed |

| USB HID (future) | Windows.Devices.HumanInterfaceDevice (WinRT) | Same rationale |



\---



\## 3. Solution Structure



```

LevelApp/

├── LevelApp.sln

├── Core/                          ← No UI dependencies. Fully unit-testable.

│   ├── Models/

│   │   ├── Project.cs

│   │   ├── ObjectDefinition.cs

│   │   ├── MeasurementSession.cs

│   │   ├── MeasurementRound.cs

│   │   ├── MeasurementStep.cs

│   │   ├── CorrectionRound.cs

│   │   └── SurfaceResult.cs

│   ├── Interfaces/

│   │   ├── IGeometryModule.cs

│   │   ├── IMeasurementStrategy.cs

│   │   ├── IInstrumentProvider.cs

│   │   └── IResultDisplay.cs

│   └── Geometry/

│       └── SurfacePlate/

│           ├── SurfacePlateModule.cs

│           ├── Strategies/

│           │   ├── FullGridStrategy.cs

│           │   └── UnionJackStrategy.cs

│           └── SurfacePlateCalculator.cs

├── Instruments/

│   └── ManualEntry/

│       └── ManualEntryProvider.cs     ← First instrument provider

├── App/                               ← WinUI 3 application

│   ├── Views/

│   │   ├── ProjectSetupView.xaml

│   │   ├── MeasurementView.xaml

│   │   └── ResultsView.xaml

│   ├── ViewModels/

│   │   ├── ProjectSetupViewModel.cs

│   │   ├── MeasurementViewModel.cs

│   │   └── ResultsViewModel.cs

│   └── DisplayModules/

│       └── SurfacePlot3D/

│           └── SurfacePlot3DDisplay.cs

└── docs/

&#x20;   └── architecture.md               ← This file

```



\---



\## 4. Core Interfaces



\### IGeometryModule

Represents a type of object to be measured (e.g. surface plate, lathe bed, machine column). Each module:

\- Defines what parameters it needs from the user (plate dimensions, grid size, etc.)

\- Exposes the list of available measurement strategies

\- Owns the calculator for its geometry type



```csharp

public interface IGeometryModule

{

&#x20;   string ModuleId { get; }

&#x20;   string DisplayName { get; }

&#x20;   IEnumerable<IMeasurementStrategy> AvailableStrategies { get; }

&#x20;   IGeometryCalculator CreateCalculator(ObjectDefinition definition);

}

```



\### IMeasurementStrategy

Generates the ordered sequence of guided steps for a given object definition. A strategy's only job is to produce the step list — it knows nothing about calculation.



```csharp

public interface IMeasurementStrategy

{

&#x20;   string StrategyId { get; }

&#x20;   string DisplayName { get; }

&#x20;   IReadOnlyList<MeasurementStep> GenerateSteps(ObjectDefinition definition);

}

```



\### IInstrumentProvider

Abstracts all instrument/connectivity code. Today it returns a value the user typed. Tomorrow it streams from Bluetooth. Geometry modules never know which provider is active.



```csharp

public interface IInstrumentProvider

{

&#x20;   string ProviderId { get; }

&#x20;   string DisplayName { get; }

&#x20;   Task<double> GetReadingAsync(MeasurementStep step, CancellationToken ct);

}

```



\### IResultDisplay

Each display module receives a completed result and renders it. New visualisations can be added without touching anything else.



```csharp

public interface IResultDisplay

{

&#x20;   string DisplayId { get; }

&#x20;   string DisplayName { get; }

&#x20;   UIElement Render(SurfaceResult result);

}

```



\---



\## 5. Data Model Hierarchy



```

Project

├── id, name, createdAt, modifiedAt, operator, notes

├── ObjectDefinition

│   ├── geometryModuleId          (e.g. "SurfacePlate")

│   └── parameters                (flexible key-value; module interprets)

│       ├── widthMm

│       ├── heightMm

│       ├── columnsCount

│       └── rowsCount

└── Measurements\[ ]

&#x20;   └── MeasurementSession

&#x20;       ├── id, label, takenAt, operator, instrumentId, strategyId, notes

&#x20;       ├── InitialRound

&#x20;       │   ├── completedAt

&#x20;       │   ├── Steps\[ ]

&#x20;       │   │   └── MeasurementStep

&#x20;       │   │       ├── index, gridCol, gridRow

&#x20;       │   │       ├── orientation  (North | South | East | West)

&#x20;       │   │       ├── instructionText

&#x20;       │   │       └── reading (mm/m)

&#x20;       │   └── Result

&#x20;       │       ├── heightMapMm\[]\[]

&#x20;       │       ├── flatnessValueMm

&#x20;       │       ├── residuals\[]

&#x20;       │       ├── flaggedStepIndices\[]

&#x20;       │       └── sigmaThreshold

&#x20;       └── Corrections\[ ]

&#x20;           └── CorrectionRound

&#x20;               ├── id, triggeredAt, operator, notes

&#x20;               ├── ReplacedSteps\[ ]

&#x20;               │   └── originalStepIndex + new reading

&#x20;               └── Result  (same structure as above)

```



\*\*Key rule:\*\* Raw readings and all intermediate results are \*\*always preserved\*\*. Nothing is ever overwritten. Results reflect the latest correction round, but full history is queryable.



\---



\## 6. Measurement Strategies



\### Full Grid

The standard approach. Traverses all rows (boustrophedon — alternating direction to avoid instrument repositioning) then all columns. Every interior grid point is visited twice, once horizontally and once vertically.



```

Row pass:

&#x20; Row 0:  (0,0)→(1,0)→...→(N,0)   orientation: East

&#x20; Row 1:  (N,1)→...→(1,1)→(0,1)   orientation: West

&#x20; Row 2:  (0,2)→...→(N,2)          orientation: East

&#x20; ...



Column pass:

&#x20; Col 0:  (0,0)→(0,1)→...→(0,M)   orientation: South

&#x20; Col 1:  (1,M)→...→(1,1)→(1,0)   orientation: North

&#x20; ...

```



\### Union Jack

Adds diagonal traversals to the Full Grid (or uses diagonals + perimeter only as the classic Moody method). More steps, higher redundancy.



\### Adding new strategies

Implement `IMeasurementStrategy`, register with the geometry module. No other changes required.



\---



\## 7. Algorithm: Least-Squares Surface Fitting



\### Why least-squares?

With a full grid, every interior point appears in two independent measurement lines (one row pass, one column pass). In theory the integrated heights must agree at every crossing point (closure). In practice they don't, due to instrument noise and drift. Simple sequential integration lets closure errors accumulate. Least-squares distributes them optimally.



\### Closure residuals

At each interior grid point:

```

residual(i,j) = height\_from\_rows(i,j) − height\_from\_cols(i,j)

```



\### Per-step residuals

After the global least-squares fit, every individual step has a residual: the difference between its reading and the value implied by the fitted surface. Large residuals flag suspect steps.



\### Outlier detection

Flag any step where:

```

|residual\_i| > k × σ

```

where σ is the standard deviation of all residuals and k is configurable (default: 2.5).



The software presents flagged steps sorted by residual magnitude, with the original reading shown.



\### Correction workflow

1\. Solver flags suspect steps after initial round

2\. User reviews flagged list, optionally triggers a correction session

3\. Guided mini-session visits only the flagged steps (showing original reading for comparison)

4\. New readings are stored as a `CorrectionRound` — originals untouched

5\. Full recalculation runs on the merged dataset

6\. Process can repeat until no steps are flagged or user accepts the result



\---



\## 8. Guided Measurement State Machine



```

\[Project Setup]

&#x20;     ↓

\[Strategy Selection]

&#x20;     ↓

\[Ready — step overview shown]

&#x20;     ↓

\[Measuring: step N of M]  ←─────────────┐

&#x20;     │                                  │

&#x20;     │  UI shows:                       │

&#x20;     │  • Grid map, current pos lit     │

&#x20;     │  • Instrument orientation arrow  │

&#x20;     │  • Instruction text              │

&#x20;     │  • Progress (N of M)             │

&#x20;     │  • Reading entry field           │

&#x20;     ↓                                  │

\[Reading accepted] ────────────────────→─┘

&#x20;     ↓ (all steps done)

\[Calculating...]

&#x20;     ↓

\[Results + flagged steps]

&#x20;     ↓ (if flags exist)

\[Correction session available]

```



\---



\## 9. Persistence — JSON File Format



File extension: `.levelproj` (internally JSON)



```json

{

&#x20; "schemaVersion": "1.0",

&#x20; "project": {

&#x20;   "id": "<uuid>",

&#x20;   "name": "Granite plate workshop 3",

&#x20;   "createdAt": "2026-04-04T09:00:00Z",

&#x20;   "modifiedAt": "2026-04-04T14:23:00Z",

&#x20;   "operator": "J. Müller",

&#x20;   "notes": "After resurfacing",



&#x20;   "objectDefinition": {

&#x20;     "geometryModuleId": "SurfacePlate",

&#x20;     "parameters": {

&#x20;       "widthMm": 1200,

&#x20;       "heightMm": 800,

&#x20;       "columnsCount": 8,

&#x20;       "rowsCount": 5

&#x20;     }

&#x20;   },



&#x20;   "measurements": \[

&#x20;     {

&#x20;       "id": "<uuid>",

&#x20;       "label": "Measurement 1",

&#x20;       "takenAt": "2026-04-04T10:15:00Z",

&#x20;       "operator": "J. Müller",

&#x20;       "instrumentId": "manual-entry",

&#x20;       "strategyId": "FullGrid",

&#x20;       "notes": "",



&#x20;       "initialRound": {

&#x20;         "completedAt": "2026-04-04T10:45:00Z",

&#x20;         "steps": \[

&#x20;           {

&#x20;             "index": 0,

&#x20;             "gridCol": 0, "gridRow": 0,

&#x20;             "orientation": "East",

&#x20;             "reading": 0.012

&#x20;           }

&#x20;         ],

&#x20;         "result": {

&#x20;           "heightMapMm": \[\[0.0, 0.012], \[0.008, 0.019]],

&#x20;           "flatnessValueMm": 0.041,

&#x20;           "residuals": \[0.001, 0.002, 0.087],

&#x20;           "flaggedStepIndices": \[14, 31],

&#x20;           "sigmaThreshold": 2.5

&#x20;         }

&#x20;       },



&#x20;       "corrections": \[

&#x20;         {

&#x20;           "id": "<uuid>",

&#x20;           "triggeredAt": "2026-04-04T11:02:00Z",

&#x20;           "operator": "J. Müller",

&#x20;           "notes": "Step 14 — instrument had not settled",

&#x20;           "replacedSteps": \[

&#x20;             { "originalStepIndex": 14, "reading": 0.008 }

&#x20;           ],

&#x20;           "result": {

&#x20;             "heightMapMm": \[\[...]],

&#x20;             "flatnessValueMm": 0.038,

&#x20;             "residuals": \[...],

&#x20;             "flaggedStepIndices": \[],

&#x20;             "sigmaThreshold": 2.5

&#x20;           }

&#x20;         }

&#x20;       ]

&#x20;     }

&#x20;   ]

&#x20; }

}

```



\### Schema versioning

The `schemaVersion` field at the root allows the app to detect older files and apply migration logic before deserialising. Always increment when making breaking changes to the format.



\---



\## 10. Display Modules



Implements `IResultDisplay`. Each module receives a `SurfaceResult` and returns a renderable `UIElement`.



| Module | Status | Notes |

|---|---|---|

| 3D Surface Plot | \*\*First to build\*\* | Primary result view |

| Colour / Heat Map | Future | Intuitive flatness overview |

| Numerical Table | Future | Raw height values per grid point |

| Residuals Chart | Future | Useful for diagnosing bad readings |



Adding a new display: implement `IResultDisplay`, register it. The results page discovers available modules automatically.



\---



\## 11. Key Design Decisions \& Rationale



| Decision | Rationale |

|---|---|

| Geometry modules as plugins | New object types (lathe bed, column, etc.) require no changes to core or UI |

| Measurement strategies as plugins | Full Grid and Union Jack share the same guided workflow infrastructure |

| Instrument providers as plugins | Manual entry and future Bluetooth/USB HID are interchangeable |

| Display modules as plugins | 3D plot, heat map, table can be added independently over time |

| ObjectDefinition.parameters as flexible key-value | Different object types need very different parameters; avoids a rigid schema |

| Least-squares over simple integration | Distributes closure errors optimally; more robust for noisy readings |

| Corrections as separate rounds, originals preserved | Full audit trail; operator can review the history of a session |

| JSON with schemaVersion | Human-readable, diffable, easily migrated as format evolves |

| .levelproj file extension | Clearly identifies the file type; internally standard JSON |

| MVVM pattern | Mandatory for testable WinUI 3 apps; Views are fully replaceable |

| Core project has zero UI dependencies | All models, interfaces, and algorithms are unit-testable without a UI |



\---



\## 12. Build Order / Roadmap



\### Phase 1 — Core foundation (no UI)

1\. Core models (`Project`, `ObjectDefinition`, `MeasurementSession`, `MeasurementStep`, etc.)

2\. Core interfaces (`IGeometryModule`, `IMeasurementStrategy`, `IInstrumentProvider`, `IResultDisplay`)

3\. `FullGridStrategy` — generates ordered step list with orientations

4\. `SurfacePlateCalculator` — least-squares solver + outlier detection

5\. `ManualEntryProvider` — trivial pass-through

6\. Unit tests for calculator and sequencer



\### Phase 2 — WinUI 3 app shell

7\. Solution setup, WinUI 3 project, navigation framework

8\. `ProjectSetupView` — object type selection, parameter entry

9\. `MeasurementView` — guided step-by-step workflow

10\. `ResultsView` with 3D surface plot display module



\### Phase 3 — Persistence

11\. JSON serialisation / deserialisation

12\. Save / load project file (`.levelproj`)



\### Phase 4 — Corrections workflow

13\. Flagged step review UI

14\. Guided correction session

15\. Correction round storage and recalculation



\### Future phases

\- `UnionJackStrategy`

\- Additional display modules (heat map, numerical table)

\- Bluetooth LE instrument provider

\- USB HID instrument provider

\- Additional geometry modules (straightness, squareness, etc.)

\- Reporting / PDF export



\---



\## 13. Open Questions



\- Should multiple instrument providers be selectable per measurement session (e.g. two axes simultaneously)?

\- Reporting: what format? PDF export? Print directly?

\- Localisation: German / English from the start, or English only initially?

\- Licensing / distribution model for the application?

\- Should the 3D surface plot be interactive (rotate, zoom)?



\---



\## 14. Model Switching Notes



When starting a new session with an AI assistant, paste this document as context. A concise session-start prompt:



> "I'm building LevelApp — a C# WinUI 3 Windows app for precision level measurement evaluation. The architecture document is below. We are currently at build step \[N]. Please continue from where we left off."



Then paste this document.



