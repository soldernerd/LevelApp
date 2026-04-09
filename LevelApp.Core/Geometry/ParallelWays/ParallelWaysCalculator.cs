using LevelApp.Core.Geometry.ParallelWays.Strategies;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.ParallelWays;

/// <summary>
/// Computes a <see cref="ParallelWaysResult"/> from a completed set of
/// Parallel Ways measurement steps.
///
/// Two solver modes (selected by <see cref="SolverMode"/> in the definition parameters):
///
/// <b>GlobalLeastSquares</b> — builds one system of normal equations covering every
/// step (along-rail forward, along-rail return, bridge). Datum: first station of the
/// reference rail = 0. After solving, best-fit line is removed per rail to give
/// straightness profiles. Parallelism is computed from the solved heights before
/// line removal.
///
/// <b>IndependentThenReconcile</b> — each rail is integrated from its forward-pass
/// readings only (drift-corrected when a return pass is present); bridge readings
/// are then used to check consistency and contribute residuals.
/// </summary>
public sealed class ParallelWaysCalculator
{
    private readonly ParallelWaysStrategy _strategy = new();

    public ParallelWaysResult Calculate(
        IReadOnlyList<MeasurementStep> steps,
        ObjectDefinition               definition,
        CalculationParameters          parameters)
    {
        if (steps.Count == 0)
            throw new ArgumentException("Steps list is empty.", nameof(steps));
        if (steps.Any(s => !s.Reading.HasValue))
            throw new InvalidOperationException(
                "All steps must have a reading before calculation.");

        var pwp   = ParallelWaysParameters.From(definition.Parameters);
        var strat = ParallelWaysStrategyParameters.From(definition.Parameters);

        return strat.SolverMode == SolverMode.IndependentThenReconcile
            ? CalcIndependent(steps, pwp, strat, parameters)
            : CalcGlobalLeastSquares(steps, pwp, strat, parameters);
    }

    // ── Global least-squares ──────────────────────────────────────────────────

    private ParallelWaysResult CalcGlobalLeastSquares(
        IReadOnlyList<MeasurementStep> steps,
        ParallelWaysParameters         pwp,
        ParallelWaysStrategyParameters strat,
        CalculationParameters          parameters)
    {
        // ── Build ordered node list ───────────────────────────────────────────
        var nodeOrder = new List<string>();
        var nodeIndex = new Dictionary<string, int>();

        void RegisterNode(string id)
        {
            if (!nodeIndex.ContainsKey(id))
            {
                nodeIndex[id] = nodeOrder.Count;
                nodeOrder.Add(id);
            }
        }

        foreach (var step in steps)
        {
            RegisterNode(step.NodeId);
            RegisterNode(step.ToNodeId);
        }

        int n = nodeOrder.Count;
        int m = steps.Count;

        // ── Compute step lengths ──────────────────────────────────────────────
        double[] stepLens = ComputeStepLengths(steps, pwp, strat);

        // ── Build normal equations ────────────────────────────────────────────
        double[,] AtA = new double[n, n];
        double[]  Atb = new double[n];

        for (int i = 0; i < m; i++)
        {
            int    from  = nodeIndex[steps[i].NodeId];
            int    to    = nodeIndex[steps[i].ToNodeId];
            double delta = steps[i].Reading!.Value * stepLens[i] / 1000.0;

            AtA[to,   to]   += 1.0;
            AtA[from, from] += 1.0;
            AtA[to,   from] -= 1.0;
            AtA[from, to]   -= 1.0;

            Atb[to]   += delta;
            Atb[from] -= delta;
        }

        // Datum: first station of reference rail = 0
        string refDatum = $"rail{pwp.ReferenceRailIndex}_sta0";
        int    refIdx   = nodeIndex.TryGetValue(refDatum, out int ri) ? ri : 0;
        AtA[refIdx, refIdx] += 1.0;

        // ── Solve ─────────────────────────────────────────────────────────────
        double[] h = SolveLinearSystem(AtA, Atb);

        var nodeHeights = new Dictionary<string, double>(n);
        for (int i = 0; i < n; i++)
            nodeHeights[nodeOrder[i]] = h[i];

        // ── Per-step residuals ────────────────────────────────────────────────
        double[] residuals = ComputeResiduals(steps, nodeHeights, stepLens);

        return BuildResult(steps, nodeHeights, residuals, pwp, strat, parameters);
    }

