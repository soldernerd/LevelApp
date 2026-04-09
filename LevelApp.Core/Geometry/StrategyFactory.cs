using LevelApp.Core.Geometry.ParallelWays.Strategies;
using LevelApp.Core.Geometry.SurfacePlate.Strategies;
using LevelApp.Core.Interfaces;

namespace LevelApp.Core.Geometry;

/// <summary>
/// Creates <see cref="IMeasurementStrategy"/> instances by strategy ID.
/// Adding a new strategy = one new class + one line here.
/// </summary>
public static class StrategyFactory
{
    public static IMeasurementStrategy Create(string strategyId) => strategyId switch
    {
        "UnionJack"    => new UnionJackStrategy(),
        "ParallelWays" => new ParallelWaysStrategy(),
        _              => new FullGridStrategy()
    };
}
