# `.levelproj` File Format Specification

## 1. Overview

A `.levelproj` file is a UTF-8–encoded JSON document that stores a complete
LevelApp measurement project: object geometry, all measurement sessions
(including correction rounds), and the computed surface results.

| Property             | Value                           |
|----------------------|---------------------------------|
| File extension       | `.levelproj`                    |
| Encoding             | UTF-8, no BOM                   |
| MIME type            | `application/json`              |
| JSON formatting      | Indented (2-space), LF line endings as written by the app |
| Property name casing | **camelCase** throughout        |

---

## 2. Top-Level Document Structure

```json
{
  "schemaVersion": "1.0",
  "appVersion":    "0.4.0",
  "project":       { ... }
}
```

| Field           | Type   | Required | Description |
|-----------------|--------|----------|-------------|
| `schemaVersion` | string | **Yes**  | Must be `"1.0"` or `"1.1"`. Any other value causes the file to be rejected on load. Version `"1.1"` was introduced in v0.7.0 and adds `passPhase` on `MeasurementStep` and `parallelWaysResult` on `MeasurementRound`. |
| `appVersion`    | string | No       | Semver string of the LevelApp version that last wrote the file (e.g. `"0.4.1"`). Absent in files written before v0.4.0. Informational only — not validated on load. |
| `project`       | object | **Yes**  | Root project object. See §3. |

---

## 3. `project` Object

```json
"project": {
  "id":               "5d2110c7-ce69-46bd-8405-38ca4560ebdd",
  "name":             "Granite plate workshop 3",
  "createdAt":        "2026-04-05T21:04:50.6611033Z",
  "modifiedAt":       "2026-04-05T21:11:05.2726375Z",
  "operator":         "Lukas",
  "notes":            "Test plate 2",
  "objectDefinition": { ... },
  "measurements":     [ ... ]
}
```