    // ── Independent-then-reconcile ────────────────────────────────────────────

    private ParallelWaysResult CalcIndependent(
        IReadOnlyList<MeasurementStep> steps,
        ParallelWaysParameters         pwp,
        ParallelWaysStrategyParameters strat,
        CalculationParameters          parameters)
    {
        var nodeHeights = new Dictionary<string, double>();

        // ── Group steps by task in order ──────────────────────────────────────
        int stepPtr = 0;

        foreach (var task in strat.Tasks)
        {
            if (task.TaskType == TaskType.AlongRail)
            {
                int    r    = task.RailIndexA;
                var    rail = pwp.Rails[r];
                int    nInt = task.GetStepCount(rail.LengthMm);  // intervals
                double dist = task.StepDistanceMm;

                if (task.PassDirection == PassDirection.SinglePass)
                {
                    // Integrate forward pass
                    double hCur = 0.0;
                    for (int s = 0; s < nInt; s++)
                    {
                        var step = steps[stepPtr + s];
                        nodeHeights.TryAdd($"rail{r}_sta{s}", hCur);
                        hCur += step.Reading!.Value * dist / 1000.0;
                    }
                    nodeHeights.TryAdd($"rail{r}_sta{nInt}", hCur);
                    stepPtr += nInt;
                }
                else // ForwardAndReturn
                {
                    // Integrate forward
                    double[] hFwd = new double[nInt + 1];
                    hFwd[0] = 0.0;
                    for (int s = 0; s < nInt; s++)
                        hFwd[s + 1] = hFwd[s] + steps[stepPtr + s].Reading!.Value * dist / 1000.0;

                    // Integrate return (return steps go from sta(N) → sta(N-1) → ... → sta(0))
                    double[] hRet = new double[nInt + 1];
                    hRet[nInt] = 0.0;
                    for (int j = 0; j < nInt; j++)
                        hRet[nInt - j - 1] = hRet[nInt - j] + steps[stepPtr + nInt + j].Reading!.Value * dist / 1000.0;

                    double[] hCorr = ApplyDriftCorrection(hFwd, hRet, strat.DriftCorrection);

                    for (int s = 0; s <= nInt; s++)
                        nodeHeights.TryAdd($"rail{r}_sta{s}", hCorr[s]);

                    stepPtr += nInt * 2;
                }
            }
            else // Bridge — skip here; bridge residuals computed later
            {
                int    rA   = task.RailIndexA;
                int    rB   = task.RailIndexB;
                var    railA = pwp.Rails[rA];
                var    railB = pwp.Rails[rB];
                double refLen = Math.Min(railA.LengthMm, railB.LengthMm);
                int    n     = task.GetStepCount(refLen);
                int    bridgeSteps = (task.PassDirection == PassDirection.ForwardAndReturn)
                    ? (n + 1) * 2 : (n + 1);

                // Ensure bridge stations are registered even if not yet seen
                for (int s = 0; s <= n; s++)
                {
                    nodeHeights.TryAdd($"rail{rA}_sta{s}", 0.0);
                    nodeHeights.TryAdd($"rail{rB}_sta{s}", 0.0);
                }

                stepPtr += bridgeSteps;
            }
        }

        // ── Compute step lengths and residuals ────────────────────────────────
        double[] stepLens = ComputeStepLengths(steps, pwp, strat);
        double[] residuals = ComputeResiduals(steps, nodeHeights, stepLens);

        return BuildResult(steps, nodeHeights, residuals, pwp, strat, parameters);
    }

