using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.SurfacePlate.Strategies;

/// <summary>
/// Union Jack measurement strategy.
///
/// Parameterised by <c>segments</c> (steps from center to each arm tip, ≥1)
/// and <c>rings</c> (<see cref="UnionJackRings"/> stored as a string).
///
/// Node positions (origin at plate bottom-left):
///   Center:              (width/2, height/2)
///   Arm E, seg k:        (width/2 + k×width/(2×segments),  height/2)
///   Arm W, seg k:        (width/2 − k×width/(2×segments),  height/2)
///   Arm S, seg k:        (width/2,  height/2 + k×height/(2×segments))
///   Arm N, seg k:        (width/2,  height/2 − k×height/(2×segments))
///   Arm SE, seg k:       (width/2 + k×w/(2s),  height/2 + k×h/(2s))
///   Arm SW, seg k:       (width/2 − k×w/(2s),  height/2 + k×h/(2s))
///   Arm NE, seg k:       (width/2 + k×w/(2s),  height/2 − k×h/(2s))
///   Arm NW, seg k:       (width/2 − k×w/(2s),  height/2 − k×h/(2s))
///
/// Step sequence:
///   Pass 0  — H arm:    W-tip → center → E-tip  (East,      2×segments steps)
///   Pass 1  — V arm:    N-tip → center → S-tip  (South,     2×segments steps)
///   Pass 2  — SE diag:  NW-tip → SE-tip          (SouthEast, 2×segments steps)
///   Pass 3  — NE diag:  NE-tip → SW-tip          (SouthWest, 2×segments steps)
///   Rings option:
///     None          — no further passes
///     Circumference — pass 4: one clockwise ring at r = segments (8 steps)
///     Full          — passes 4…3+segments: rings r = 1…segments (8 steps each)
///
/// Total steps:
///   None          = 8 × segments
///   Circumference = 8 × segments + 8
///   Full          = 8 × segments + 8 × segments = 16 × segments
/// </summary>
public sealed class UnionJackStrategy : IMeasurementStrategy
{
    public string StrategyId  => "UnionJack";
    public string DisplayName => "Union Jack";

    // ── Step generation ───────────────────────────────────────────────────────

