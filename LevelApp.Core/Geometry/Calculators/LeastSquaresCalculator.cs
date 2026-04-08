using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.Calculators;

/// <summary>
/// Least-squares surface fitting for any measurement strategy.
///
/// Each step contributes one equation:  h[to] − h[from] = reading × stepDistM
///   (reading in mm/m; stepDistM in metres; result in mm)
///
/// The overdetermined system A h = b is solved via normal equations AᵀA h = Aᵀb.
/// h[node_0] is fixed to 0 as the height reference.
/// Gaussian elimination with partial pivoting solves the N×N system.
///
/// Degrees of freedom = M − (N − 1), where N = unique node count, M = step count.
/// Per-step residuals drive outlier detection: flag steps where |r| > k·σ.
/// </summary>
public sealed class LeastSquaresCalculator : ISurfaceCalculator
{
    private readonly IMeasurementStrategy _strategy;

    public LeastSquaresCalculator(IMeasurementStrategy strategy)
        => _strategy = strategy;

    public string MethodId    => "LeastSquares";
    public string DisplayName => "Least Squares";

    public SurfaceResult Calculate(
        IReadOnlyList<MeasurementStep> steps,
        ObjectDefinition definition,
        CalculationParameters parameters)
    {
        if (steps.Count == 0)
            throw new ArgumentException("Steps list is empty.", nameof(steps));

        if (steps.Any(s => !s.Reading.HasValue))
            throw new InvalidOperationException(
                "All steps must have a reading before the surface can be calculated.");

        // ── Build ordered node list and index map ─────────────────────────────
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

        // ── Pre-compute step lengths from physical positions ───────────────────
        double[] stepLensMm = new double[m];
        for (int i = 0; i < m; i++)
        {
            var (fx, fy) = _strategy.GetNodePosition(steps[i], definition);
            var (tx, ty) = _strategy.GetToNodePosition(steps[i], definition);
            stepLensMm[i] = Math.Sqrt((tx - fx) * (tx - fx) + (ty - fy) * (ty - fy));
        }

        // ── Build normal equations AᵀA h = Aᵀb ───────────────────────────────
        double[,] AtA = new double[n, n];
        double[]  Atb = new double[n];

        for (int i = 0; i < m; i++)
        {
            int from  = nodeIndex[steps[i].NodeId];
            int to    = nodeIndex[steps[i].ToNodeId];
            double delta = steps[i].Reading!.Value * stepLensMm[i] / 1000.0;

            AtA[to,   to]   += 1.0;
            AtA[from, from] += 1.0;
            AtA[to,   from] -= 1.0;
            AtA[from, to]   -= 1.0;

            Atb[to]   += delta;
            Atb[from] -= delta;
        }

        // Reference constraint: h[node_0] = 0
        AtA[0, 0] += 1.0;

        // ── Solve ─────────────────────────────────────────────────────────────
        double[] h = SolveLinearSystem(AtA, Atb);

        // ── Per-step residuals  r_i = (h[to] − h[from]) − delta ──────────────
        double[] residuals = new double[m];
        for (int i = 0; i < m; i++)
        {
            int from  = nodeIndex[steps[i].NodeId];
            int to    = nodeIndex[steps[i].ToNodeId];
            double delta = steps[i].Reading!.Value * stepLensMm[i] / 1000.0;
            residuals[i] = (h[to] - h[from]) - delta;
        }

        // ── Sigma (RMS with degrees of freedom) ───────────────────────────────
        int dof = m - (n - 1);
        double sigma = dof > 0
            ? Math.Sqrt(residuals.Sum(r => r * r) / dof)
            : 0.0;

        // ── Outlier detection ─────────────────────────────────────────────────
        double threshold = parameters.AutoExcludeOutliers
            ? parameters.SigmaThreshold
            : double.MaxValue;

        var flagged = steps
            .Select((s, i) => (s.Index, AbsResidual: Math.Abs(residuals[i])))
            .Where(x => x.AbsResidual > threshold * sigma)
            .Select(x => x.Index)
            .ToList();

        // ── NodeHeights dictionary ────────────────────────────────────────────
        var nodeHeights = new Dictionary<string, double>(n);
        for (int i = 0; i < n; i++)
            nodeHeights[nodeOrder[i]] = h[i];

        // ── Closure errors ────────────────────────────────────────────────────
        var (loops, closureMean, closureMedian, closureMax, closureRms) =
            ClosureErrorCalculator.Compute(steps, stepLensMm, _strategy, definition);

        return new SurfaceResult
        {
            NodeHeights        = nodeHeights,
            FlatnessValueMm    = h.Max() - h.Min(),
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

    // ── Private helpers ───────────────────────────────────────────────────────

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
                    "Singular matrix — the step set does not form a connected graph " +
                    "covering all nodes. Ensure all required passes are present.");

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
