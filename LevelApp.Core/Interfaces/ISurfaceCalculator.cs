using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

public interface ISurfaceCalculator
{
    string MethodId { get; }
    string DisplayName { get; }
    SurfaceResult Calculate(
        IReadOnlyList<MeasurementStep> steps,
        ObjectDefinition definition,
        CalculationParameters parameters);
}