    // ── Shared post-processing ────────────────────────────────────────────────

    private static ParallelWaysResult BuildResult(
        IReadOnlyList<MeasurementStep> steps,
        Dictionary<string, double>     nodeHeights,
        double[]                       residuals,
        ParallelWaysParameters         pwp,
        ParallelWaysStrategyParameters strat,
        CalculationParameters          parameters)
    {
        int m = steps.Count;

        // ── Sigma (simple RMS, no DOF) ────────────────────────────────────────
        double rms = m > 0
            ? Math.Sqrt(residuals.Sum(r => r * r) / m)
            : 0.0;

        // ── Outlier detection ─────────────────────────────────────────────────
        double threshold = parameters.AutoExcludeOutliers
            ? parameters.SigmaThreshold * rms
            : double.MaxValue;

        var flagged = Enumerable.Range(0, m)
            .Where(i => Math.Abs(residuals[i]) > threshold)
            .Select(i => steps[i].Index)
            .ToArray();

        // ── Extract heights per rail (sorted by station index) ────────────────
        // Group all node heights by rail index
        var railHeightMap = new Dictionary<int, SortedDictionary<int, double>>();
        foreach (var (nodeId, height) in nodeHeights)
        {
            var (rail, sta) = ParallelWaysStrategy.ParseNodeId(nodeId);
            if (!railHeightMap.TryGetValue(rail, out var map))
                railHeightMap[rail] = map = [];
            map[sta] = height;
        }

        // ── Rail profiles (straightness after best-fit line removal) ──────────
        var railProfiles = new List<RailProfile>();

        // Keep a copy of solved heights for parallelism computation (before line removal)
        var solvedHeights = new Dictionary<int, double[]>();

        foreach (var (railIdx, staMap) in railHeightMap.OrderBy(kv => kv.Key))
        {
            // Find step distance for this rail
            double stepDist = strat.Tasks
                .Where(t => t.RailIndexA == railIdx)
                .Select(t => (double?)t.StepDistanceMm)
                .FirstOrDefault()
                ?? strat.Tasks.FirstOrDefault()?.StepDistanceMm
                ?? 1.0;

            var    rail      = pwp.Rails[railIdx];
            int[]  stations  = [.. staMap.Keys];
            double[] heights = [.. staMap.Values];
            double[] positions = stations
                .Select(s => rail.AxialOffsetMm + s * stepDist)
                .ToArray();

            solvedHeights[railIdx] = heights;

            // Best-fit line y = a + b*x
            (double a, double b) = FitLine(positions, heights);
            double[] straightness = heights
                .Select((h, i) => h - (a + b * positions[i]))
                .ToArray();

            railProfiles.Add(new RailProfile
            {
                RailIndex         = railIdx,
                HeightProfileMm   = straightness,
                StationPositionsMm = positions,
                StraightnessValueMm = straightness.Length > 0
                    ? straightness.Max() - straightness.Min()
                    : 0.0
            });
        }

        // ── Parallelism profiles (from solved heights before line removal) ─────
        var parallelismProfiles = new List<ParallelismProfile>();

        // Generate profiles for every rail pair that appears in bridge tasks
        var bridgePairs = strat.Tasks
            .Where(t => t.TaskType == TaskType.Bridge)
            .Select(t => (Math.Min(t.RailIndexA, t.RailIndexB),
                          Math.Max(t.RailIndexA, t.RailIndexB)))
            .Distinct();

        foreach (var (rA, rB) in bridgePairs)
        {
            if (!railHeightMap.TryGetValue(rA, out var mapA)) continue;
            if (!railHeightMap.TryGetValue(rB, out var mapB)) continue;

            // Common stations
            var common = mapA.Keys.Intersect(mapB.Keys).OrderBy(s => s).ToArray();
            if (common.Length == 0) continue;

            double stepDist = strat.Tasks
                .Where(t => t.RailIndexA == rA || t.RailIndexA == rB)
                .Select(t => (double?)t.StepDistanceMm)
                .FirstOrDefault()
                ?? 1.0;

            var railA = pwp.Rails[rA];
            double[] deviations = common
                .Select(s => mapB[s] - mapA[s])
                .ToArray();
            double[] positions = common
                .Select(s => railA.AxialOffsetMm + s * stepDist)
                .ToArray();

            parallelismProfiles.Add(new ParallelismProfile
            {
                RailIndexA          = rA,
                RailIndexB          = rB,
                DeviationMm         = deviations,
                StationPositionsMm  = positions,
                ParallelismValueMm  = deviations.Length > 0
                    ? deviations.Max() - deviations.Min()
                    : 0.0
            });
        }

        // If no bridge tasks but multiple rails, add parallelism between adjacent rail pairs
        if (!parallelismProfiles.Any() && railHeightMap.Count >= 2)
        {
            var railIndices = railHeightMap.Keys.OrderBy(k => k).ToList();
            for (int i = 0; i < railIndices.Count - 1; i++)
            {
                int rA = railIndices[i];
                int rB = railIndices[i + 1];
                var mapA = railHeightMap[rA];
                var mapB = railHeightMap[rB];

                var common = mapA.Keys.Intersect(mapB.Keys).OrderBy(s => s).ToArray();
                if (common.Length == 0) continue;

                double stepDist = strat.Tasks
                    .Where(t => t.RailIndexA == rA || t.RailIndexA == rB)
                    .Select(t => (double?)t.StepDistanceMm)
                    .FirstOrDefault()
                    ?? 1.0;

                var railA = pwp.Rails[rA];
                double[] deviations = common.Select(s => mapB[s] - mapA[s]).ToArray();
                double[] positions  = common.Select(s => railA.AxialOffsetMm + s * stepDist).ToArray();

                parallelismProfiles.Add(new ParallelismProfile
                {
                    RailIndexA         = rA,
                    RailIndexB         = rB,
                    DeviationMm        = deviations,
                    StationPositionsMm = positions,
                    ParallelismValueMm = deviations.Length > 0
                        ? deviations.Max() - deviations.Min()
                        : 0.0
                });
            }
        }

        return new ParallelWaysResult
        {
            RailProfiles        = railProfiles,
            ParallelismProfiles = parallelismProfiles,
            Residuals           = residuals,
            FlaggedStepIndices  = flagged,
            SigmaThreshold      = parameters.SigmaThreshold,
            ResidualRms         = rms
        };
    }

