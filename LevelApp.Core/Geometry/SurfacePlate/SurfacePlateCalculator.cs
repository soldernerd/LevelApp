using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.SurfacePlate;

/// <summary>
/// Least-squares surface fitting for a rectangular grid measured with a precision level.
///
/// Each step contributes one linear equation:  h[to] − h[from] = reading × stepLen / 1000
///   (reading in mm/m; stepLen in mm; delta in mm)
///
/// The overdetermined system A h = b is solved by normal equations AᵀA h = Aᵀb, assembled
/// directly without materialising the full M×N matrix.  h[0] is fixed to 0 as the height
/// reference by adding a unit constraint row.  Gaussian elimination with partial pivoting
/// solves the resulting N×N system.
///
/// Degrees of freedom = M − (N − 1), where N = cols × rows, M = number of steps.
/// Per-step residuals drive outlier detection: flag steps where |r| > k·σ.
/// </summary>
public sealed class SurfacePlateCalculator : IGeometryCalculator
{
    private readonly int _cols;
    private readonly int _rows;
    private readonly double _stepLenX;       // mm between adjacent columns
    private readonly double _stepLenY;       // mm between adjacent rows
    private readonly double _sigmaThreshold; // k for outlier detection

    public SurfacePlateCalculator(ObjectDefinition definition, double sigmaThreshold = 2.5)
    {
        _cols = Convert.ToInt32(definition.Parameters["columnsCount"]);
        _rows = Convert.ToInt32(definition.Parameters["rowsCount"]);
        double widthMm  = Convert.ToDouble(definition.Parameters["widthMm"]);
        double heightMm = Convert.ToDouble(definition.Parameters["heightMm"]);
        _stepLenX = widthMm  / (_cols - 1);
        _stepLenY = heightMm / (_rows - 1);
        _sigmaThreshold = sigmaThreshold;
    }

    public SurfaceResult Calculate(MeasurementRound round)
    {
        var steps = round.Steps;

        if (steps.Count == 0)
            throw new ArgumentException("Round contains no steps.", nameof(round));

        if (steps.Any(s => !s.Reading.HasValue))
            throw new InvalidOperationException(
                "All steps must have a reading before the surface can be calculated.");

        int n = _rows * _cols;
        int m = steps.Count;

        // ── Build normal equations AᵀA h = Aᵀb ──────────────────────────────
        // Each step contributes: h[to] − h[from] = delta
        //   AᵀA[to,to]     += 1    AᵀA[from,from] += 1
        //   AᵀA[to,from]   -= 1    AᵀA[from,to]   -= 1
        //   Aᵀb[to]  += delta      Aᵀb[from] -= delta

        double[,] AtA = new double[n, n];
        double[]  Atb = new double[n];

        foreach (var step in steps)
        {
            var (from, to, stepLen) = NodeIndices(step);
            double delta = step.Reading!.Value * stepLen / 1000.0;

            AtA[to,   to]   += 1.0;
            AtA[from, from] += 1.0;
            AtA[to,   from] -= 1.0;
            AtA[from, to]   -= 1.0;

            Atb[to]   += delta;
            Atb[from] -= delta;
        }

        // Reference constraint: h[0] = 0  (adds the row [1 0 … 0]·h = 0 to the system)
        AtA[0, 0] += 1.0;

        // ── Solve ─────────────────────────────────────────────────────────────
        double[] h = SolveLinearSystem(AtA, Atb);

        // ── Per-step residuals  r_i = (h[to] − h[from]) − delta ──────────────
        double[] residuals = new double[m];
        for (int i = 0; i < m; i++)
        {
            var (from, to, stepLen) = NodeIndices(steps[i]);
            double delta = steps[i].Reading!.Value * stepLen / 1000.0;
            residuals[i] = (h[to] - h[from]) - delta;
        }

        // ── Sigma (RMS with degrees of freedom) ───────────────────────────────
        int dof = m - (n - 1);
        double sigma = dof > 0
            ? Math.Sqrt(residuals.Sum(r => r * r) / dof)
            : 0.0;

        // ── Outlier detection ─────────────────────────────────────────────────
        var flagged = steps
            .Select((s, i) => (s.Index, AbsResidual: Math.Abs(residuals[i])))
            .Where(x => x.AbsResidual > _sigmaThreshold * sigma)
            .Select(x => x.Index)
            .ToList();

        // ── Height map  [row][col] ────────────────────────────────────────────
        double[][] heightMap = new double[_rows][];
        for (int row = 0; row < _rows; row++)
        {
            heightMap[row] = new double[_cols];
            for (int col = 0; col < _cols; col++)
                heightMap[row][col] = h[row * _cols + col];
        }

        return new SurfaceResult
        {
            HeightMapMm        = heightMap,
            FlatnessValueMm    = h.Max() - h.Min(),
            Residuals          = residuals,
            FlaggedStepIndices = flagged,
            SigmaThreshold     = _sigmaThreshold,
            Sigma              = sigma
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the flat node indices for the "from" and "to" endpoints of a step,
    /// and the physical step length in mm.
    /// GridCol/GridRow on a step is always the "from" endpoint;
    /// Orientation points toward the "to" endpoint.
    /// </summary>
    private (int from, int to, double stepLen) NodeIndices(MeasurementStep step)
    {
        int fromIdx = step.GridRow * _cols + step.GridCol;

        (int toRow, int toCol) = step.Orientation switch
        {
            Orientation.East  => (step.GridRow,     step.GridCol + 1),
            Orientation.West  => (step.GridRow,     step.GridCol - 1),
            Orientation.South => (step.GridRow + 1, step.GridCol),
            Orientation.North => (step.GridRow - 1, step.GridCol),
            _ => throw new ArgumentException($"Unrecognised orientation: {step.Orientation}")
        };

        int toIdx = toRow * _cols + toCol;
        double stepLen = step.Orientation is Orientation.East or Orientation.West
            ? _stepLenX : _stepLenY;

        return (fromIdx, toIdx, stepLen);
    }

    /// <summary>
    /// Solves the N×N linear system A·x = b using Gaussian elimination with partial
    /// pivoting.  Works on a copy of the inputs — originals are not modified.
    /// Throws if the system is singular (disconnected grid or missing passes).
    /// </summary>
    private static double[] SolveLinearSystem(double[,] A, double[] b)
    {
        int n = b.Length;

        // Augmented matrix  [A | b]
        double[,] aug = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) aug[i, j] = A[i, j];
            aug[i, n] = b[i];
        }

        // Forward elimination with partial pivoting
        for (int col = 0; col < n; col++)
        {
            // Find pivot row
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
                    "covering all grid nodes. Ensure both row and column passes are present.");

            for (int row = col + 1; row < n; row++)
            {
                double factor = aug[row, col] / aug[col, col];
                for (int j = col; j <= n; j++)
                    aug[row, j] -= factor * aug[col, j];
            }
        }

        // Back substitution
        double[] x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            x[i] = aug[i, n];
            for (int j = i + 1; j < n; j++)
                x[i] -= aug[i, j] * x[j];
            x[i] /= aug[i, i];
        }

        return x;
    }
}