| Field              | Type            | Required | Description |
|--------------------|-----------------|----------|-------------|
| `id`               | string (UUID)   | **Yes**  | RFC 4122 UUID, lowercase hyphenated: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`. |
| `name`             | string          | **Yes**  | Human-readable project name. May be empty string `""`. |
| `createdAt`        | string (ISO 8601) | **Yes** | UTC timestamp of project creation. Format: `"2026-04-05T21:04:50.6611033Z"`. |
| `modifiedAt`       | string (ISO 8601) | **Yes** | UTC timestamp of the last save. Same format as `createdAt`. |
| `operator`         | string          | **Yes**  | Default operator name for the project. May be empty string `""`. |
| `notes`            | string          | **Yes**  | Free-text project notes. May be empty string `""`. |
| `objectDefinition` | object          | **Yes**  | Describes the geometry being measured. See §4. |
| `measurements`     | array           | **Yes**  | Zero or more `MeasurementSession` objects. See §5. Empty array `[]` when no measurements have been taken. |

---

## 4. `objectDefinition` Object

```json
"objectDefinition": {
  "geometryModuleId": "SurfacePlate",
  "parameters": { ... }
}
```

| Field              | Type   | Required | Description |
|--------------------|--------|----------|-------------|
| `geometryModuleId` | string | **Yes**  | Identifies the geometry plug-in. Valid values: `"SurfacePlate"` or `"ParallelWays"`. |
| `parameters`       | object | **Yes**  | Key–value bag whose keys and value types depend on `geometryModuleId` and the measurement strategy. Values are JSON numbers (integer or floating-point) or strings. See §4.1 and §4.2. |

### 4.1 SurfacePlate — Full Grid strategy

The `parameters` object must contain:

| Key            | JSON type | Required | Unit | Description |
|----------------|-----------|----------|------|-------------|
| `widthMm`      | number    | **Yes**  | mm   | Physical width of the plate (X axis, left–right). Must be > 0. |
| `heightMm`     | number    | **Yes**  | mm   | Physical height of the plate (Y axis, top–bottom). Must be > 0. |
| `columnsCount` | number (integer) | **Yes** | — | Number of grid columns. Minimum 2. |
| `rowsCount`    | number (integer) | **Yes** | — | Number of grid rows. Minimum 2. |

`segments` and `rings` must **not** appear when `strategyId` is `"FullGrid"`.

Example:
```json
"parameters": {
  "widthMm":      1200,
  "heightMm":     800,
  "columnsCount": 9,
  "rowsCount":    6
}
```

### 4.2 SurfacePlate — Union Jack strategy

The `parameters` object must contain:

| Key        | JSON type | Required | Unit | Description |
|------------|-----------|----------|------|-------------|
| `widthMm`  | number    | **Yes**  | mm   | Physical width of the plate. Must be > 0. |
| `heightMm` | number    | **Yes**  | mm   | Physical height of the plate. Must be > 0. |
| `segments` | number (integer) | **Yes** | — | Number of steps from the center node to each arm tip. Minimum 1. Defines how many nodes appear on each of the eight arms. |
| `rings`    | string    | **Yes**  | —   | Ring-pass option. Must be one of the three string values in the table below. |

`columnsCount` and `rowsCount` must **not** appear when `strategyId` is `"UnionJack"` or `"ParallelWays"`.

**`rings` string values:**

| Value           | Ring passes generated | Step count (total) |
|-----------------|-----------------------|--------------------|
| `"None"`        | None — arm diagonals only. | 8 × `segments` |
| `"Circumference"` | One clockwise rectangular pass connecting the eight arm tips (at r = `segments`). | 8 × `segments` + 8 |
| `"Full"`        | All ring levels r = 1 … `segments`, from innermost to outermost. | 8 × `segments` + 8 × `segments` = 16 × `segments` |

> **Backward-compatibility note (pre-v0.4.0 files):** Older files stored `rings`
> as a JSON integer instead of a string. The app maps integer `0` → `"None"` and
> any positive integer → `"Full"` on load. New files always write a string.

Example:
```json
"parameters": {
  "widthMm":  1200,
  "heightMm": 800,
  "segments": 4,
  "rings":    "Circumference"
}
```

### 4.3 Parallel Ways

The `parameters` object must contain:

| Key                    | JSON type | Required | Description |
|------------------------|-----------|----------|-------------|
| `orientation`          | string    | **Yes**  | Axis along which rails run. Must be `"Horizontal"` or `"Vertical"`. |
| `referenceRailIndex`   | number (integer) | **Yes** | 0-based index of the reference rail (height datum is set at its first station). |
| `rails`                | array     | **Yes**  | One or more `RailDefinition` objects (see §4.3.1). Minimum 2. |
| `tasks`                | array     | **Yes**  | One or more `ParallelWaysTask` objects (see §4.3.2). |
| `driftCorrection`      | string    | **Yes**  | Drift correction method. Must be `"None"` or `"LeastSquares"`. |
| `solverMode`           | string    | **Yes**  | Solver mode. Must be `"GlobalLeastSquares"` or `"IndependentThenReconcile"`. |

#### 4.3.1 `RailDefinition` Object

| Field                  | JSON type | Required | Unit | Description |
|------------------------|-----------|----------|------|-------------|
| `label`                | string    | **Yes**  | —    | Human-readable rail label (e.g. `"A"`, `"Front"`). |
| `lengthMm`             | number    | **Yes**  | mm   | Total length of the rail. Must be > 0. |
| `lateralSeparationMm`  | number    | **Yes**  | mm   | Distance between this rail and the reference rail, measured perpendicular to the rail axis. |
| `verticalOffsetMm`     | number    | **Yes**  | mm   | Known vertical height difference of this rail's datum point relative to the reference rail. |
| `axialOffsetMm`        | number    | **Yes**  | mm   | Axial shift of this rail's station grid along the rail axis relative to the reference rail. |

#### 4.3.2 `ParallelWaysTask` Object

| Field             | JSON type | Required | Description |
|-------------------|-----------|----------|-------------|
| `taskType`        | string    | **Yes**  | Task kind. Must be `"AlongRail"` (traverse one rail) or `"Bridge"` (measure across two rails at each station). |
| `railIndexA`      | number (integer) | **Yes** | 0-based index of the primary rail (for AlongRail: the rail being traversed; for Bridge: the *from* rail). |
| `railIndexB`      | number (integer) | **Yes** | 0-based index of the secondary rail (for Bridge only; for AlongRail this field is ignored). |
| `stepDistanceMm`  | number    | **Yes**  | mm   | Distance between consecutive measurement stations along the rail (or between bridge endpoints). |
| `passDirection`   | string    | **Yes**  | Pass mode. Must be `"SinglePass"` or `"ForwardAndReturn"`. |

Example:
```json
"parameters": {
  "orientation":          "Horizontal",
  "referenceRailIndex":   0,
  "rails": [
    { "label": "A", "lengthMm": 1000, "lateralSeparationMm": 0,   "verticalOffsetMm": 0, "axialOffsetMm": 0 },
    { "label": "B", "lengthMm": 1000, "lateralSeparationMm": 400, "verticalOffsetMm": 0, "axialOffsetMm": 0 }
  ],
  "tasks": [
    { "taskType": "AlongRail", "railIndexA": 0, "railIndexB": 1, "stepDistanceMm": 200, "passDirection": "SinglePass" },
    { "taskType": "AlongRail", "railIndexA": 1, "railIndexB": 1, "stepDistanceMm": 200, "passDirection": "SinglePass" }
  ],
  "driftCorrection": "LeastSquares",
  "solverMode":      "GlobalLeastSquares"
}
```

---

## 5. `measurements` Array — `MeasurementSession` Objects

Each element of `measurements` represents one complete measurement session
(one trip through all steps, plus any subsequent correction rounds).

```json
{
  "id":           "6d7574de-6591-4214-a7d9-1a41084b3b1f",
  "label":        "Measurement 1",
  "takenAt":      "2026-04-05T21:04:50.6611004Z",
  "operator":     "Lukas",
  "instrumentId": "manual-entry",
  "strategyId":   "FullGrid",
  "notes":        "",
  "initialRound": { ... },
  "corrections":  [ ... ]
}
```

| Field          | Type            | Required | Description |
|----------------|-----------------|----------|-------------|
| `id`           | string (UUID)   | **Yes**  | Unique session identifier. |
| `label`        | string          | **Yes**  | Human-readable label, e.g. `"Measurement 2"`. |
| `takenAt`      | string (ISO 8601) | **Yes** | UTC timestamp when the session was started. |
| `operator`     | string          | **Yes**  | Name of the operator who took this session. May be empty string `""`. |
| `instrumentId` | string          | **Yes**  | Identifier of the instrument used. The app currently always writes `"manual-entry"`. |
| `strategyId`   | string          | **Yes**  | Identifies the measurement strategy. Must match a registered strategy: `"FullGrid"`, `"UnionJack"`, or `"ParallelWays"`. Must be consistent with `objectDefinition.geometryModuleId` and `parameters` (see §4). |
| `notes`        | string          | **Yes**  | Per-session notes. May be empty string `""`. |
| `initialRound` | object          | **Yes**  | The first and only full-pass measurement round. See §6. |
| `corrections`  | array           | **Yes**  | Zero or more `CorrectionRound` objects applied after the initial round. See §9. Empty array `[]` when no corrections exist. |

---

## 6. `initialRound` Object — `MeasurementRound`

```json
"initialRound": {
  "completedAt":       "2026-04-05T21:11:05.2726342Z",
  "steps":             [ ... ],
  "result":            { ... },
  "parallelWaysResult": { ... }
}
```

| Field                | Type            | Required | Description |
|----------------------|-----------------|----------|-------------|
| `completedAt`        | string (ISO 8601) \| `null` | **Yes** | UTC timestamp when the last step reading was recorded. `null` if measurement is still in progress (not all steps have readings yet). |
| `steps`              | array           | **Yes**  | Ordered list of `MeasurementStep` objects. See §7. Must not be empty once the session has been started. |
| `result`             | object \| `null` | **Yes** | `SurfaceResult` for Surface Plate sessions (if the calculation has been run); otherwise `null`. See §8. |
| `parallelWaysResult` | object \| `null` | **Yes** | `ParallelWaysResult` for Parallel Ways sessions (if the calculation has been run); otherwise `null`. See §9. Absent or `null` for Surface Plate sessions. |

---

## 7. `steps` Array — `MeasurementStep` Objects

Each step represents one level-instrument placement between two adjacent nodes.
The ordered sequence in the array defines the measurement order presented to the
operator; `index` must equal the element's 0-based position in the array.

### 7.1 Common fields (all strategies)

| Field             | Type             | Required | Description |
|-------------------|------------------|----------|-------------|
| `index`           | number (integer) | **Yes**  | 0-based position of this step in the array. Must equal the array index. |
| `gridCol`         | number (integer) | **Yes**  | X grid coordinate of the *from* node. 0-based. For Union Jack steps this is always `0` (not meaningful). |
| `gridRow`         | number (integer) | **Yes**  | Y grid coordinate of the *from* node. 0-based. For Union Jack steps this is always `0` (not meaningful). |
| `orientation`     | string           | **Yes**  | Direction from the *from* node to the *to* node. See §10 for allowed values. |
| `instructionText` | string           | **Yes**  | Human-readable instruction shown to the operator. May be empty string `""`. |
| `reading`         | number \| `null` | **Yes**  | Instrument reading in **mm/m** (millimetres per metre). `null` until the operator records a value. A reading of `0` is valid and distinct from `null`. |
| `nodeId`          | string           | **Yes*** | Identity of the *from* node. See §11 for naming conventions. |
| `toNodeId`        | string           | **Yes*** | Identity of the *to* node. Same naming conventions as `nodeId`. |

> \* `nodeId` and `toNodeId` were introduced in v0.4.0. Files written by earlier
> versions omit these fields; on load the app falls back to deriving node identity
> from `gridCol`, `gridRow`, and `orientation`. New files must always include them.

### 7.2 Full Grid (`strategyId = "FullGrid"`) step specifics

Node ids follow the pattern `"col{c}_row{r}"` where `c` is the 0-based column
index and `r` is the 0-based row index.

The `orientation` field is restricted to `"East"`, `"West"`, `"North"`, `"South"`.

**Row passes** (boustrophedon, one pass per row):

- Even rows (row 0, 2, 4, …) go **East**: `gridCol` increases from `0` to `columnsCount − 2`.
  For each step: `gridRow = r`, `gridCol = c`, `orientation = "East"`,
  `nodeId = "col{c}_row{r}"`, `toNodeId = "col{c+1}_row{r}"`.
- Odd rows go **West**: `gridCol` decreases from `columnsCount − 1` to `1`.
  `orientation = "West"`, `nodeId = "col{c}_row{r}"`, `toNodeId = "col{c-1}_row{r}"`.

Number of row-pass steps = `rowsCount × (columnsCount − 1)`.

**Column passes** (boustrophedon, one pass per column, after all row passes):

- Even columns (col 0, 2, 4, …) go **South**: `gridRow` increases from `0` to `rowsCount − 2`.
  `orientation = "South"`, `nodeId = "col{c}_row{r}"`, `toNodeId = "col{c}_row{r+1}"`.
- Odd columns go **North**: `gridRow` decreases from `rowsCount − 1` to `1`.
  `orientation = "North"`, `nodeId = "col{c}_row{r}"`, `toNodeId = "col{c}_row{r-1}"`.

Number of column-pass steps = `columnsCount × (rowsCount − 1)`.

**Total step count** = `rowsCount × (columnsCount − 1) + columnsCount × (rowsCount − 1)`.

Example step (Full Grid, East):
```json
{
  "index":           0,
  "gridCol":         0,
  "gridRow":         0,
  "orientation":     "East",
  "instructionText": "Row pass — row 1, instrument at column 1 → 2, facing East",
  "reading":         0.003521,
  "nodeId":          "col0_row0",
  "toNodeId":        "col1_row0"
}
```

### 7.3 Union Jack (`strategyId = "UnionJack"`) step specifics

`gridCol` and `gridRow` are always `0` for all Union Jack steps.

Node ids use one of two patterns:
- `"center"` — the single center node.
- `"arm{Dir}_seg{k}"` — a node on one of the eight arms, where `{Dir}` is the
  arm direction and `{k}` is the 1-based segment index (`1` = adjacent to center,
  `segments` = tip of the arm). See §11.2 for the full list of direction codes.

Steps are ordered by pass. Pass ids (not stored in the file) are implicit from
the step order: passes 0–3 are the four arm passes; passes 4+ are ring passes.

**Pass 0 — Horizontal arm (orientation `"East"`):**
Traverses from the W arm tip toward center, then from center toward the E arm tip.
Steps for `k = segments` down to `1`:
`nodeId = "armW_seg{k}"`, `toNodeId = "armW_seg{k-1}"` (last step: `toNodeId = "center"`).
Then for `k = 1` up to `segments`:
`nodeId = "center"` (first) or `"armE_seg{k-1}"`, `toNodeId = "armE_seg{k}"`.

**Pass 1 — Vertical arm (orientation `"South"`):**
N arm tip → center → S arm tip, identical pattern to Pass 0 with N/S.

**Pass 2 — SE diagonal (orientation `"SouthEast"`):**
NW arm tip → center → SE arm tip.

**Pass 3 — NE diagonal (orientation `"SouthWest"`):**
NE arm tip → center → SW arm tip.

**Pass 4… — Ring passes (one pass per active ring level):**
Each ring at radius `r` is a clockwise rectangle of 8 steps visiting:
SW→S (East), S→SE (East), SE→E (North), E→NE (North),
NE→N (West), N→NW (West), NW→W (South), W→SW (South).
All node ids end in `_seg{r}`.

Ring passes generated depend on `rings`:
- `"None"`: no ring passes.
- `"Circumference"`: one ring pass at r = `segments` (pass index 4).
- `"Full"`: ring passes at r = 1, 2, …, `segments` (pass indices 4, 5, …, 3 + `segments`).

Example step (Union Jack, SE diagonal, segment 1 → center):
```json
{
  "index":           4,
  "gridCol":         0,
  "gridRow":         0,
  "orientation":     "SouthEast",
  "instructionText": "SE diag — NW side, seg 1→0, facing SE",
  "reading":         -0.012,
  "nodeId":          "armNW_seg1",
  "toNodeId":        "center"
}
```

### 7.4 Parallel Ways (`strategyId = "ParallelWays"`) step specifics

`gridCol` and `gridRow` are always `0` for all Parallel Ways steps.

Node ids follow the pattern `"rail{r}_sta{s}"` (see §11.3).

An additional field is present on all Parallel Ways steps:

| Field       | Type   | Required | Description |
|-------------|--------|----------|-------------|
| `passPhase` | string | **Yes**  | Phase of the pass for this step. Must be `"NotApplicable"` (single-pass tasks), `"Forward"` (first pass of a ForwardAndReturn task), or `"Return"` (second pass). Omitted or `"NotApplicable"` for Surface Plate steps; defaults to `"NotApplicable"` when loading v1.0 files. |

**AlongRail steps** traverse along one rail: `nodeId = "rail{r}_sta{s}"`, `toNodeId = "rail{r}_sta{s+1}"` (Forward) or `"rail{r}_sta{s-1}"` (Return). Orientation is `"East"` (Forward) or `"West"` (Return) for Horizontal sessions.

**Bridge steps** cross between two rails at the same station: `nodeId = "rail{rA}_sta{s}"`, `toNodeId = "rail{rB}_sta{s}"`. Orientation is `"North"` or `"South"` for Horizontal sessions (depending on which rail is higher-indexed).

Example step (AlongRail, Forward):
```json
{
  "index":           0,
  "gridCol":         0,
  "gridRow":         0,
  "orientation":     "East",
  "passPhase":       "NotApplicable",
  "instructionText": "Rail A — sta 0→1, facing East",
  "reading":         0.005,
  "nodeId":          "rail0_sta0",
  "toNodeId":        "rail0_sta1"
}
```

---

## 8. `result` Object — `SurfaceResult`

Present when the calculation has been run; `null` otherwise.

```json
"result": {
  "nodeHeights":       { "col0_row0": 0.0, "col1_row0": 0.042, ... },
  "flatnessValueMm":   0.042,
  "residuals":         [ 1.2e-9, -3.4e-9, ... ],
  "flaggedStepIndices": [],
  "sigmaThreshold":    2.5,
  "sigma":             0.000012,
  "primitiveLoops":    [ ... ],
  "closureErrorMean":  0.000003,
  "closureErrorMedian":0.0000025,
  "closureErrorMax":   0.000008,
  "closureErrorRms":   0.000004
}
```

| Field                | Type             | Description |
|----------------------|------------------|-------------|
| `nodeHeights`        | object           | **Required.** Best-fit surface height in mm for every node, keyed by node id. The datum node is pinned to 0 mm. The number of entries equals the total number of unique nodes across all steps. Key naming: Full Grid = `"col{c}_row{r}"`; Union Jack = `"arm{Dir}_seg{k}"` or `"center"`. |
| `flatnessValueMm`    | number           | **Required.** Peak-to-valley deviation in mm: `max(nodeHeights) − min(nodeHeights)`. Always ≥ 0. |
| `residuals`          | array of numbers | **Required.** One signed residual in mm per step, in the same order as the `steps` array. Length must equal `steps.length`. |
| `flaggedStepIndices` | array of integers | **Required.** `index` values of steps whose absolute residual exceeds `sigmaThreshold × sigma`. Empty array `[]` when no steps are flagged. |
| `sigmaThreshold`     | number           | **Required.** Outlier-detection multiplier applied during the calculation. Default `2.5`. |
| `sigma`              | number           | **Required.** Residual RMS in mm, computed as √(Σresidual²/DOF). |
| `primitiveLoops`     | array            | **Required.** Array of `PrimitiveLoop` objects (see §8.1). Empty array `[]` when no primitive loops were computed. |
| `closureErrorMean`   | number           | **Required.** Mean of `|closureErrorMm|` across all primitive loops, in mm. `0` when `primitiveLoops` is empty. |
| `closureErrorMedian` | number           | **Required.** Median of `|closureErrorMm|`, in mm. `0` when `primitiveLoops` is empty. |
| `closureErrorMax`    | number           | **Required.** Maximum `|closureErrorMm|`, in mm. `0` when `primitiveLoops` is empty. |
| `closureErrorRms`    | number           | **Required.** RMS of `closureErrorMm` values, in mm. `0` when `primitiveLoops` is empty. |

### 8.1 `PrimitiveLoop` Object

Each element of `primitiveLoops` describes one independent closure loop and its
signed closure error.

```json
{
  "nodeIds":        ["center", "armE_seg1", "armNE_seg1"],
  "closureErrorMm": 0.0000031
}
```

| Field            | Type             | Description |
|------------------|------------------|-------------|
| `nodeIds`        | array of strings | Ordered list of node ids forming the loop. For triangular loops: 3 entries. For rectangular loops: 4 entries. Consecutive pairs represent single measured steps (or their reverses). |
| `closureErrorMm` | number           | Signed closure error in mm: Σ(reading_mm_per_m × step_distance_m) around the loop, traversed in the order given by `nodeIds`. |

---

## 9. `parallelWaysResult` Object — `ParallelWaysResult`

Present for Parallel Ways sessions when the calculation has been run; `null` otherwise. Absent for Surface Plate sessions.

```json
"parallelWaysResult": {
  "railProfiles": [
    {
      "railIndex":           0,
      "heightProfileMm":     [ 0.0, 0.0012, 0.0031, 0.0018, 0.0, -0.0005, 0.0 ],
      "stationPositionsMm":  [ 0, 200, 400, 600, 800, 1000 ],
      "straightnessValueMm": 0.0036
    }
  ],
  "parallelismProfiles": [
    {
      "railIndexA":          0,
      "railIndexB":          1,
      "deviationMm":         [ 0.0, 0.0005, -0.0008, 0.0003, 0.0 ],
      "stationPositionsMm":  [ 0, 200, 400, 600, 800 ],
      "parallelismValueMm":  0.0013
    }
  ],
  "residuals":          [ 1.2e-6, -3.4e-6, ... ],
  "flaggedStepIndices": [],
  "sigmaThreshold":     3.0,
  "residualRms":        0.0000042
}
```

### 9.1 `RailProfile` Object

| Field                 | Type             | Description |
|-----------------------|------------------|-------------|
| `railIndex`           | number (integer) | 0-based rail index. |
| `heightProfileMm`     | array of numbers | Height of each station in mm after best-fit line removal. Length = number of stations on this rail. |
| `stationPositionsMm`  | array of numbers | Physical position of each station along the rail in mm. Length matches `heightProfileMm`. |
| `straightnessValueMm` | number           | Peak-to-valley of `heightProfileMm`. Always ≥ 0. |

### 9.2 `ParallelismProfile` Object

| Field                | Type             | Description |
|----------------------|------------------|-------------|
| `railIndexA`         | number (integer) | 0-based index of the reference rail in this pair. |
| `railIndexB`         | number (integer) | 0-based index of the secondary rail in this pair. |
| `deviationMm`        | array of numbers | Height difference `hB − hA` at each common station, in mm. |
| `stationPositionsMm` | array of numbers | Physical position of each station in mm. |
| `parallelismValueMm` | number           | Peak-to-valley of `deviationMm`. Always ≥ 0. |

### 9.3 Top-level fields

| Field                | Type             | Required | Description |
|----------------------|------------------|----------|-------------|
| `railProfiles`       | array            | **Yes**  | One `RailProfile` per rail. |
| `parallelismProfiles`| array            | **Yes**  | One `ParallelismProfile` per adjacent rail pair. |
| `residuals`          | array of numbers | **Yes**  | One signed residual in mm per step (same order as `steps`). |
| `flaggedStepIndices` | array of integers | **Yes** | `index` values of steps flagged as outliers. |
| `sigmaThreshold`     | number           | **Yes**  | Outlier-detection multiplier used during calculation. Default `3.0`. |
| `residualRms`        | number           | **Yes**  | RMS of all residuals in mm. |

---

## 10. `corrections` Array — `CorrectionRound` Objects

```json
{
  "id":           "c3d4e5f6-...",
  "triggeredAt":  "2026-04-06T08:30:00.000Z",
  "operator":     "Lukas",
  "notes":        "",
  "replacedSteps": [
    { "originalStepIndex": 3, "reading": 0.0042 }
  ],
  "result": { ... }
}
```

| Field           | Type            | Required | Description |
|-----------------|-----------------|----------|-------------|
| `id`            | string (UUID)   | **Yes**  | Unique correction-round identifier. |
| `triggeredAt`   | string (ISO 8601) | **Yes** | UTC timestamp when the correction session was started. |
| `operator`      | string          | **Yes**  | Operator name. May be empty string `""`. |
| `notes`         | string          | **Yes**  | Free-text notes. May be empty string `""`. |
| `replacedSteps` | array           | **Yes**  | One or more `ReplacedStep` entries (see §10.1). Never empty: a correction round always replaces at least one step. |
| `result`        | object \| `null` | **Yes** | `SurfaceResult` from re-running the calculation with the replaced readings merged in; `null` if not yet calculated. Same structure as §8. |

### 10.1 `ReplacedStep` Object

```json
{ "originalStepIndex": 3, "reading": 0.0042 }
```

| Field               | Type             | Description |
|---------------------|------------------|-------------|
| `originalStepIndex` | number (integer) | The `index` value of the step in `initialRound.steps` that this entry replaces. |
| `reading`           | number           | New instrument reading in mm/m. |

The corrected result is computed by taking `initialRound.steps`, substituting
each listed `originalStepIndex` with its new `reading`, and re-running the
calculation. Original readings in `initialRound.steps` are **never modified**.
Multiple correction rounds chain: the latest `CorrectionRound` with a non-null
`result` is the current best result.

---

## 11. `orientation` Encoding

The `orientation` field in a `MeasurementStep` describes the direction from the
*from* node to the *to* node as the instrument is traversed.

**Current format (v0.4.0+):** written and read as a string name.

| String value  | Meaning |
|---------------|---------|
| `"East"`      | Towards higher column index (+X) |
| `"West"`      | Towards lower column index (−X) |
| `"South"`     | Towards higher row index (+Y, downward) |
| `"North"`     | Towards lower row index (−Y, upward) |
| `"SouthEast"` | Diagonal: +X and +Y (used by Union Jack SE-diagonal arm) |
| `"SouthWest"` | Diagonal: −X and +Y (used by Union Jack NE-diagonal arm) |
| `"NorthEast"` | Diagonal: +X and −Y |
| `"NorthWest"` | Diagonal: −X and −Y |

Full Grid sessions use only `"East"`, `"West"`, `"South"`, `"North"`.
Union Jack sessions additionally use `"SouthEast"` and `"SouthWest"`.
`"NorthEast"` and `"NorthWest"` are defined but not currently produced by any
implemented strategy.

**Legacy format (pre-v0.4.0):** written as a JSON integer. Only the four cardinal
directions were supported.

| Integer | Equivalent string |
|---------|-------------------|
| `0`     | `"North"` |
| `1`     | `"South"` |
| `2`     | `"East"`  |
| `3`     | `"West"`  |

The app reads both formats; it always writes the string format.

---

## 12. Node-Id Naming Conventions

Node ids are strings that uniquely identify measurement grid nodes. They appear
in `MeasurementStep.nodeId`, `MeasurementStep.toNodeId`,
`SurfaceResult.nodeHeights` keys, and `PrimitiveLoop.nodeIds` entries.

### 12.1 Full Grid nodes

```
"col{c}_row{r}"
```

- `{c}` — 0-based column index, 0 ≤ c < `columnsCount`
- `{r}` — 0-based row index, 0 ≤ r < `rowsCount`

Example: `"col0_row0"` (top-left), `"col8_row5"` (bottom-right of a 9×6 grid).

### 12.2 Union Jack nodes

**Center node:**
```
"center"
```
There is exactly one center node per Union Jack session.

**Arm nodes:**
```
"arm{Dir}_seg{k}"
```

- `{Dir}` — arm direction code (one of eight values):

  | Code | Direction          | Physical position relative to plate center |
  |------|--------------------|--------------------------------------------|
  | `E`  | East               | (cx + k·w/(2s), cy) |
  | `W`  | West               | (cx − k·w/(2s), cy) |
  | `S`  | South              | (cx, cy + k·h/(2s)) |
  | `N`  | North              | (cx, cy − k·h/(2s)) |
  | `SE` | South-East         | (cx + k·w/(2s), cy + k·h/(2s)) |
  | `SW` | South-West         | (cx − k·w/(2s), cy + k·h/(2s)) |
  | `NE` | North-East         | (cx + k·w/(2s), cy − k·h/(2s)) |
  | `NW` | North-West         | (cx − k·w/(2s), cy − k·h/(2s)) |

  where cx = widthMm/2, cy = heightMm/2, s = segments, w = widthMm, h = heightMm.

- `{k}` — 1-based segment index, 1 ≤ k ≤ `segments`.
  k = 1 is adjacent to center; k = `segments` is the arm tip at the plate boundary.

Examples: `"armE_seg1"`, `"armSW_seg3"`, `"armNW_seg2"`.

The complete set of Union Jack nodes for a session with `s` segments is:
- 1 center node
- 8 × s arm nodes = 8s nodes
- **Total:** 8s + 1 nodes

### 12.3 Parallel Ways nodes

```
"rail{r}_sta{s}"
```

- `{r}` — 0-based rail index, matching the order in `objectDefinition.parameters.rails[]`
- `{s}` — 0-based station index along the rail (0 = start, `n` = end)

The station count for rail `r` with length `L` mm and step distance `d` mm is `floor(L / d) + 1`.

Examples: `"rail0_sta0"` (first station on rail 0), `"rail1_sta5"` (sixth station on rail 1).

Bridge steps always go from a lower-indexed rail to a higher-indexed rail at the same station:
`nodeId = "rail{rA}_sta{s}"`, `toNodeId = "rail{rB}_sta{s}"` where `rA < rB`.

---

## 13. Complete Minimal Examples

### 13.1 Surface Plate — Full Grid, 4×3, in-progress (no result)

```json
{
  "schemaVersion": "1.0",
  "appVersion": "0.4.0",
  "project": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Workshop Granite Plate",
    "createdAt": "2026-04-08T09:00:00.000Z",
    "modifiedAt": "2026-04-08T09:00:00.000Z",
    "operator": "J. Smith",
    "notes": "",
    "objectDefinition": {
      "geometryModuleId": "SurfacePlate",
      "parameters": {
        "widthMm":      630,
        "heightMm":     400,
        "columnsCount": 4,
        "rowsCount":    3
      }
    },
    "measurements": [
      {
        "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
        "label": "Measurement 1",
        "takenAt": "2026-04-08T09:00:00.000Z",
        "operator": "J. Smith",
        "instrumentId": "manual-entry",
        "strategyId": "FullGrid",
        "notes": "",
        "initialRound": {
          "completedAt": null,
          "steps": [
            {
              "index": 0, "gridCol": 0, "gridRow": 0,
              "orientation": "East",
              "instructionText": "Row pass — row 1, instrument at column 1 → 2, facing East",
              "reading": null,
              "nodeId": "col0_row0", "toNodeId": "col1_row0"
            }
          ],
          "result": null
        },
        "corrections": []
      }
    ]
  }
}
```

(Only step index 0 shown; a complete 4×3 grid has 17 steps: 9 row-pass + 8 column-pass.)

---

### 13.2 Surface Plate — Union Jack, segments=1, rings="Circumference", with result

```json
{
  "schemaVersion": "1.0",
  "appVersion": "0.4.0",
  "project": {
    "id": "c3d4e5f6-a7b8-9012-cdef-123456789012",
    "name": "Precision Plate UJ",
    "createdAt": "2026-04-08T10:00:00.000Z",
    "modifiedAt": "2026-04-08T10:30:00.000Z",
    "operator": "L. Fässler",
    "notes": "",
    "objectDefinition": {
      "geometryModuleId": "SurfacePlate",
      "parameters": {
        "widthMm":  1200,
        "heightMm": 800,
        "segments": 1,
        "rings":    "Circumference"
      }
    },
    "measurements": [
      {
        "id": "d4e5f6a7-b8c9-0123-defa-234567890123",
        "label": "Measurement 1",
        "takenAt": "2026-04-08T10:00:00.000Z",
        "operator": "L. Fässler",
        "instrumentId": "manual-entry",
        "strategyId": "UnionJack",
        "notes": "",
        "initialRound": {
          "completedAt": "2026-04-08T10:30:00.000Z",
          "steps": [
            { "index": 0, "gridCol": 0, "gridRow": 0, "orientation": "East",
              "instructionText": "H arm — W side, seg 1→0, facing East",
              "reading": 0.012, "nodeId": "armW_seg1", "toNodeId": "center" },
            { "index": 1, "gridCol": 0, "gridRow": 0, "orientation": "East",
              "instructionText": "H arm — E side, seg 0→1, facing East",
              "reading": 0.0, "nodeId": "center", "toNodeId": "armE_seg1" },
            { "index": 2, "gridCol": 0, "gridRow": 0, "orientation": "South",
              "instructionText": "V arm — N side, seg 1→0, facing South",
              "reading": 0.001, "nodeId": "armN_seg1", "toNodeId": "center" },
            { "index": 3, "gridCol": 0, "gridRow": 0, "orientation": "South",
              "instructionText": "V arm — S side, seg 0→1, facing South",
              "reading": 0.0, "nodeId": "center", "toNodeId": "armS_seg1" },
            { "index": 4, "gridCol": 0, "gridRow": 0, "orientation": "SouthEast",
              "instructionText": "SE diag — NW side, seg 1→0, facing SE",
              "reading": -0.012, "nodeId": "armNW_seg1", "toNodeId": "center" },
            { "index": 5, "gridCol": 0, "gridRow": 0, "orientation": "SouthEast",
              "instructionText": "SE diag — SE side, seg 0→1, facing SE",
              "reading": -0.0125, "nodeId": "center", "toNodeId": "armSE_seg1" },
            { "index": 6, "gridCol": 0, "gridRow": 0, "orientation": "SouthWest",
              "instructionText": "NE diag — NE side, seg 1→0, facing SW",
              "reading": 0.0, "nodeId": "armNE_seg1", "toNodeId": "center" },
            { "index": 7, "gridCol": 0, "gridRow": 0, "orientation": "SouthWest",
              "instructionText": "NE diag — SW side, seg 0→1, facing SW",
              "reading": 0.0, "nodeId": "center", "toNodeId": "armSW_seg1" },
            { "index": 8, "gridCol": 0, "gridRow": 0, "orientation": "East",
              "instructionText": "Ring 1 — SW→S (East)",
              "reading": 0.005, "nodeId": "armSW_seg1", "toNodeId": "armS_seg1" },
            { "index": 9, "gridCol": 0, "gridRow": 0, "orientation": "East",
              "instructionText": "Ring 1 — S→SE (East)",
              "reading": -0.003, "nodeId": "armS_seg1", "toNodeId": "armSE_seg1" },
            { "index": 10, "gridCol": 0, "gridRow": 0, "orientation": "North",
              "instructionText": "Ring 1 — SE→E (North)",
              "reading": 0.002, "nodeId": "armSE_seg1", "toNodeId": "armE_seg1" },
            { "index": 11, "gridCol": 0, "gridRow": 0, "orientation": "North",
              "instructionText": "Ring 1 — E→NE (North)",
              "reading": -0.001, "nodeId": "armE_seg1", "toNodeId": "armNE_seg1" },
            { "index": 12, "gridCol": 0, "gridRow": 0, "orientation": "West",
              "instructionText": "Ring 1 — NE→N (West)",
              "reading": 0.004, "nodeId": "armNE_seg1", "toNodeId": "armN_seg1" },
            { "index": 13, "gridCol": 0, "gridRow": 0, "orientation": "West",
              "instructionText": "Ring 1 — N→NW (West)",
              "reading": -0.002, "nodeId": "armN_seg1", "toNodeId": "armNW_seg1" },
            { "index": 14, "gridCol": 0, "gridRow": 0, "orientation": "South",
              "instructionText": "Ring 1 — NW→W (South)",
              "reading": 0.001, "nodeId": "armNW_seg1", "toNodeId": "armW_seg1" },
            { "index": 15, "gridCol": 0, "gridRow": 0, "orientation": "South",
              "instructionText": "Ring 1 — W→SW (South)",
              "reading": -0.005, "nodeId": "armW_seg1", "toNodeId": "armSW_seg1" }
          ],
          "result": {
            "nodeHeights": {
              "armW_seg1":  0.0,
              "center":     0.007,
              "armE_seg1":  0.007,
              "armN_seg1":  0.0068,
              "armS_seg1":  0.0072,
              "armNW_seg1": 0.0158,
              "armSE_seg1": -0.0018,
              "armNE_seg1": 0.0072,
              "armSW_seg1": 0.0072
            },
            "flatnessValueMm":    0.01760,
            "residuals":          [ 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
                                    0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 ],
            "flaggedStepIndices": [],
            "sigmaThreshold":     2.5,
            "sigma":              0.0,
            "primitiveLoops": [
              { "nodeIds": ["center", "armE_seg1",  "armNE_seg1"], "closureErrorMm":  0.000001 },
              { "nodeIds": ["center", "armNE_seg1", "armN_seg1"],  "closureErrorMm": -0.000001 },
              { "nodeIds": ["center", "armS_seg1",  "armSE_seg1"], "closureErrorMm":  0.000002 },
              { "nodeIds": ["center", "armSE_seg1", "armE_seg1"],  "closureErrorMm": -0.000002 },
              { "nodeIds": ["center", "armW_seg1",  "armSW_seg1"], "closureErrorMm":  0.000001 },
              { "nodeIds": ["center", "armSW_seg1", "armS_seg1"],  "closureErrorMm": -0.000001 },
              { "nodeIds": ["center", "armN_seg1",  "armNW_seg1"], "closureErrorMm":  0.000003 },
              { "nodeIds": ["center", "armNW_seg1", "armW_seg1"],  "closureErrorMm": -0.000003 }
            ],
            "closureErrorMean":   0.000001875,
            "closureErrorMedian": 0.0000015,
            "closureErrorMax":    0.000003,
            "closureErrorRms":    0.000002121
          }
        },
        "corrections": []
      }
    ]
  }
}
```

### 13.3 Parallel Ways — 2 rails, 5 stations, in-progress

```json
{
  "schemaVersion": "1.1",
  "appVersion": "0.7.0",
  "project": {
    "id": "e5f6a7b8-c9d0-1234-efab-345678901234",
    "name": "Machine Bed Rails",
    "createdAt": "2026-04-09T08:00:00.000Z",
    "modifiedAt": "2026-04-09T08:00:00.000Z",
    "operator": "L. Fässler",
    "notes": "",
    "objectDefinition": {
      "geometryModuleId": "ParallelWays",
      "parameters": {
        "orientation":        "Horizontal",
        "referenceRailIndex": 0,
        "rails": [
          { "label": "A", "lengthMm": 1000, "lateralSeparationMm": 0,   "verticalOffsetMm": 0, "axialOffsetMm": 0 },
          { "label": "B", "lengthMm": 1000, "lateralSeparationMm": 400, "verticalOffsetMm": 0, "axialOffsetMm": 0 }
        ],
        "tasks": [
          { "taskType": "AlongRail", "railIndexA": 0, "railIndexB": 1, "stepDistanceMm": 250, "passDirection": "SinglePass" },
          { "taskType": "AlongRail", "railIndexA": 1, "railIndexB": 1, "stepDistanceMm": 250, "passDirection": "SinglePass" }
        ],
        "driftCorrection": "LeastSquares",
        "solverMode":      "GlobalLeastSquares"
      }
    },
    "measurements": [
      {
        "id": "f6a7b8c9-d0e1-2345-fabc-456789012345",
        "label": "Measurement 1",
        "takenAt": "2026-04-09T08:00:00.000Z",
        "operator": "L. Fässler",
        "instrumentId": "manual-entry",
        "strategyId": "ParallelWays",
        "notes": "",
        "initialRound": {
          "completedAt": null,
          "steps": [
            { "index": 0, "gridCol": 0, "gridRow": 0,
              "orientation": "East", "passPhase": "NotApplicable",
              "instructionText": "Rail A — sta 0→1, facing East",
              "reading": null, "nodeId": "rail0_sta0", "toNodeId": "rail0_sta1" },
            { "index": 1, "gridCol": 0, "gridRow": 0,
              "orientation": "East", "passPhase": "NotApplicable",
              "instructionText": "Rail A — sta 1→2, facing East",
              "reading": null, "nodeId": "rail0_sta1", "toNodeId": "rail0_sta2" }
          ],
          "result": null,
          "parallelWaysResult": null
        },
        "corrections": []
      }
    ]
  }
}
```

(Only steps 0–1 shown; a 2-rail, 250 mm step, 1000 mm session has 8 along-rail steps total.)

---

## 14. Summary of Required vs Optional Fields

| Path | Required | Notes |
|------|----------|-------|
| `schemaVersion` | **Yes** | Must be `"1.0"` or `"1.1"` |
| `appVersion` | No | Present from v0.4.0 |
| `project.id` | **Yes** | |
| `project.name` | **Yes** | May be `""` |
| `project.createdAt` | **Yes** | |
| `project.modifiedAt` | **Yes** | |
| `project.operator` | **Yes** | May be `""` |
| `project.notes` | **Yes** | May be `""` |
| `project.objectDefinition` | **Yes** | |
| `project.measurements` | **Yes** | May be `[]` |
| `objectDefinition.geometryModuleId` | **Yes** | |
| `objectDefinition.parameters` | **Yes** | |
| `parameters.widthMm` | **Yes** | SurfacePlate only |
| `parameters.heightMm` | **Yes** | SurfacePlate only |
| `parameters.columnsCount` | **Yes** | FullGrid only |
| `parameters.rowsCount` | **Yes** | FullGrid only |
| `parameters.segments` | **Yes** | UnionJack only |
| `parameters.rings` | **Yes** | UnionJack only; string value |
| `parameters.orientation` | **Yes** | ParallelWays only |
| `parameters.referenceRailIndex` | **Yes** | ParallelWays only |
| `parameters.rails` | **Yes** | ParallelWays only; min 2 entries |
| `parameters.tasks` | **Yes** | ParallelWays only; min 1 entry |
| `parameters.driftCorrection` | **Yes** | ParallelWays only |
| `parameters.solverMode` | **Yes** | ParallelWays only |
| `MeasurementSession.id` | **Yes** | |
| `MeasurementSession.label` | **Yes** | |
| `MeasurementSession.takenAt` | **Yes** | |
| `MeasurementSession.operator` | **Yes** | May be `""` |
| `MeasurementSession.instrumentId` | **Yes** | |
| `MeasurementSession.strategyId` | **Yes** | `"FullGrid"`, `"UnionJack"`, or `"ParallelWays"` |
| `MeasurementSession.notes` | **Yes** | May be `""` |
| `MeasurementSession.initialRound` | **Yes** | |
| `MeasurementSession.corrections` | **Yes** | May be `[]` |
| `MeasurementRound.completedAt` | **Yes** | `null` if in-progress |
| `MeasurementRound.steps` | **Yes** | |
| `MeasurementRound.result` | **Yes** | `null` if not calculated or N/A (ParallelWays) |
| `MeasurementRound.parallelWaysResult` | **Yes** | ParallelWays only; `null` if not calculated |
| `MeasurementStep.index` | **Yes** | |
| `MeasurementStep.gridCol` | **Yes** | |
| `MeasurementStep.gridRow` | **Yes** | |
| `MeasurementStep.orientation` | **Yes** | String (v0.4.0+) or legacy integer |
| `MeasurementStep.passPhase` | **Yes** | ParallelWays (v1.1+); defaults to `"NotApplicable"` for v1.0 |
| `MeasurementStep.instructionText` | **Yes** | May be `""` |
| `MeasurementStep.reading` | **Yes** | `null` if not yet recorded |
| `MeasurementStep.nodeId` | **Yes*** | See note in §7.1 |
| `MeasurementStep.toNodeId` | **Yes*** | See note in §7.1 |
| `SurfaceResult.*` (all fields) | **Yes** | When `result` is not `null` (SurfacePlate) |
| `ParallelWaysResult.*` (all fields) | **Yes** | When `parallelWaysResult` is not `null` |
| `CorrectionRound.id` | **Yes** | |
| `CorrectionRound.triggeredAt` | **Yes** | |
| `CorrectionRound.operator` | **Yes** | May be `""` |
| `CorrectionRound.notes` | **Yes** | May be `""` |
| `CorrectionRound.replacedSteps` | **Yes** | Non-empty |
| `CorrectionRound.result` | **Yes** | `null` if not yet calculated |
| `ReplacedStep.originalStepIndex` | **Yes** | |
| `ReplacedStep.reading` | **Yes** | |
| `PrimitiveLoop.nodeIds` | **Yes** | |
| `PrimitiveLoop.closureErrorMm` | **Yes** | |
