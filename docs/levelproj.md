# `.levelproj` File Format Reference

File extension: `.levelproj`
Encoding: UTF-8
Format: JSON (indented, camelCase property names)

`.levelproj` files are plain JSON and are human-readable and version-control friendly. Every save operation writes a complete snapshot of the project including all measurement sessions, correction rounds, and computed results.

---

## Root object

```json
{
  "schemaVersion": "1.0",
  "appVersion": "0.2.1",
  "project": { ... }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `schemaVersion` | string | Yes | Identifies the file format version. Currently `"1.0"`. Used to detect older files and apply migration logic. |
| `appVersion` | string | No | Version of the app that wrote the file (e.g. `"0.2.1"`). Informational only — not validated on load. Absent in files written before v0.2.0. |
| `project` | object | Yes | The project data. See [Project](#project). |

Increment `schemaVersion` whenever a breaking change is made to the format. The app throws `NotSupportedException` for unrecognised schema versions.

---

## Project

```json
{
  "id": "<uuid>",
  "name": "Granite plate workshop 3",
  "createdAt": "2026-04-04T09:00:00Z",
  "modifiedAt": "2026-04-04T14:23:00Z",
  "operator": "J. Müller",
  "notes": "After resurfacing",
  "objectDefinition": { ... },
  "measurements": [ ... ]
}
```

| Field | Type | Description |
|---|---|---|
| `id` | string (UUID) | Stable project identifier. Assigned on creation, never changed. |
| `name` | string | Human-readable project name. |
| `createdAt` | string (ISO 8601 UTC) | Timestamp when the project was first created. |
| `modifiedAt` | string (ISO 8601 UTC) | Timestamp of the last save. |
| `operator` | string | Name of the operator who created the project. |
| `notes` | string | Free-text notes. May be empty. |
| `objectDefinition` | object | Describes the object being measured. See [ObjectDefinition](#objectdefinition). |
| `measurements` | array | Ordered list of measurement sessions. See [MeasurementSession](#measurementsession). |

---

## ObjectDefinition

```json
{
  "geometryModuleId": "SurfacePlate",
  "parameters": {
    "widthMm": 1200,
    "heightMm": 800,
    "columnsCount": 9,
    "rowsCount": 6
  }
}
```

| Field | Type | Description |
|---|---|---|
| `geometryModuleId` | string | Identifies the geometry type. Currently always `"SurfacePlate"`. |
| `parameters` | object | Module-specific key-value parameters. Interpreted by the geometry module. |

### Surface plate parameters

| Key | Type | Description |
|---|---|---|
| `widthMm` | number | Plate width in millimetres (X direction, columns). |
| `heightMm` | number | Plate height in millimetres (Y direction, rows). |
| `columnsCount` | number (int) | Number of grid nodes in the X direction. |
| `rowsCount` | number (int) | Number of grid nodes in the Y direction. |

---

## MeasurementSession

```json
{
  "id": "<uuid>",
  "label": "Measurement 1",
  "takenAt": "2026-04-04T10:15:00Z",
  "operator": "J. Müller",
  "instrumentId": "manual-entry",
  "strategyId": "FullGrid",
  "notes": "",
  "initialRound": { ... },
  "corrections": [ ... ]
}
```

| Field | Type | Description |
|---|---|---|
| `id` | string (UUID) | Stable session identifier. |
| `label` | string | Display label, e.g. `"Measurement 1"`. |
| `takenAt` | string (ISO 8601 UTC) | When the measurement session was started. |
| `operator` | string | Operator who took the measurements. |
| `instrumentId` | string | Identifies the instrument provider. Currently always `"manual-entry"`. |
| `strategyId` | string | Identifies the measurement strategy. Currently always `"FullGrid"`. |
| `notes` | string | Free-text notes. May be empty. |
| `initialRound` | object | The first complete measurement round. See [MeasurementRound](#measurementround). |
| `corrections` | array | Zero or more correction rounds applied after the initial round. See [CorrectionRound](#correctionround). |

---

## MeasurementRound

Used for `initialRound`. Contains the full ordered step list and the computed result.

```json
{
  "completedAt": "2026-04-04T10:45:00Z",
  "steps": [ ... ],
  "result": { ... }
}
```

| Field | Type | Description |
|---|---|---|
| `completedAt` | string (ISO 8601 UTC) | When the last reading was accepted. Null if the round is still in progress. |
| `steps` | array | Ordered list of measurement steps. See [MeasurementStep](#measurementstep). |
| `result` | object \| null | Computed result. Null until all steps have readings and the solver has run. See [SurfaceResult](#surfaceresult). |

---

## MeasurementStep

```json
{
  "index": 0,
  "gridCol": 0,
  "gridRow": 0,
  "orientation": "East",
  "instructionText": "Row pass — row 0, instrument at col 0 → 1, facing East",
  "reading": 0.012
}
```

| Field | Type | Description |
|---|---|---|
| `index` | number (int) | Zero-based step index within the round. |
| `gridCol` | number (int) | Column of the **from** endpoint. |
| `gridRow` | number (int) | Row of the **from** endpoint. |
| `orientation` | string | Direction from the **from** endpoint toward the **to** endpoint. One of `"North"`, `"South"`, `"East"`, `"West"`. |
| `instructionText` | string | Plain-text instruction shown to the operator. |
| `reading` | number \| null | Instrument reading in mm/m. Null until the operator records a value. |

#### Orientation — backwards compatibility

`orientation` is always written as a string. On read, the app also accepts legacy integer values: `0` = North, `1` = South, `2` = East, `3` = West (written by builds prior to string serialisation being enforced).

---

## SurfaceResult

```json
{
  "heightMapMm": [[0.0, 0.012, 0.021], [0.008, 0.019, 0.027]],
  "flatnessValueMm": 0.041,
  "residuals": [0.0012, -0.0008, 0.0031],
  "flaggedStepIndices": [14, 31],
  "sigmaThreshold": 2.5,
  "sigma": 0.00034
}
```

| Field | Type | Description |
|---|---|---|
| `heightMapMm` | number[][] | Jagged array of fitted node heights in mm, indexed `[row][col]`. Node `[0][0]` is the height reference (always 0.0). |
| `flatnessValueMm` | number | Peak-to-valley flatness value in mm. |
| `residuals` | number[] | Per-step residuals in mm, one entry per step in step-index order. |
| `flaggedStepIndices` | number[] | Indices of steps whose residual exceeds `sigmaThreshold × sigma`. Empty array if no steps are flagged. |
| `sigmaThreshold` | number | Outlier detection multiplier. Default: 2.5. |
| `sigma` | number | Residual RMS with DOF correction, in mm. `σ = sqrt(Σ residual² / DOF)` where `DOF = steps − (nodes − 1)`. |

---

## CorrectionRound

```json
{
  "id": "<uuid>",
  "triggeredAt": "2026-04-04T11:02:00Z",
  "operator": "J. Müller",
  "notes": "Step 14 — instrument had not settled",
  "replacedSteps": [
    { "originalStepIndex": 14, "reading": 0.008 }
  ],
  "result": { ... }
}
```

| Field | Type | Description |
|---|---|---|
| `id` | string (UUID) | Stable correction round identifier. |
| `triggeredAt` | string (ISO 8601 UTC) | When the correction session was started. |
| `operator` | string | Operator who performed the correction. |
| `notes` | string | Free-text notes. May be empty. |
| `replacedSteps` | array | Steps whose readings were replaced. See [ReplacedStep](#replacedstep). |
| `result` | object \| null | Result computed from the merged dataset (original readings with replacements applied). See [SurfaceResult](#surfaceresult). |

---

## ReplacedStep

```json
{ "originalStepIndex": 14, "reading": 0.008 }
```

| Field | Type | Description |
|---|---|---|
| `originalStepIndex` | number (int) | Zero-based index into `initialRound.steps`. |
| `reading` | number | Replacement reading in mm/m. |

The original reading in `initialRound.steps` is **never modified**. The correction round stores only the replacement value alongside a reference to the step it replaces. The full history is always queryable.

---

## Full example

```json
{
  "schemaVersion": "1.0",
  "appVersion": "0.2.1",
  "project": {
    "id": "d500c901-45a9-40ac-a69b-cd3ebba66a20",
    "name": "Granite plate workshop 3",
    "createdAt": "2026-04-04T09:00:00Z",
    "modifiedAt": "2026-04-04T14:23:00Z",
    "operator": "J. Müller",
    "notes": "After resurfacing",
    "objectDefinition": {
      "geometryModuleId": "SurfacePlate",
      "parameters": {
        "widthMm": 1200,
        "heightMm": 800,
        "columnsCount": 8,
        "rowsCount": 5
      }
    },
    "measurements": [
      {
        "id": "1c934f45-2bef-4ac7-99f9-ac5f25b0dd80",
        "label": "Measurement 1",
        "takenAt": "2026-04-04T10:15:00Z",
        "operator": "J. Müller",
        "instrumentId": "manual-entry",
        "strategyId": "FullGrid",
        "notes": "",
        "initialRound": {
          "completedAt": "2026-04-04T10:45:00Z",
          "steps": [
            {
              "index": 0,
              "gridCol": 0,
              "gridRow": 0,
              "orientation": "East",
              "instructionText": "Row pass — row 0, instrument at col 0 → 1, facing East",
              "reading": 0.012
            }
          ],
          "result": {
            "heightMapMm": [[0.0, 0.012], [0.008, 0.019]],
            "flatnessValueMm": 0.041,
            "residuals": [0.001, 0.002, 0.087],
            "flaggedStepIndices": [14, 31],
            "sigmaThreshold": 2.5,
            "sigma": 0.00034
          }
        },
        "corrections": [
          {
            "id": "a3f72b10-8c14-4d92-b5e1-3f1d9a2c7084",
            "triggeredAt": "2026-04-04T11:02:00Z",
            "operator": "J. Müller",
            "notes": "Step 14 — instrument had not settled",
            "replacedSteps": [
              { "originalStepIndex": 14, "reading": 0.008 }
            ],
            "result": {
              "heightMapMm": [[0.0, 0.011], [0.007, 0.018]],
              "flatnessValueMm": 0.038,
              "residuals": [0.001, 0.001, 0.002],
              "flaggedStepIndices": [],
              "sigmaThreshold": 2.5,
              "sigma": 0.00021
            }
          }
        ]
      }
    ]
  }
}
```
