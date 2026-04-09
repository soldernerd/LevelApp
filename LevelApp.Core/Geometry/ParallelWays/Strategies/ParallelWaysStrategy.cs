using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.ParallelWays.Strategies;

/// <summary>
/// Generates the ordered flat step list for a Parallel Ways measurement.
///
/// Parameters are read from <see cref="ObjectDefinition.Parameters"/>:
///   "orientation"       → WaysOrientation (Horizontal | Vertical)
///   "referenceRailIndex"→ int
///   "rails"             → List&lt;RailDefinition&gt;
///   "tasks"             → List&lt;ParallelWaysTask&gt;
///   "driftCorrection"   → DriftCorrectionMethod (string)
///   "solverMode"        → SolverMode (string)
///
/// Node IDs use the scheme:
///   Along-rail and bridge from-nodes: "rail{r}_sta{s}"
///
/// GridCol = station index, GridRow = rail index (AlongRail) or
/// RailIndexA * 100 + RailIndexB (Bridge — documented here).
///
/// Orientation:
///   Horizontal ways: forward = East, return = West, bridge = North/South
///   Vertical ways:   forward = South, return = North, bridge = East/West
/// </summary>
public sealed class ParallelWaysStrategy : IMeasurementStrategy
{
    public string StrategyId  => "ParallelWays";
    public string DisplayName => "Parallel Ways";

    // ── IMeasurementStrategy ──────────────────────────────────────────────────

    public IReadOnlyList<MeasurementStep> GenerateSteps(ObjectDefinition definition)
    {
        var pwp   = ParallelWaysParameters.From(definition.Parameters);
        var strat = ParallelWaysStrategyParameters.From(definition.Parameters);

        if (pwp.Rails.Count < 2)
            throw new ArgumentException("Parallel Ways requires at least 2 rails.", nameof(definition));
        if (strat.Tasks.Count == 0)
            throw new ArgumentException("Parallel Ways requires at least one task.", nameof(definition));

        var steps  = new List<MeasurementStep>();
        int index  = 0;
        int passId = 0;

        bool horizontal = pwp.Orientation == WaysOrientation.Horizontal;

        foreach (var task in strat.Tasks)
        {
            if (task.TaskType == TaskType.AlongRail)
            {
                int    r     = task.RailIndexA;
                var    rail  = pwp.Rails[r];
                int    n     = task.GetStepCount(rail.LengthMm);   // number of intervals
                double dist  = task.StepDistanceMm;

                // ── Forward pass ──────────────────────────────────────────────
                Orientation fwdOri = horizontal ? Orientation.East : Orientation.South;
                for (int s = 0; s < n; s++)
                {
                    steps.Add(MakeAlongRail(index++, r, s, s + 1, fwdOri,
                        task.PassDirection == PassDirection.ForwardAndReturn
                            ? PassPhase.Forward
                            : PassPhase.NotApplicable,
                        passId, rail.Label, dist, horizontal));
                }
                passId++;

                // ── Return pass (if requested) ────────────────────────────────
                if (task.PassDirection == PassDirection.ForwardAndReturn)
                {
                    Orientation retOri = horizontal ? Orientation.West : Orientation.North;
                    for (int s = n; s > 0; s--)
                    {
                        steps.Add(MakeAlongRail(index++, r, s, s - 1, retOri,
                            PassPhase.Return, passId, rail.Label, dist, horizontal));
                    }
                    passId++;
                }
            }
            else // Bridge
            {
                int    rA   = task.RailIndexA;
                int    rB   = task.RailIndexB;
                var    railA = pwp.Rails[rA];
                var    railB = pwp.Rails[rB];

                // Use the shorter of the two rails for station count
                double refLen = Math.Min(railA.LengthMm, railB.LengthMm);
                int    n      = task.GetStepCount(refLen);
                double dist   = task.StepDistanceMm;

                // Bridge orientation = perpendicular to travel direction
                // Determine direction based on lateral separation sign
                bool bToHigherLateral = railB.LateralSeparationMm >= railA.LateralSeparationMm;
                Orientation fwdBri = horizontal
                    ? (bToHigherLateral ? Orientation.North : Orientation.South)
                    : (bToHigherLateral ? Orientation.East  : Orientation.West);

                for (int s = 0; s <= n; s++)
                {
                    steps.Add(MakeBridge(index++, rA, rB, s,
                        task.PassDirection == PassDirection.ForwardAndReturn
                            ? PassPhase.Forward
                            : PassPhase.NotApplicable,
                        fwdBri, passId, railA.Label, railB.Label, dist));
                }
                passId++;

                if (task.PassDirection == PassDirection.ForwardAndReturn)
                {
                    Orientation retBri = horizontal
                        ? (bToHigherLateral ? Orientation.South : Orientation.North)
                        : (bToHigherLateral ? Orientation.West  : Orientation.East);

                    for (int s = n; s >= 0; s--)
                    {
                        steps.Add(MakeBridge(index++, rB, rA, s,
                            PassPhase.Return, retBri, passId, railB.Label, railA.Label, dist));
                    }
                    passId++;
                }
            }
        }

        return steps.AsReadOnly();
    }

