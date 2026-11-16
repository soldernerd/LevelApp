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
/// Step sequence (the instrument is never turned by 180°, so a differential measurement stays possible):
///   Pass 0  — H arm:    W-tip → center → E-tip  (East,      2×segments steps)
///   Pass 1  — V arm:    N-tip → center → S-tip  (South,     2×segments steps)
///   Pass 2  — SE diag:  NW-tip → SE-tip          (SouthEast, 2×segments steps)
///   Pass 3  — NE diag:  NE-tip → SW-tip          (SouthWest, 2×segments steps)
///   Rings option:
///     None          — no further passes
///     Circumference — pass 4: one ring at r = segments (8 steps)
///     Full          — passes 4…3+segments: rings r = 1…segments (8 steps each)
///   Every ring runs its horizontal edges East (bottom edge, then top edge) and its vertical edges South
///   (left edge, then right edge): 4 × 2 steps, a ring is not walked around.
///
/// With the optional <c>reverseDirection</c> parameter the whole sequence is mirrored through the plate
/// centre (E↔W, N↔S, NE↔SW, NW↔SE): the arms start at the opposite tips and face West / North / North-West /
/// North-East, the ring edges face the other way. The order of the steps is unchanged.
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

        bool reversed = WalkDirection.IsReversed(definition);

        int ringStepCount = ringsOption switch
        {
            UnionJackRings.None          => 0,
            UnionJackRings.Circumference => 1,   // single ring at r = segments
            UnionJackRings.Full          => segments,
            _                            => 0
        };

        var steps = new List<MeasurementStep>(8 * segments + 8 * ringStepCount);
        int index = 0;

        // Everything below is written for the normal direction. In a reversed walk every arm / diagonal name
        // and every orientation is replaced by its point mirror before the step is created.
        string Dir(string d) => reversed ? MirrorDirection(d) : d;
        Orientation Facing(Orientation o) => reversed ? WalkDirection.Opposite(o) : o;
        string Arrow(Orientation o) => Facing(o) switch
        {
            Orientation.East      => "East",
            Orientation.West      => "West",
            Orientation.North     => "North",
            Orientation.South     => "South",
            Orientation.NorthEast => "NE",
            Orientation.NorthWest => "NW",
            Orientation.SouthEast => "SE",
            _                     => "SW"
        };

        // One arm pass: tip A → center → tip B along the axis (dirA is the start tip, dirB the end tip).
        void ArmPass(int pass, string label, string dirA, string dirB, Orientation orientation)
        {
            string a = Dir(dirA), b = Dir(dirB);
            for (int k = segments; k >= 1; k--)
            {
                string from = $"arm{a}_seg{k}";
                string to   = k == 1 ? "center" : $"arm{a}_seg{k - 1}";
                steps.Add(MakeStep(index++, from, to, Facing(orientation), pass,
                    $"{label} — {a} side, seg {k}→{k - 1}, facing {Arrow(orientation)}"));
            }
            for (int k = 1; k <= segments; k++)
            {
                string from = k == 1 ? "center" : $"arm{b}_seg{k - 1}";
                string to   = $"arm{b}_seg{k}";
                steps.Add(MakeStep(index++, from, to, Facing(orientation), pass,
                    $"{label} — {b} side, seg {k - 1}→{k}, facing {Arrow(orientation)}"));
            }
        }

        ArmPass(0, "H arm",   "W",  "E",  Orientation.East);
        ArmPass(1, "V arm",   "N",  "S",  Orientation.South);
        ArmPass(2, "SE diag", "NW", "SE", Orientation.SouthEast);
        ArmPass(3, "NE diag", "NE", "SW", Orientation.SouthWest);

        // ── Ring passes (8 steps each, one-way edges) ─────────────────────────
        // Circumference: one ring at r = segments.
        // Full:          rings r = 1 … segments.
        int ringPassId  = 4;
        int ringStart   = ringsOption == UnionJackRings.Circumference ? segments : 1;
        int ringEnd     = ringsOption == UnionJackRings.None          ? 0        : segments;

        // One edge of a ring: three nodes from → mid → to in the given orientation.
        void Edge(int r, string a, string m, string b, Orientation orientation)
        {
            string da = Dir(a), dm = Dir(m), db = Dir(b);
            string text = Arrow(orientation);
            steps.Add(MakeStep(index++, $"arm{da}_seg{r}", $"arm{dm}_seg{r}", Facing(orientation), ringPassId,
                $"Ring {r} — {da}→{dm} ({text})"));
            steps.Add(MakeStep(index++, $"arm{dm}_seg{r}", $"arm{db}_seg{r}", Facing(orientation), ringPassId,
                $"Ring {r} — {dm}→{db} ({text})"));
        }

        for (int r = ringStart; r <= ringEnd; r++)
        {
            Edge(r, "SW", "S", "SE", Orientation.East);    // bottom edge
            Edge(r, "NW", "N", "NE", Orientation.East);    // top edge
            Edge(r, "NW", "W", "SW", Orientation.South);   // left edge
            Edge(r, "NE", "E", "SE", Orientation.South);   // right edge
            ringPassId++;
        }

        return steps.AsReadOnly();
    }

    /// <summary>Point mirror of an arm direction: E↔W, N↔S, NE↔SW, NW↔SE.</summary>
    private static string MirrorDirection(string d) => d switch
    {
        "E"  => "W",  "W"  => "E",
        "N"  => "S",  "S"  => "N",
        "NE" => "SW", "SW" => "NE",
        "NW" => "SE", "SE" => "NW",
        _    => d
    };

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
