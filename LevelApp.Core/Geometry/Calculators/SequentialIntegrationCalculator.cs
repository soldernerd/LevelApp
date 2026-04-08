using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.Calculators;

/// <summary>
/// Sequential integration with proportional closure distribution.
///
/// For each pass (row, column, diagonal):
///   1. Integrate: height[node_k] = Σ(readings × step_dist_m) from start of pass
///   2. For each node shared with an already-processed pass, accumulate the
///      closure error (current height − reference height from prior pass)
///   3. Distribute the total accumulated closure error linearly along the pass:
///      correction[k] = −totalError × (k / (pass_nodes − 1))
///   4. Apply corrections
///
/// Final height at each node = average of adjusted heights from all passes
/// through that node.
///
/// This method requires no matrix algebra. It is less optimal than
/// least-squares but provides an independent cross-check.
/// </summary>
public sealed class SequentialIntegrationCalculator : ISurfaceCalculator
{
    private readonly IMeasurementStrategy _strategy;

    public SequentialIntegrationCalculator(IMeasurementStrategy strategy)
        => _strategy = strategy;

    public string MethodId    => "SequentialIntegration";
    public string DisplayName => "Sequential Integration";

    public SurfaceResult Calculate(
        IReadOnlyList<MeasurementStep> steps,
        ObjectDefinition definition,
        CalculationParameters parameters)
    {
        if (steps.Count == 0)
            throw new ArgumentException("Steps list is empty.", nameof(steps));

        // ── Step lengths from physical node positions ─────────────────────────
        double[] stepLensMm = new double[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            var (fx, fy) = _strategy.GetNodePosition(steps[i], definition);
            var (tx, ty) = _strategy.GetToNodePosition(steps[i], definition);
            stepLensMm[i] = Math.Sqrt((tx - fx) * (tx - fx) + (ty - fy) * (ty - fy));
        }

        // ── Group steps into consecutive passes ───────────────────────────────
        // Two steps belong to the same pass iff step[i].ToNodeId == step[i+1].NodeId.
        var passes = GroupIntoPasses(steps);

        // ── Integrate each pass; apply closure correction against prior passes ─
        var passNodes   = new List<string[]>(passes.Count);
        var passHeights = new List<double[]>(passes.Count);

        // nodePassRef[nodeId] = (passIndex, nodeIndex) of the FIRST pass that visited it.
        // Used as the reference height for subsequent passes at the same crossing.
        var nodePassRef = new Dictionary<string, (int PassIdx, int NodeIdx)>();

        int globalStepIdx = 0;   // tracks our position in the flat steps[] list

        foreach (var pass in passes)
        {
            int m = pass.Count;       // number of steps in this pass
            int n = m + 1;            // number of nodes in this pass

            // Collect ordered node IDs for this pass
            string[] nodeIds = new string[n];
            nodeIds[0] = pass[0].NodeId;
            for (int k = 0; k < m; k++)
                nodeIds[k + 1] = pass[k].ToNodeId;

            // Integrate from h = 0 at the start node
            double[] h = new double[n];
            h[0] = 0.0;
            for (int k = 0; k < m; k++)
            {
                double delta = steps[globalStepIdx + k].Reading!.Value
                               * stepLensMm[globalStepIdx + k] / 1000.0;
                h[k + 1] = h[k] + delta;
            }

            // Accumulate closure errors at every crossing with a prior pass
            double totalClosure = 0.0;
            for (int k = 0; k < n; k++)
            {
                if (nodePassRef.TryGetValue(nodeIds[k], out var refEntry))
                {
                    double refH = passHeights[refEntry.PassIdx][refEntry.NodeIdx];
                    totalClosure += h[k] - refH;
                }
            }

            // Apply proportional correction: ramp from 0 at start to −totalClosure at end
            if (n > 1)
            {
                for (int k = 0; k < n; k++)
                    h[k] -= totalClosure * ((double)k / (n - 1));
            }

            // Register this pass
            int passIdx = passNodes.Count;
            passNodes.Add(nodeIds);
            passHeights.Add(h);

            // Record first-visit reference for each node in this pass
            for (int k = 0; k < n; k++)
            {
                if (!nodePassRef.ContainsKey(nodeIds[k]))
                    nodePassRef[nodeIds[k]] = (passIdx, k);
            }

            globalStepIdx += m;
        }

        // ── Final heights: average across all passes that visit each node ─────
        var heightSums   = new Dictionary<string, double>();
        var heightCounts = new Dictionary<string, int>();

        for (int pi = 0; pi < passNodes.Count; pi++)
        {
            string[] ids = passNodes[pi];
            double[] hs  = passHeights[pi];
            for (int k = 0; k < ids.Length; k++)
            {
                heightSums[ids[k]]   = heightSums.GetValueOrDefault(ids[k])   + hs[k];
                heightCounts[ids[k]] = heightCounts.GetValueOrDefault(ids[k]) + 1;
            }
        }

        var nodeHeights = heightSums.ToDictionary(
            kv => kv.Key,
            kv => kv.Value / heightCounts[kv.Key]);

        // Normalize: shift so the minimum height is 0
        double minH = nodeHeights.Values.Min();
        foreach (var key in nodeHeights.Keys.ToList())
            nodeHeights[key] -= minH;

        double flatness = nodeHeights.Values.Max();   // max − 0 = peak-to-valley

        // ── Per-step residuals ────────────────────────────────────────────────
        double[] residuals = new double[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            double hFrom  = nodeHeights[steps[i].NodeId];
            double hTo    = nodeHeights[steps[i].ToNodeId];
            double delta  = steps[i].Reading!.Value * stepLensMm[i] / 1000.0;
            residuals[i]  = (hTo - hFrom) - delta;
        }

        // ── Sigma (RMS with DOF correction) ──────────────────────────────────
        int dof = steps.Count - (nodeHeights.Count - 1);
        double sigma = dof > 0
            ? Math.Sqrt(residuals.Sum(r => r * r) / dof)
            : 0.0;

        // ── Outlier detection ─────────────────────────────────────────────────
        double threshold = parameters.AutoExcludeOutliers
            ? parameters.SigmaThreshold
            : double.MaxValue;

        var flagged = steps
            .Select((s, i) => (s.Index, AbsRes: Math.Abs(residuals[i])))
            .Where(x => x.AbsRes > threshold * sigma)
            .Select(x => x.Index)
            .ToList();

        // ── Closure errors ────────────────────────────────────────────────────
        var (loops, closureMean, closureMedian, closureMax, closureRms) =
            ClosureErrorCalculator.Compute(steps, stepLensMm, _strategy, definition);

        return new SurfaceResult
        {
            NodeHeights        = nodeHeights,
            FlatnessValueMm    = flatness,
            Residuals          = residuals,
            FlaggedStepIndices = flagged,
            SigmaThreshold     = parameters.SigmaThreshold,
            Sigma              = sigma,
            PrimitiveLoops     = loops,
            ClosureErrorMean   = closureMean,
            ClosureErrorMedian = closureMedian,
            ClosureErrorMax    = closureMax,
            ClosureErrorRms    = closureRms
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups steps into consecutive passes. Two consecutive steps belong to
    /// the same pass iff steps[i].ToNodeId == steps[i+1].NodeId.
    /// </summary>
    private static List<List<MeasurementStep>> GroupIntoPasses(
        IReadOnlyList<MeasurementStep> steps)
    {
        var passes  = new List<List<MeasurementStep>>();
        var current = new List<MeasurementStep> { steps[0] };

        for (int i = 1; i < steps.Count; i++)
        {
            if (steps[i].NodeId == steps[i - 1].ToNodeId)
                current.Add(steps[i]);
            else
            {
                passes.Add(current);
                current = new List<MeasurementStep> { steps[i] };
            }
        }
        passes.Add(current);
        return passes;
    }
}