    public (double X, double Y) GetNodePosition(MeasurementStep step, ObjectDefinition definition)
        => NodePositionById(step.NodeId, definition);

    public (double X, double Y) GetToNodePosition(MeasurementStep step, ObjectDefinition definition)
        => NodePositionById(step.ToNodeId, definition);

    public IReadOnlyList<IReadOnlyList<string>> GetPrimitiveLoopNodeIds(ObjectDefinition definition)
        => [];   // No closure loops for Parallel Ways

    // ── Public helper — used by display / renderer ────────────────────────────

    /// <summary>
    /// Returns the physical position (X, Y) in mm for a node identified by
    /// <paramref name="nodeId"/> given the object definition.
    /// </summary>
    public static (double X, double Y) NodePositionById(
        string nodeId, ObjectDefinition definition)
    {
        // Parse "rail{r}_sta{s}"
        (int r, int s) = ParseNodeId(nodeId);

        var pwp   = ParallelWaysParameters.From(definition.Parameters);
        var strat = ParallelWaysStrategyParameters.From(definition.Parameters);

        var rail = pwp.Rails[r];

        // Find step distance: prefer AlongRail task for this rail, fall back to first task
        double stepDist = strat.Tasks
            .Where(t => t.RailIndexA == r)
            .Select(t => (double?)t.StepDistanceMm)
            .FirstOrDefault()
            ?? strat.Tasks.Select(t => (double?)t.StepDistanceMm).FirstOrDefault()
            ?? 1.0;

        double axialPos   = rail.AxialOffsetMm + s * stepDist;
        double lateralPos = rail.LateralSeparationMm;

        // Horizontal ways: X = axial direction, Y = lateral (cross-rail)
        // Vertical ways:   X = lateral, Y = axial
        return pwp.Orientation == WaysOrientation.Horizontal
            ? (axialPos, lateralPos)
            : (lateralPos, axialPos);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static MeasurementStep MakeAlongRail(
        int index, int rail, int fromSta, int toSta,
        Orientation orientation, PassPhase passPhase,
        int passId, string railLabel, double stepDist, bool horizontal)
    {
        return new MeasurementStep
        {
            Index           = index,
            GridCol         = fromSta,
            GridRow         = rail,
            Orientation     = orientation,
            PassPhase       = passPhase,
            PassId          = passId,
            NodeId          = RailNodeId(rail, fromSta),
            ToNodeId        = RailNodeId(rail, toSta),
            InstructionText = BuildAlongRailInstruction(
                railLabel, fromSta, toSta, orientation, horizontal)
        };
    }

    private static MeasurementStep MakeBridge(
        int index, int rFrom, int rTo, int station,
        PassPhase passPhase, Orientation orientation,
        int passId, string labelFrom, string labelTo, double stepDist)
    {
        return new MeasurementStep
        {
            Index           = index,
            GridCol         = station,
            GridRow         = rFrom * 100 + rTo,   // encoded bridge pair — see class doc
            Orientation     = orientation,
            PassPhase       = passPhase,
            PassId          = passId,
            NodeId          = RailNodeId(rFrom, station),
            ToNodeId        = RailNodeId(rTo,   station),
            InstructionText = $"Bridge {labelFrom}\u2194{labelTo} — station {station + 1}, spanning {labelFrom} \u2192 {labelTo}"
        };
    }

    private static string BuildAlongRailInstruction(
        string label, int fromSta, int toSta, Orientation ori, bool horizontal)
    {
        string dir = ori switch
        {
            Orientation.East  => "East",
            Orientation.West  => "West",
            Orientation.South => "South",
            Orientation.North => "North",
            _                 => ori.ToString()
        };
        return $"Rail \u2018{label}\u2019 \u2014 station {fromSta + 1} \u2192 {toSta + 1}, facing {dir}";
    }

    private static string RailNodeId(int rail, int station)
        => $"rail{rail}_sta{station}";

    /// <summary>Parses "rail{r}_sta{s}" → (r, s).</summary>
    public static (int Rail, int Station) ParseNodeId(string nodeId)
    {
        // Format: "rail{r}_sta{s}"
        var parts = nodeId.Split('_');
        int r = int.Parse(parts[0][4..]);  // skip "rail"
        int s = int.Parse(parts[1][3..]);  // skip "sta"
        return (r, s);
    }
}
