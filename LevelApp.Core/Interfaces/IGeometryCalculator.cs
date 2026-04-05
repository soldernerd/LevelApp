using LevelApp.Core.Models;

namespace LevelApp.Core.Interfaces;

/// <summary>
/// Performs the least-squares surface fit and outlier detection for a specific geometry type.
/// Each geometry module provides its own implementation via <see cref="IGeometryModule.CreateCalculator"/>.
/// </summary>
public interface IGeometryCalculator
{
    SurfaceResult Calculate(MeasurementRound round);
}