    // ── Step-length computation ───────────────────────────────────────────────

    private double[] ComputeStepLengths(
        IReadOnlyList<MeasurementStep> steps,
        ParallelWaysParameters         pwp,
        ParallelWaysStrategyParameters strat)
    {
        var lens = new double[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var (rFrom, sFrom) = ParallelWaysStrategy.ParseNodeId(step.NodeId);
            var (rTo,   sTo)   = ParallelWaysStrategy.ParseNodeId(step.ToNodeId);

            if (rFrom == rTo) // along-rail: step length = task's StepDistanceMm
            {
                lens[i] = strat.Tasks
                    .Where(t => t.TaskType == TaskType.AlongRail && t.RailIndexA == rFrom)
                    .Select(t => (double?)t.StepDistanceMm)
                    .FirstOrDefault()
                    ?? 1.0;
            }
            else // bridge: actual gauge between rails
            {
                var railA = pwp.Rails[rFrom];
                var railB = pwp.Rails[rTo];
                double latDiff  = railB.LateralSeparationMm - railA.LateralSeparationMm;
                double vertDiff = railB.VerticalOffsetMm    - railA.VerticalOffsetMm;
                lens[i] = Math.Sqrt(latDiff * latDiff + vertDiff * vertDiff);
                if (lens[i] < 1e-6) lens[i] = 1.0; // guard against zero
            }
        }
        return lens;
    }

    // ── Residual computation ──────────────────────────────────────────────────

