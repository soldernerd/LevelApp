using LevelApp.Core.Geometry.Calculators;
using LevelApp.Core.Interfaces;

namespace LevelApp.Core.Geometry;

/// <summary>
/// Creates <see cref="ISurfaceCalculator"/> instances by method ID.
/// Adding a new algorithm = one new class + one line here.
/// </summary>
public static class CalculatorFactory
{
    public static ISurfaceCalculator Create(string methodId, IMeasurementStrategy strategy) =>
        methodId == "SequentialIntegration"
            ? new SequentialIntegrationCalculator(strategy)
            : new LeastSquaresCalculator(strategy);
}