    public IReadOnlyList<MeasurementStep> GenerateSteps(ObjectDefinition definition)
    {
        int segments     = Convert.ToInt32(definition.Parameters["segments"]);
        var ringsOption  = ParseRingsOption(definition.Parameters["rings"]);

        if (segments < 1)
            throw new ArgumentException("segments must be at least 1.", nameof(definition));

        int ringStepCount = ringsOption switch
        {
            UnionJackRings.None          => 0,
            UnionJackRings.Circumference => 1,   // single ring at r = segments
            UnionJackRings.Full          => segments,
            _                            => 0
        };

        var steps = new List<MeasurementStep>(8 * segments + 8 * ringStepCount);
        int index = 0;

        // ── Pass 0: Horizontal arm — W-tip → center → E-tip (East) ───────────
        int pass = 0;
        for (int k = segments; k >= 1; k--)
        {
            string from = $"armW_seg{k}";
            string to   = k == 1 ? "center" : $"armW_seg{k - 1}";
            steps.Add(MakeStep(index++, from, to, Orientation.East, pass,
                $"H arm — W side, seg {k}→{k - 1}, facing East"));
        }
        for (int k = 1; k <= segments; k++)
        {
            string from = k == 1 ? "center" : $"armE_seg{k - 1}";
            string to   = $"armE_seg{k}";
            steps.Add(MakeStep(index++, from, to, Orientation.East, pass,
                $"H arm — E side, seg {k - 1}→{k}, facing East"));
        }

        // ── Pass 1: Vertical arm — N-tip → center → S-tip (South) ────────────
        pass = 1;
        for (int k = segments; k >= 1; k--)
        {
            string from = $"armN_seg{k}";
            string to   = k == 1 ? "center" : $"armN_seg{k - 1}";
            steps.Add(MakeStep(index++, from, to, Orientation.South, pass,
                $"V arm — N side, seg {k}→{k - 1}, facing South"));
        }
        for (int k = 1; k <= segments; k++)
        {
            string from = k == 1 ? "center" : $"armS_seg{k - 1}";
            string to   = $"armS_seg{k}";
            steps.Add(MakeStep(index++, from, to, Orientation.South, pass,
                $"V arm — S side, seg {k - 1}→{k}, facing South"));
        }

        // ── Pass 2: SE diagonal — NW-tip → center → SE-tip (SouthEast) ──────
        pass = 2;
        for (int k = segments; k >= 1; k--)
        {
            string from = $"armNW_seg{k}";
            string to   = k == 1 ? "center" : $"armNW_seg{k - 1}";
            steps.Add(MakeStep(index++, from, to, Orientation.SouthEast, pass,
                $"SE diag — NW side, seg {k}→{k - 1}, facing SE"));
        }
        for (int k = 1; k <= segments; k++)
        {
            string from = k == 1 ? "center" : $"armSE_seg{k - 1}";
            string to   = $"armSE_seg{k}";
            steps.Add(MakeStep(index++, from, to, Orientation.SouthEast, pass,
                $"SE diag — SE side, seg {k - 1}→{k}, facing SE"));
        }

        // ── Pass 3: NE diagonal — NE-tip → center → SW-tip (SouthWest) ──────
        pass = 3;
        for (int k = segments; k >= 1; k--)
        {
            string from = $"armNE_seg{k}";
            string to   = k == 1 ? "center" : $"armNE_seg{k - 1}";
            steps.Add(MakeStep(index++, from, to, Orientation.SouthWest, pass,
                $"NE diag — NE side, seg {k}→{k - 1}, facing SW"));
        }
        for (int k = 1; k <= segments; k++)
        {
            string from = k == 1 ? "center" : $"armSW_seg{k - 1}";
            string to   = $"armSW_seg{k}";
            steps.Add(MakeStep(index++, from, to, Orientation.SouthWest, pass,
                $"NE diag — SW side, seg {k - 1}→{k}, facing SW"));
        }

        // ── Ring passes (clockwise, 8 steps each) ─────────────────────────────
        // Circumference: one ring at r = segments.
        // Full:          rings r = 1 … segments.
        int ringPassId  = 4;
        int ringStart   = ringsOption == UnionJackRings.Circumference ? segments : 1;
        int ringEnd     = ringsOption == UnionJackRings.None          ? 0        : segments;

        for (int r = ringStart; r <= ringEnd; r++)
        {
            // Bottom edge (East): SW → S → SE
            steps.Add(MakeStep(index++, $"armSW_seg{r}", $"armS_seg{r}",  Orientation.East,  ringPassId, $"Ring {r} — SW→S (East)"));
            steps.Add(MakeStep(index++, $"armS_seg{r}",  $"armSE_seg{r}", Orientation.East,  ringPassId, $"Ring {r} — S→SE (East)"));
            // Right edge (North): SE → E → NE
            steps.Add(MakeStep(index++, $"armSE_seg{r}", $"armE_seg{r}",  Orientation.North, ringPassId, $"Ring {r} — SE→E (North)"));
            steps.Add(MakeStep(index++, $"armE_seg{r}",  $"armNE_seg{r}", Orientation.North, ringPassId, $"Ring {r} — E→NE (North)"));
            // Top edge (West): NE → N → NW
            steps.Add(MakeStep(index++, $"armNE_seg{r}", $"armN_seg{r}",  Orientation.West,  ringPassId, $"Ring {r} — NE→N (West)"));
            steps.Add(MakeStep(index++, $"armN_seg{r}",  $"armNW_seg{r}", Orientation.West,  ringPassId, $"Ring {r} — N→NW (West)"));
            // Left edge (South): NW → W → SW
            steps.Add(MakeStep(index++, $"armNW_seg{r}", $"armW_seg{r}",  Orientation.South, ringPassId, $"Ring {r} — NW→W (South)"));
            steps.Add(MakeStep(index++, $"armW_seg{r}",  $"armSW_seg{r}", Orientation.South, ringPassId, $"Ring {r} — W→SW (South)"));
            ringPassId++;
        }

        return steps.AsReadOnly();
    }