    private static double[] ComputeResiduals(
        IReadOnlyList<MeasurementStep> steps,
        Dictionary<string, double>     nodeHeights,
        double[]                       stepLens)
    {
        var res = new double[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            if (!nodeHeights.TryGetValue(steps[i].NodeId,   out double hFrom)) continue;
            if (!nodeHeights.TryGetValue(steps[i].ToNodeId, out double hTo))   continue;

            double delta = steps[i].Reading!.Value * stepLens[i] / 1000.0;
            res[i] = (hTo - hFrom) - delta;
        }
        return res;
    }

    // ── Drift correction ──────────────────────────────────────────────────────

    private static double[] ApplyDriftCorrection(
        double[]             hFwd,
        double[]             hRet,
        DriftCorrectionMethod method)
    {
        int n = hFwd.Length;

        switch (method)
        {
            case DriftCorrectionMethod.FirstStationAnchor:
                return hFwd;

            case DriftCorrectionMethod.LinearDriftCorrection:
            {
                // Shift hRet so station 0 = 0 (hRet is referenced to station N = 0)
                double offset = hRet[0]; // hRet[0] is the accumulated return profile at station 0
                // Corrected = average of forward and negated-shifted return
                double[] corr = new double[n];
                for (int i = 0; i < n; i++)
                    corr[i] = (hFwd[i] + (-hRet[i] + offset)) / 2.0;
                return corr;
            }

            case DriftCorrectionMethod.LeastSquares:
            default:
            {
                // Treat forward and return as two independent observations of the same heights.
                // Solve a mini-LS system: forward gives h[s+1] - h[s] = delta_f[s]
                //                         return  gives h[s]   - h[s+1] = delta_r[j] (j = N-1-s)
                // This reduces to averaging forward and reversed-return heights.
                double[] corr = new double[n];
                double retOffset = hRet[0]; // shift hRet so station 0 is the reference
                for (int i = 0; i < n; i++)
                    corr[i] = (hFwd[i] + (-hRet[i] + retOffset)) / 2.0;
                return corr;
            }
        }
    }

    // ── Best-fit line ─────────────────────────────────────────────────────────

    private static (double a, double b) FitLine(double[] x, double[] y)
    {
        int n = x.Length;
        if (n == 0) return (0, 0);
        if (n == 1) return (y[0], 0);

        double sumX  = x.Sum();
        double sumY  = y.Sum();
        double sumXX = x.Sum(v => v * v);
        double sumXY = x.Zip(y, (xi, yi) => xi * yi).Sum();

        double denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-12) return (sumY / n, 0);

        double b = (n * sumXY - sumX * sumY) / denom;
        double a = (sumY - b * sumX) / n;
        return (a, b);
    }

    // ── Linear solver (Gaussian elimination with partial pivoting) ────────────

    private static double[] SolveLinearSystem(double[,] A, double[] b)
    {
        int n = b.Length;
        double[,] aug = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) aug[i, j] = A[i, j];
            aug[i, n] = b[i];
        }

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(aug[row, col]) > Math.Abs(aug[pivot, col]))
                    pivot = row;

            if (pivot != col)
                for (int j = 0; j <= n; j++)
                    (aug[col, j], aug[pivot, j]) = (aug[pivot, j], aug[col, j]);

            if (Math.Abs(aug[col, col]) < 1e-14)
                throw new InvalidOperationException(
                    "Singular matrix — the step set does not connect all nodes. " +
                    "Ensure at least one AlongRail task is present per rail.");

            for (int row = col + 1; row < n; row++)
            {
                double factor = aug[row, col] / aug[col, col];
                for (int j = col; j <= n; j++)
                    aug[row, j] -= factor * aug[col, j];
            }
        }

        double[] x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            x[i] = aug[i, n];
            for (int j = i + 1; j < n; j++) x[i] -= aug[i, j] * x[j];
            x[i] /= aug[i, i];
        }
        return x;
    }
}
