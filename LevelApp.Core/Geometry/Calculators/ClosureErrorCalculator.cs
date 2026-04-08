using LevelApp.Core.Interfaces;
using LevelApp.Core.Models;

namespace LevelApp.Core.Geometry.Calculators;

/// <summary>
/// Shared helper that computes primitive closure loop errors and their statistics.
/// Called by both <see cref="LeastSquaresCalculator"/> and <see cref="SequentialIntegrationCalculator"/>.
/// </summary>
internal static class ClosureErrorCalculator
{
    internal static (PrimitiveLoop[] Loops,
                     double Mean, double Median, double Max, double Rms) Compute(
        IReadOnlyList<MeasurementStep> steps,
        double[] stepLensMm,
        IMeasurementStrategy strategy,
        ObjectDefinition definition)
    {
        var stepFwd = new Dictionary<(string, string), int>(steps.Count);
        for (int i = 0; i < steps.Count; i++)
            stepFwd[(steps[i].NodeId, steps[i].ToNodeId)] = i;

        var loopDefs    = strategy.GetPrimitiveLoopNodeIds(definition);
        var loopResults = new List<PrimitiveLoop>(loopDefs.Count);

        foreach (var nodeIds in loopDefs)
        {
            double closureErr = 0.0;
            bool   valid      = true;

            for (int j = 0; j < nodeIds.Count; j++)
            {
                string fromId = nodeIds[j];
                string toId   = nodeIds[(j + 1) % nodeIds.Count];

                if (stepFwd.TryGetValue((fromId, toId), out int si))
                    closureErr += steps[si].Reading!.Value * stepLensMm[si] / 1000.0;
                else if (stepFwd.TryGetValue((toId, fromId), out int siRev))
                    closureErr -= steps[siRev].Reading!.Value * stepLensMm[siRev] / 1000.0;
                else { valid = false; break; }
            }

            if (valid)
                loopResults.Add(new PrimitiveLoop(nodeIds.ToArray(), closureErr));
        }

        if (loopResults.Count == 0)
            return ([], 0.0, 0.0, 0.0, 0.0);

        double[] absErrors = loopResults.Select(l => Math.Abs(l.ClosureErrorMm)).ToArray();
        double[] errors    = loopResults.Select(l => l.ClosureErrorMm).ToArray();

        double mean = errors.Average();
        double max  = absErrors.Max();
        double rms  = Math.Sqrt(errors.Sum(e => e * e) / errors.Length);

        Array.Sort(absErrors);
        int    mid    = absErrors.Length / 2;
        double median = absErrors.Length % 2 == 0
            ? (absErrors[mid - 1] + absErrors[mid]) / 2.0
            : absErrors[mid];

        return ([.. loopResults], mean, median, max, rms);
    }
}