    // ── Node position ─────────────────────────────────────────────────────────

    public (double X, double Y) GetNodePosition(MeasurementStep step, ObjectDefinition definition)
        => NodePositionById(step.NodeId, definition);

    public (double X, double Y) GetToNodePosition(MeasurementStep step, ObjectDefinition definition)
        => NodePositionById(step.ToNodeId, definition);

    // ── Primitive loop enumeration ────────────────────────────────────────────

    /// <summary>
    /// Returns primitive (unit) closure loops for the Union Jack pattern.
    ///
    /// <b>None</b>: no loops.
    ///
    /// <b>Circumference</b>: only the outermost ring (r = segments) is measured.
    ///   Unit triangles exist only when segments = 1 (the ring touches center).
    ///   For segments &gt; 1 the ring edges do not form unit-step triangles with the
    ///   arm passes, so an empty list is returned.
    ///
    /// <b>Full</b> (rings r = 1…segments):
    ///   • 8 triangles per ring level — 8 × segments total.
    ///   • 8 unit rectangles per adjacent ring pair — 8 × (segments − 1) total.
    ///   Grand total = 8 × segments + 8 × (segments − 1).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> GetPrimitiveLoopNodeIds(ObjectDefinition definition)
    {
        int segments    = Convert.ToInt32(definition.Parameters["segments"]);
        var ringsOption = ParseRingsOption(definition.Parameters["rings"]);
        var loops       = new List<IReadOnlyList<string>>();

        switch (ringsOption)
        {
            case UnionJackRings.None:
                break;  // no loops

            case UnionJackRings.Circumference:
                // Unit triangles are only possible when segments = 1, because then
                // the single ring (at r = 1 = segments) touches the center node and
                // every triangle edge is a single measured step.
                if (segments == 1)
                    AddTrianglesAtRing(loops, r: 1, innerNodeForNE: "center",
                                                    innerNodeForSE: "center",
                                                    innerNodeForSW: "center",
                                                    innerNodeForNW: "center");
                // For segments > 1 the ring sits at r = segments; the triangle edges
                // from inner diagonal nodes to cardinal nodes at r = segments span
                // multiple arm steps and are not unit loops.
                break;

            case UnionJackRings.Full:
                // Triangles at each ring level
                for (int r = 1; r <= segments; r++)
                {
                    string ne = r == 1 ? "center" : $"armNE_seg{r - 1}";
                    string se = r == 1 ? "center" : $"armSE_seg{r - 1}";
                    string sw = r == 1 ? "center" : $"armSW_seg{r - 1}";
                    string nw = r == 1 ? "center" : $"armNW_seg{r - 1}";
                    AddTrianglesAtRing(loops, r, ne, se, sw, nw);
                }
                // Unit rectangles between adjacent ring levels
                for (int r = 1; r < segments; r++)
                {
                    // NE sector
                    loops.Add(new[] { $"armE_seg{r}",  $"armE_seg{r + 1}",  $"armNE_seg{r + 1}", $"armNE_seg{r}" });
                    loops.Add(new[] { $"armNE_seg{r}", $"armNE_seg{r + 1}", $"armN_seg{r + 1}",  $"armN_seg{r}"  });
                    // SE sector
                    loops.Add(new[] { $"armS_seg{r}",  $"armSE_seg{r}",     $"armSE_seg{r + 1}", $"armS_seg{r + 1}"  });
                    loops.Add(new[] { $"armSE_seg{r}", $"armE_seg{r}",      $"armE_seg{r + 1}",  $"armSE_seg{r + 1}" });
                    // SW sector
                    loops.Add(new[] { $"armW_seg{r}",  $"armSW_seg{r}",     $"armSW_seg{r + 1}", $"armW_seg{r + 1}"  });
                    loops.Add(new[] { $"armSW_seg{r}", $"armS_seg{r}",      $"armS_seg{r + 1}",  $"armSW_seg{r + 1}" });
                    // NW sector
                    loops.Add(new[] { $"armN_seg{r}",  $"armNW_seg{r}",     $"armNW_seg{r + 1}", $"armN_seg{r + 1}"  });
                    loops.Add(new[] { $"armNW_seg{r}", $"armW_seg{r}",      $"armW_seg{r + 1}",  $"armNW_seg{r + 1}" });
                }
                break;
        }

        return loops;
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the <c>rings</c> parameter from an <see cref="ObjectDefinition"/>.
    /// Accepts the string names (<c>"None"</c>, <c>"Circumference"</c>, <c>"Full"</c>)
    /// or legacy integer values (0 → None; any positive value → Full).
    /// </summary>
    public static UnionJackRings ParseRingsOption(object value)
    {
        if (value is string s && Enum.TryParse<UnionJackRings>(s, ignoreCase: true, out var parsed))
            return parsed;
        throw new ArgumentException($"Unrecognized rings value '{value}'.", nameof(value));
    }

    /// <summary>
    /// Returns the physical position of a named node given the plate dimensions and segments.
    /// </summary>
    public static (double X, double Y) NodePositionById(string nodeId, ObjectDefinition definition)
    {
        double w = Convert.ToDouble(definition.Parameters["widthMm"]);
        double h = Convert.ToDouble(definition.Parameters["heightMm"]);
        int    s = Convert.ToInt32(definition.Parameters["segments"]);

        if (nodeId == "center")
            return (w / 2.0, h / 2.0);

        if (!nodeId.StartsWith("arm"))
            throw new ArgumentException($"Unrecognised Union Jack node id '{nodeId}'.");

        int    sepIdx = nodeId.IndexOf("_seg", StringComparison.Ordinal);
        string dir    = nodeId[3..sepIdx];
        int    k      = int.Parse(nodeId[(sepIdx + 4)..]);

        double dx = k * w / (2.0 * s);
        double dy = k * h / (2.0 * s);
        double cx = w / 2.0;
        double cy = h / 2.0;

        return dir switch
        {
            "E"  => (cx + dx, cy),
            "W"  => (cx - dx, cy),
            "S"  => (cx,      cy + dy),
            "N"  => (cx,      cy - dy),
            "SE" => (cx + dx, cy + dy),
            "SW" => (cx - dx, cy + dy),
            "NE" => (cx + dx, cy - dy),
            "NW" => (cx - dx, cy - dy),
            _    => throw new ArgumentException($"Unknown arm direction '{dir}' in node id '{nodeId}'.")
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void AddTrianglesAtRing(
        List<IReadOnlyList<string>> loops, int r,
        string innerNodeForNE, string innerNodeForSE,
        string innerNodeForSW, string innerNodeForNW)
    {
        loops.Add(new[] { innerNodeForNE, $"armE_seg{r}",  $"armNE_seg{r}" });
        loops.Add(new[] { innerNodeForNE, $"armNE_seg{r}", $"armN_seg{r}"  });
        loops.Add(new[] { innerNodeForSE, $"armS_seg{r}",  $"armSE_seg{r}" });
        loops.Add(new[] { innerNodeForSE, $"armSE_seg{r}", $"armE_seg{r}"  });
        loops.Add(new[] { innerNodeForSW, $"armW_seg{r}",  $"armSW_seg{r}" });
        loops.Add(new[] { innerNodeForSW, $"armSW_seg{r}", $"armS_seg{r}"  });
        loops.Add(new[] { innerNodeForNW, $"armN_seg{r}",  $"armNW_seg{r}" });
        loops.Add(new[] { innerNodeForNW, $"armNW_seg{r}", $"armW_seg{r}"  });
    }

    private static MeasurementStep MakeStep(
        int index, string fromId, string toId,
        Orientation orientation, int passId, string instruction)
    {
        return new MeasurementStep
        {
            Index           = index,
            GridCol         = 0,    // not meaningful for Union Jack
            GridRow         = 0,
            Orientation     = orientation,
            PassId          = passId,
            NodeId          = fromId,
            ToNodeId        = toId,
            InstructionText = instruction
        };
    }
}
